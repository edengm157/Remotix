using H264Sharp;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace Receiver
{
    internal class VideoDecoder : IDisposable
    {
        private H264Decoder _videoDecoder;
        private RgbImage _decodedImage;
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;
        private int _failedDecodes = 0;
        private int _skippedFrames = 0;
        private int _imageWidth = 1920;
        private int _imageHeight = 1008;
        private bool _isReinitializing = false;

        // Performance monitoring
        private PerformanceMonitor _performanceMonitor;
        private System.Diagnostics.Stopwatch _frameTimer = new System.Diagnostics.Stopwatch();

        public event Action<BitmapSource> FrameDecoded;
        public event Action<int, int> SizeChanged;
        public event Action<PerformanceMonitor> MetricsUpdated;

        private readonly string _logPath = "receiver_decoder.log";

        // Expose monitoring
        public PerformanceMonitor PerformanceMonitor => _performanceMonitor;

        public VideoDecoder()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile("🚀 VideoDecoder initialized with performance monitoring");

            _performanceMonitor = new PerformanceMonitor();
            _frameTimer.Start();
        }

        public void InitializeDecoder(Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _videoDecoder = new H264Decoder();

                var decParam = new TagSVCDecodingParam();
                decParam.uiTargetDqLayer = 0xff;
                decParam.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
                decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_AVC;

                _videoDecoder.Initialize(decParam);

                _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);

                LogToFile($"✅ H264Sharp decoder initialized with AVC parameters, buffer: {_imageWidth}x{_imageHeight}");

                dispatcher?.BeginInvoke(() =>
                {
                    statusCallback?.Invoke("Decoder initialized and ready");
                });
            }
            catch (Exception initErr)
            {
                LogToFile($"❌ Decoder init error: {initErr.Message}");
                dispatcher?.BeginInvoke(() =>
                {
                    statusCallback?.Invoke($"Decoder init error: {initErr.Message}");
                });
                throw;
            }
        }

        public void DecodeAndDisplayFrame(byte[] encodedData, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            if (_videoDecoder == null || encodedData == null || encodedData.Length == 0)
            {
                LogToFile("❌ DecodeAndDisplayFrame: decoder is null or no data");
                _performanceMonitor.RecordDroppedFrame();
                return;
            }

            if (_isReinitializing)
            {
                LogToFile("⏸️ Skipping frame during reinitialization");
                _performanceMonitor.RecordDroppedFrame();
                return;
            }

            // Copy data to prevent race conditions
            byte[] dataCopy = new byte[encodedData.Length];
            Buffer.BlockCopy(encodedData, 0, dataCopy, 0, encodedData.Length);

            // Use BeginInvoke to avoid blocking UDP thread
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    DecodeInternal(dataCopy, statusCallback);
                }));
            }
            else
            {
                DecodeInternal(dataCopy, statusCallback);
            }
        }

        private void DecodeInternal(byte[] encodedData, Action<string> statusCallback)
        {
            long frameStartTime = _frameTimer.ElapsedMilliseconds;

            try
            {
                LogToFile($"📥 Attempting to decode frame: {encodedData.Length} bytes");

                // Analyze NAL structure
                bool hasSPS = false;
                bool hasPPS = false;
                bool hasIDR = false;

                for (int i = 0; i < encodedData.Length - 4; i++)
                {
                    if (encodedData[i] == 0x00 && encodedData[i + 1] == 0x00 &&
                        encodedData[i + 2] == 0x00 && encodedData[i + 3] == 0x01)
                    {
                        int nalType = encodedData[i + 4] & 0x1F;

                        if (nalType == 7) hasSPS = true;
                        if (nalType == 8) hasPPS = true;
                        if (nalType == 5) hasIDR = true;
                    }
                }

                LogToFile($"Frame analysis: SPS={hasSPS}, PPS={hasPPS}, IDR={hasIDR}");

                // Skip non-keyframes until we get the first keyframe
                if (_receivedFrames == 0 && (!hasSPS || !hasPPS || !hasIDR))
                {
                    _skippedFrames++;
                    LogToFile($"⏭️ Skipping non-keyframe #{_skippedFrames} (waiting for SPS+PPS+IDR)");
                    _performanceMonitor.RecordDroppedFrame();

                    if (_skippedFrames % 10 == 0)
                    {
                        statusCallback?.Invoke($"⏳ Waiting for keyframe... skipped {_skippedFrames} frames");
                    }
                    return;
                }

                LogToFile("🔄 Calling Decode...");
                bool decodeResult = _videoDecoder.Decode(
                    encodedData,
                    0,
                    encodedData.Length,
                    noDelay: true,
                    out DecodingState ds,
                    ref _decodedImage);

                long decodeTime = _frameTimer.ElapsedMilliseconds - frameStartTime;

                LogToFile($"Decode result: {decodeResult}, DecodingState: {ds}, Time: {decodeTime}ms");

                if (decodeResult && _decodedImage != null)
                {
                    _receivedFrames++;
                    _totalBytesReceived += encodedData.Length;
                    _failedDecodes = 0;
                    _skippedFrames = 0;

                    // Record metrics with latency
                    _performanceMonitor.RecordFrame(encodedData.Length, decodeTime);

                    // Check if image size changed
                    if (_decodedImage.Width != _imageWidth || _decodedImage.Height != _imageHeight)
                    {
                        int oldWidth = _imageWidth;
                        int oldHeight = _imageHeight;

                        _imageWidth = _decodedImage.Width;
                        _imageHeight = _decodedImage.Height;

                        LogToFile($"📐 Image size changed from {oldWidth}x{oldHeight} to {_imageWidth}x{_imageHeight}");

                        SizeChanged?.Invoke(_imageWidth, _imageHeight);

                        _decodedImage?.Dispose();
                        _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);

                        LogToFile($"✅ Decoder buffer reallocated for new size: {_imageWidth}x{_imageHeight}");
                    }

                    LogToFile($"✅ Successfully decoded frame #{_receivedFrames}: {_decodedImage.Width}x{_decodedImage.Height}");

                    // Already on UI thread - no dispatcher needed
                    try
                    {
                        var bitmapSource = RgbImageToWriteableBitmap(_decodedImage);
                        LogToFile($"✅ Converted to WriteableBitmap: {bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");

                        FrameDecoded?.Invoke(bitmapSource);
                        LogToFile("✅ FrameDecoded event invoked");

                        // Update status with metrics
                        if (_receivedFrames % 15 == 0)
                        {
                            string status = $"{_performanceMonitor.GetQualityIndicator()} {_performanceMonitor.GetStatusString()}";
                            statusCallback?.Invoke(status);

                            MetricsUpdated?.Invoke(_performanceMonitor);
                        }
                        else if (_receivedFrames == 1)
                        {
                            statusCallback?.Invoke($"✅ First frame decoded! {_decodedImage.Width}x{_decodedImage.Height}");
                        }
                    }
                    catch (Exception convErr)
                    {
                        LogToFile($"❌ Display error: {convErr.Message}\n{convErr.StackTrace}");
                        statusCallback?.Invoke($"Display error: {convErr.Message}");
                    }
                }
                else
                {
                    _failedDecodes++;
                    _performanceMonitor.RecordDroppedFrame();
                    LogToFile($"❌ Decode returned false: state={ds}, dataSize={encodedData.Length}");

                    if (_failedDecodes <= 3)
                    {
                        string firstBytes = BitConverter.ToString(encodedData, 0, Math.Min(50, encodedData.Length));
                        LogToFile($"First 50 bytes: {firstBytes}");

                        statusCallback?.Invoke($"Decode failed #{_failedDecodes}: state={ds}, size={encodedData.Length}");
                    }
                }
            }
            catch (Exception decErr)
            {
                LogToFile($"❌ Decoder exception: {decErr.Message}\n{decErr.StackTrace}");
                _performanceMonitor.RecordDroppedFrame();
                statusCallback?.Invoke($"Decoder error: {decErr.Message}");
            }
        }

        private WriteableBitmap RgbImageToWriteableBitmap(RgbImage img)
        {
            int width = img.Width;
            int height = img.Height;
            int strideSrc = img.Stride;

            LogToFile($"Converting image: {width}x{height}, stride: {strideSrc}, format: {img.Format}");

            var wb = new WriteableBitmap(
                width,
                height,
                96, 96,
                PixelFormats.Bgr24,
                null);

            wb.Lock();

            int strideDst = wb.BackBufferStride;

            unsafe
            {
                byte* dst = (byte*)wb.BackBuffer;

                if (img.IsManaged && img.ManagedBytes != null)
                {
                    fixed (byte* srcPtr = img.ManagedBytes)
                    {
                        byte* src = srcPtr + img.dataOffset;

                        for (int y = 0; y < height; y++)
                        {
                            byte* srcRow = src + y * strideSrc;
                            byte* dstRow = dst + y * strideDst;

                            for (int x = 0; x < width; x++)
                            {
                                int srcIdx = x * 3;
                                int dstIdx = x * 3;

                                dstRow[dstIdx + 0] = srcRow[srcIdx + 2]; // B = R
                                dstRow[dstIdx + 1] = srcRow[srcIdx + 1]; // G = G
                                dstRow[dstIdx + 2] = srcRow[srcIdx + 0]; // R = B
                            }
                        }
                    }
                }
                else
                {
                    byte* src = (byte*)(img.NativeBytes + img.dataOffset);

                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = src + y * strideSrc;
                        byte* dstRow = dst + y * strideDst;

                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = x * 3;
                            int dstIdx = x * 3;

                            dstRow[dstIdx + 0] = srcRow[srcIdx + 2]; // B = R
                            dstRow[dstIdx + 1] = srcRow[srcIdx + 1]; // G = G
                            dstRow[dstIdx + 2] = srcRow[srcIdx + 0]; // R = B
                        }
                    }
                }
            }

            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            wb.Unlock();
            wb.Freeze();

            LogToFile("✅ WriteableBitmap conversion complete (RGB->BGR swapped)");
            return wb;
        }

        public void ReinitializeDecoder(int newWidth, int newHeight, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _isReinitializing = true;
                LogToFile($"🔄 Reinitializing decoder for new size: {newWidth}x{newHeight}");

                _videoDecoder?.Dispose();
                _decodedImage?.Dispose();

                _receivedFrames = 0;
                _skippedFrames = 0;
                _failedDecodes = 0;
                _performanceMonitor.Reset();

                _imageWidth = newWidth;
                _imageHeight = newHeight;

                _videoDecoder = new H264Decoder();

                var decParam = new TagSVCDecodingParam();
                decParam.uiTargetDqLayer = 0xff;
                decParam.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
                decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_AVC;

                _videoDecoder.Initialize(decParam);

                _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);

                LogToFile($"✅ Decoder reinitialized: {_imageWidth}x{_imageHeight}");

                _isReinitializing = false;

                dispatcher?.BeginInvoke(() =>
                {
                    statusCallback?.Invoke($"Decoder reinitialized for {_imageWidth}x{_imageHeight}");
                });
            }
            catch (Exception err)
            {
                _isReinitializing = false;
                LogToFile($"❌ Decoder reinit error: {err.Message}");
                throw;
            }
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
            }
        }

        public void DisposeDecoder()
        {
            LogToFile($"🛑 Decoder disposed. Final stats: {_performanceMonitor.GetStatusString()}");

            _decodedImage?.Dispose();
            _decodedImage = null;

            _videoDecoder?.Dispose();
            _videoDecoder = null;
        }

        public void Dispose()
        {
            DisposeDecoder();
        }
    }

    internal class FrameReceiver : IDisposable
    {
        private UdpClient _udpClient;
        private UdpClient _controlClient;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private int _port;
        private IPEndPoint _senderEndPoint;
        private const int CONTROL_PORT_OFFSET = 2;

        // Frame reassembly state
        private Dictionary<long, FrameAssembler> _incompleteFrames = new Dictionary<long, FrameAssembler>();
        private long _lastDeliveredFrame = -1;
        private long _lastSeenSequence = 0;

        // I-Frame request state
        private bool _waitingForIFrame = false;
        private DateTime _lastIFrameRequestTime = DateTime.MinValue;
        private const int IFRAME_REQUEST_COOLDOWN_MS = 1000;

        // Statistics
        private int _totalFramesReceived = 0;
        private long _totalPacketsReceived = 0;
        private long _duplicatePackets = 0;
        private long _outOfOrderPackets = 0;
        private long _lostPackets = 0;
        private int _iframeRequestsSent = 0;

        public event Action<byte[]> EncodedDataReceived;
        public event Action FrameDroppedForUI;

        private readonly string _logPath = "receiver_udp.log";

        public FrameReceiver()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile("🚀 FrameReceiver initialized with I-Frame request capability");
        }

        public void InitializeReceiver(int port, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _port = port;
                _udpClient = new UdpClient(_port);
                _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024;

                _controlClient = new UdpClient();

                LogToFile($"✅ UDP socket created on port {_port}");
                LogToFile($"✅ Control client created for I-Frame requests");
                LogToFile($"📋 Will send control messages to sender's port + {CONTROL_PORT_OFFSET}");

                dispatcher?.BeginInvoke(() =>
                {
                    statusCallback?.Invoke($"Receiver ready on port {_port}");
                });
            }
            catch (Exception initErr)
            {
                LogToFile($"❌ Receiver init error: {initErr.Message}");
                throw;
            }
        }

        public void StartReceiving()
        {
            if (_udpClient == null)
            {
                throw new InvalidOperationException("UDP client not initialized. Call InitializeReceiver first.");
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token));

            LogToFile("▶️ Started receiving loop with I-Frame request on packet loss");
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            LogToFile($"🔄 Receive loop started on port {_port}");

            // TEMP, DELETE LATER
            Random random = new Random();
            int droppedPackets = 0;
            int totalPacketsForRandom = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    byte[] packet = result.Buffer;

                    if (_senderEndPoint == null)
                    {
                        _senderEndPoint = result.RemoteEndPoint;
                        LogToFile($"📍 Sender identified: {_senderEndPoint.Address}:{_senderEndPoint.Port}");
                    }

                    _totalPacketsReceived++;
                    totalPacketsForRandom++;

                    //TEMP DELETE LATER
                    // 🎲 SIMULATE 0.5% PACKET LOSS (1 in 200 packets)
                    if (random.Next(200) == 0)
                    {
                        droppedPackets++;
                        LogToFile($"🎲 SIMULATED PACKET LOSS! Dropped packet #{totalPacketsForRandom} (Total dropped: {droppedPackets}/{totalPacketsForRandom} = {(droppedPackets * 100.0 / totalPacketsForRandom):F2}%)");
                        continue;
                    }

                    if (!ParsePacketHeader(packet, out long seqNum, out long frameNum, out int totalPackets, out byte[] payload))
                    {
                        LogToFile($"❌ Failed to parse packet header");
                        continue;
                    }

                    // Detect out-of-order packets
                    if (seqNum <= _lastSeenSequence && _lastSeenSequence > 0)
                    {
                        _outOfOrderPackets++;
                        if (_outOfOrderPackets % 10 == 1)
                        {
                            LogToFile($"📊 Out-of-order: Seq {seqNum} (expected > {_lastSeenSequence})");
                        }
                    }

                    // Simple packet loss detection - immediate I-Frame request
                    if (seqNum > _lastSeenSequence + 1 && _lastSeenSequence > 0)
                    {
                        long gap = seqNum - _lastSeenSequence - 1;
                        _lostPackets += gap;

                        LogToFile($"⚠️ PACKET LOSS! Gap: {gap} packets (Seq {_lastSeenSequence + 1} to {seqNum - 1})");

                        await RequestIFrameAsync();
                    }

                    _lastSeenSequence = Math.Max(_lastSeenSequence, seqNum);

                    // Get or create frame assembler
                    if (!_incompleteFrames.ContainsKey(frameNum))
                    {
                        _incompleteFrames[frameNum] = new FrameAssembler(frameNum, totalPackets);
                        LogToFile($"📦 New frame #{frameNum} started (expects {totalPackets} packets)");
                    }

                    FrameAssembler assembler = _incompleteFrames[frameNum];

                    bool isNewPacket = assembler.AddPacket(seqNum, payload);

                    if (!isNewPacket)
                    {
                        _duplicatePackets++;
                        if (_duplicatePackets % 10 == 1)
                        {
                            LogToFile($"🔄 Duplicate packet: Frame {frameNum}, Seq {seqNum}");
                        }
                    }

                    if (assembler.IsComplete)
                    {
                        byte[] completeFrame = assembler.GetCompleteFrame();

                        LogToFile($"✅ Frame #{frameNum} COMPLETE: {assembler.ReceivedPackets}/{assembler.TotalPackets} packets, {completeFrame.Length} bytes");

                        if (_waitingForIFrame)
                        {
                            bool isCompleteIFrame = IsIFrame(completeFrame);

                            if (isCompleteIFrame)
                            {
                                LogToFile($"🎯 COMPLETE I-FRAME RECEIVED! Frame #{frameNum} - resuming normal operation");
                                _waitingForIFrame = false;

                                _incompleteFrames.Clear();

                                DeliverFrame(frameNum, completeFrame);
                            }
                            else
                            {
                                LogToFile($"⏸️ Frame #{frameNum} complete but NOT an I-Frame (missing SPS/PPS/IDR), discarding");
                                _incompleteFrames.Remove(frameNum);
                            }
                        }
                        else
                        {
                            DeliverFrame(frameNum, completeFrame);
                            CleanupOldFrames(frameNum);
                        }
                    }
                    else
                    {
                        if (_totalPacketsReceived % 50 == 0)
                        {
                            LogToFile($"📥 Frame #{frameNum}: {assembler.ReceivedPackets}/{assembler.TotalPackets} packets");
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    LogToFile("⚠️ UDP client disposed, exiting receive loop");
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Receive error: {ex.Message}");
                }
            }

            LogToFile($"🛑 Receive loop ended");
            LogStatistics();
        }

        private bool ParsePacketHeader(byte[] packet, out long seqNum, out long frameNum, out int totalPackets, out byte[] payload)
        {
            seqNum = 0;
            frameNum = 0;
            totalPackets = 0;
            payload = null;

            try
            {
                int firstHash = Array.IndexOf(packet, (byte)'#');
                if (firstHash < 0 || firstHash > 20)
                {
                    return false;
                }

                int secondHash = Array.IndexOf(packet, (byte)'#', firstHash + 1);
                if (secondHash < 0 || secondHash > firstHash + 20)
                {
                    return false;
                }

                string seqStr = Encoding.ASCII.GetString(packet, 0, firstHash);
                string frameStr = Encoding.ASCII.GetString(packet, firstHash + 1, secondHash - firstHash - 1);
                string totalStr = Encoding.ASCII.GetString(packet, secondHash + 1, 3);

                if (!long.TryParse(seqStr, out seqNum))
                {
                    return false;
                }

                if (!long.TryParse(frameStr, out frameNum))
                {
                    return false;
                }

                if (!int.TryParse(totalStr, out totalPackets))
                {
                    return false;
                }

                int payloadStart = secondHash + 1 + 3;
                int payloadSize = packet.Length - payloadStart;

                if (payloadSize < 0)
                {
                    return false;
                }

                payload = new byte[payloadSize];
                Buffer.BlockCopy(packet, payloadStart, payload, 0, payloadSize);

                return true;
            }
            catch (Exception ex)
            {
                LogToFile($"❌ Parse error: {ex.Message}");
                return false;
            }
        }

        private bool IsIFrame(byte[] data)
        {
            bool hasSPS = false;
            bool hasPPS = false;
            bool hasIDR = false;

            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 &&
                    data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    if (i + 4 < data.Length)
                    {
                        int nalType = data[i + 4] & 0x1F;

                        if (nalType == 7) hasSPS = true;
                        if (nalType == 8) hasPPS = true;
                        if (nalType == 5) hasIDR = true;
                    }
                }
            }

            return hasSPS && hasPPS && hasIDR;
        }

        private async Task RequestIFrameAsync()
        {
            if (_senderEndPoint == null)
            {
                LogToFile("⚠️ Cannot request I-Frame: sender endpoint unknown");
                return;
            }

            if ((DateTime.Now - _lastIFrameRequestTime).TotalMilliseconds < IFRAME_REQUEST_COOLDOWN_MS)
            {
                LogToFile("⏸️ I-Frame request on cooldown, skipping");
                return;
            }

            _lastIFrameRequestTime = DateTime.Now;
            _waitingForIFrame = true;

            int controlPort = _senderEndPoint.Port + CONTROL_PORT_OFFSET;
            var controlEndpoint = new IPEndPoint(_senderEndPoint.Address, controlPort);

            LogToFile($"📡 Sending I-Frame request to {controlEndpoint.Address}:{controlEndpoint.Port}");

            byte[] requestMessage = Encoding.ASCII.GetBytes("IFRAME_REQUEST");

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await _controlClient.SendAsync(requestMessage, requestMessage.Length, controlEndpoint);
                    _iframeRequestsSent++;
                    LogToFile($"📤 I-Frame request sent (attempt {i + 1}/3)");

                    if (i < 2)
                    {
                        await Task.Delay(50);
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Failed to send I-Frame request: {ex.Message}");
                }
            }

            LogToFile($"✅ I-Frame request sequence complete ({_iframeRequestsSent} total requests sent)");
        }

        private void DeliverFrame(long frameNum, byte[] frameData)
        {
            _totalFramesReceived++;
            _lastDeliveredFrame = frameNum;

            EncodedDataReceived?.Invoke(frameData);

            LogToFile($"📤 Frame #{frameNum} delivered to decoder ({_totalFramesReceived} total)");
            LogToFile("────────────────────────────────────────");
        }

        private void CleanupOldFrames(long currentFrame)
        {
            var toRemove = _incompleteFrames.Keys.Where(f => f < currentFrame - 5).ToList();

            foreach (var frameNum in toRemove)
            {
                var assembler = _incompleteFrames[frameNum];
                if (!assembler.IsComplete)
                {
                    LogToFile($"🗑️ Discarding incomplete frame #{frameNum} ({assembler.ReceivedPackets}/{assembler.TotalPackets} packets)");
                    FrameDroppedForUI?.Invoke();
                }
                _incompleteFrames.Remove(frameNum);
            }
        }

        private void LogStatistics()
        {
            double lossPercent = _totalPacketsReceived > 0 ? (_lostPackets * 100.0 / _totalPacketsReceived) : 0;

            LogToFile("═══════════════════════════════════════");
            LogToFile("📊 FINAL STATISTICS:");
            LogToFile($"  Total Packets Received: {_totalPacketsReceived}");
            LogToFile($"  Lost Packets: {_lostPackets} ({lossPercent:F2}%)");
            LogToFile($"  Out-of-Order Packets: {_outOfOrderPackets}");
            LogToFile($"  Duplicate Packets: {_duplicatePackets}");
            LogToFile($"  Total Frames Delivered: {_totalFramesReceived}");
            LogToFile($"  I-Frame Requests Sent: {_iframeRequestsSent}");
            LogToFile($"  Last Frame Number: {_lastDeliveredFrame}");
            LogToFile($"  Last Sequence Number: {_lastSeenSequence}");
            LogToFile("═══════════════════════════════════════");
        }

        public void StopReceiving()
        {
            LogToFile("🛑 Stopping receiver...");

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));

            LogStatistics();
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            StopReceiving();

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _controlClient?.Close();
            _controlClient?.Dispose();
            _controlClient = null;

            LogToFile("🗑️ FrameReceiver disposed");
        }
    }

    /// <summary>
    /// Assembles packets belonging to a single frame
    /// </summary>
    internal class FrameAssembler
    {
        public long FrameNumber { get; }
        public int TotalPackets { get; }
        public int ReceivedPackets => _packets.Count;
        public bool IsComplete => ReceivedPackets == TotalPackets;

        private Dictionary<long, byte[]> _packets = new Dictionary<long, byte[]>();

        public FrameAssembler(long frameNumber, int totalPackets)
        {
            FrameNumber = frameNumber;
            TotalPackets = totalPackets;
        }

        /// <summary>
        /// Add a packet to this frame. Returns true if packet was new, false if duplicate.
        /// </summary>
        public bool AddPacket(long seqNum, byte[] payload)
        {
            if (_packets.ContainsKey(seqNum))
            {
                return false; // Duplicate
            }

            _packets[seqNum] = payload;
            return true;
        }

        /// <summary>
        /// Get the complete frame data by concatenating all packets in sequence order
        /// </summary>
        public byte[] GetCompleteFrame()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Frame is not complete yet");
            }

            // Sort packets by sequence number
            var orderedPackets = _packets.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();

            // Calculate total size
            int totalSize = orderedPackets.Sum(p => p.Length);

            // Concatenate all packets
            byte[] result = new byte[totalSize];
            int offset = 0;

            foreach (var packet in orderedPackets)
            {
                Buffer.BlockCopy(packet, 0, result, offset, packet.Length);
                offset += packet.Length;
            }

            return result;
        }
    }
}
