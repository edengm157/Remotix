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

        // ✅ CRITICAL FIX: Lock for thread safety
        private readonly object _decodeLock = new object();

        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;
        private int _failedDecodes = 0;
        private int _skippedFrames = 0;
        private int _imageWidth = 1920;
        private int _imageHeight = 1008;
        private bool _isReinitializing = false;

        private PerformanceMonitor _performanceMonitor;
        private System.Diagnostics.Stopwatch _frameTimer = new System.Diagnostics.Stopwatch();

        public event Action<BitmapSource> FrameDecoded;
        public event Action<int, int> SizeChanged;
        public event Action<PerformanceMonitor> MetricsUpdated;

        private readonly string _logPath = "receiver_decoder.log";

        public PerformanceMonitor PerformanceMonitor => _performanceMonitor;

        public VideoDecoder()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile("🚀 VideoDecoder initialized with thread-safe locking");

            _performanceMonitor = new PerformanceMonitor();
            _frameTimer.Start();
        }

        public void InitializeDecoder(Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            lock (_decodeLock)
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

            LogToFile($"📞 DecodeAndDisplayFrame called with {encodedData.Length} bytes (thread: {Thread.CurrentThread.ManagedThreadId})");

            // ✅ CRITICAL CHANGE: Use Invoke (synchronous) instead of BeginInvoke
            // This blocks the jitter buffer thread until decode completes
            if (dispatcher != null)
            {
                dispatcher.Invoke(new Action(() =>
                {
                    LogToFile($"🎯 Dispatcher.Invoke executing on thread: {Thread.CurrentThread.ManagedThreadId}");
                    DecodeInternal(dataCopy, statusCallback);
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            else
            {
                DecodeInternal(dataCopy, statusCallback);
            }

            LogToFile($"📞 DecodeAndDisplayFrame RETURNED (decode complete)");
        }

        // Replace the DecodeInternal method with this version that has additional safety checks:

        private void DecodeInternal(byte[] encodedData, Action<string> statusCallback)
        {
            // ✅ CRITICAL: Lock to ensure only ONE decode operation at a time
            lock (_decodeLock)
            {
                LogToFile($"🔒 DecodeInternal LOCKED (thread: {Thread.CurrentThread.ManagedThreadId})");
                long frameStartTime = _frameTimer.ElapsedMilliseconds;

                try
                {
                    // ✅ CRITICAL: Validate decoder state INSIDE the lock
                    if (_videoDecoder == null)
                    {
                        LogToFile("❌ Decoder is null, cannot decode");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    // ✅ CRITICAL: Validate data before processing
                    if (encodedData == null || encodedData.Length == 0)
                    {
                        LogToFile("❌ Encoded data is null or empty");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    // ✅ CRITICAL: Check for minimum valid H264 data size
                    if (encodedData.Length < 5)
                    {
                        LogToFile($"❌ Encoded data too small: {encodedData.Length} bytes");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    LogToFile($"📥 Attempting to decode frame: {encodedData.Length} bytes");

                    // Analyze NAL structure
                    bool hasSPS = false;
                    bool hasPPS = false;
                    bool hasIDR = false;
                    int nalCount = 0;

                    for (int i = 0; i < encodedData.Length - 4; i++)
                    {
                        if (encodedData[i] == 0x00 && encodedData[i + 1] == 0x00 &&
                            encodedData[i + 2] == 0x00 && encodedData[i + 3] == 0x01)
                        {
                            nalCount++;
                            if (i + 4 < encodedData.Length)
                            {
                                int nalType = encodedData[i + 4] & 0x1F;

                                if (nalType == 7) hasSPS = true;
                                if (nalType == 8) hasPPS = true;
                                if (nalType == 5) hasIDR = true;
                            }
                        }
                    }

                    LogToFile($"Frame analysis: NALs={nalCount}, SPS={hasSPS}, PPS={hasPPS}, IDR={hasIDR}");

                    // ✅ CRITICAL: Validate frame has at least one NAL unit
                    if (nalCount == 0)
                    {
                        LogToFile("❌ No NAL units found in frame data - corrupted frame");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

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

                    // ✅ CRITICAL: Always dispose old buffer BEFORE creating new one
                    if (_decodedImage != null)
                    {
                        try
                        {
                            LogToFile("🗑️ Disposing old decoded image buffer");
                            _decodedImage.Dispose();
                            _decodedImage = null;
                        }
                        catch (Exception disposeEx)
                        {
                            LogToFile($"⚠️ Error disposing old image: {disposeEx.Message}");
                            _decodedImage = null; // Force null even if dispose fails
                        }
                    }

                    // ✅ CRITICAL: Small delay to ensure dispose completes
                    Thread.Sleep(1);

                    // ✅ CRITICAL: Create new buffer
                    try
                    {
                        _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);
                        LogToFile($"✅ Created new decode buffer: {_imageWidth}x{_imageHeight}");
                    }
                    catch (Exception bufferEx)
                    {
                        LogToFile($"❌ Failed to create decode buffer: {bufferEx.Message}");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    // ✅ CRITICAL: Validate buffer was created successfully
                    if (_decodedImage == null)
                    {
                        LogToFile("❌ Failed to create decode buffer - buffer is null");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    LogToFile("🔄 Calling Decode...");
                    LogToFile($"   Decoder valid: {_videoDecoder != null}");
                    LogToFile($"   Buffer valid: {_decodedImage != null}");
                    LogToFile($"   Buffer size: {_decodedImage.Width}x{_decodedImage.Height}");
                    LogToFile($"   Data size: {encodedData.Length} bytes");

                    bool decodeResult = false;
                    DecodingState ds = DecodingState.dsErrorFree;

                    try
                    {
                        // ✅ THE ACTUAL DECODE CALL
                        decodeResult = _videoDecoder.Decode(
                            encodedData,
                            0,
                            encodedData.Length,
                            noDelay: true,
                            out ds,
                            ref _decodedImage);

                        LogToFile($"✅ Decode() completed without exception");
                    }
                    catch (AccessViolationException avEx)
                    {
                        LogToFile("═══════════════════════════════════════════════════════════");
                        LogToFile("❌❌❌ ACCESS VIOLATION in Decode() ❌❌❌");
                        LogToFile("═══════════════════════════════════════════════════════════");
                        LogToFile($"Thread: {Thread.CurrentThread.ManagedThreadId}");
                        LogToFile($"Frame details:");
                        LogToFile($"  - Data length: {encodedData.Length} bytes");
                        LogToFile($"  - NAL units: {nalCount}");
                        LogToFile($"  - Has SPS: {hasSPS}, PPS: {hasPPS}, IDR: {hasIDR}");
                        LogToFile($"  - Received frames so far: {_receivedFrames}");
                        LogToFile($"Buffer details:");
                        LogToFile($"  - Target size: {_imageWidth}x{_imageHeight}");
                        LogToFile($"  - Buffer exists: {_decodedImage != null}");
                        if (_decodedImage != null)
                        {
                            LogToFile($"  - Buffer size: {_decodedImage.Width}x{_decodedImage.Height}");
                            LogToFile($"  - Buffer stride: {_decodedImage.Stride}");
                            LogToFile($"  - Buffer format: {_decodedImage.Format}");
                            LogToFile($"  - Is managed: {_decodedImage.IsManaged}");
                        }
                        LogToFile($"Exception:");
                        LogToFile($"  - Message: {avEx.Message}");
                        LogToFile($"  - Source: {avEx.Source}");

                        // Dump encoded data for analysis
                        string hexDump = BitConverter.ToString(encodedData, 0, Math.Min(200, encodedData.Length));
                        LogToFile($"Encoded data (first 200 bytes):");
                        LogToFile($"  {hexDump}");
                        LogToFile("═══════════════════════════════════════════════════════════");

                        _performanceMonitor.RecordDroppedFrame();
                        statusCallback?.Invoke("❌ Decoder crashed - attempting recovery");

                        // ✅ AGGRESSIVE RECOVERY: Completely reinitialize decoder
                        LogToFile("🔄 Attempting FULL decoder recovery...");
                        try
                        {
                            // Dispose everything
                            if (_decodedImage != null)
                            {
                                _decodedImage.Dispose();
                                _decodedImage = null;
                            }

                            if (_videoDecoder != null)
                            {
                                _videoDecoder.Dispose();
                                _videoDecoder = null;
                            }

                            // Small delay
                            Thread.Sleep(10);

                            // Recreate decoder
                            _videoDecoder = new H264Decoder();
                            var decParam = new TagSVCDecodingParam();
                            decParam.uiTargetDqLayer = 0xff;
                            decParam.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
                            decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_AVC;
                            _videoDecoder.Initialize(decParam);

                            // Recreate buffer
                            _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);

                            // Reset frame counter to force waiting for keyframe
                            _receivedFrames = 0;
                            _skippedFrames = 0;

                            LogToFile("✅ Full decoder recovery successful");
                            statusCallback?.Invoke("⚠️ Decoder recovered - waiting for keyframe");
                        }
                        catch (Exception recoveryEx)
                        {
                            LogToFile($"❌ Recovery FAILED: {recoveryEx.Message}");
                            LogToFile($"   Stack: {recoveryEx.StackTrace}");
                            statusCallback?.Invoke("❌ Decoder recovery failed - restart may be needed");
                        }

                        return;
                    }
                    catch (Exception otherEx)
                    {
                        LogToFile($"❌ Unexpected exception in Decode(): {otherEx.GetType().Name}");
                        LogToFile($"   Message: {otherEx.Message}");
                        LogToFile($"   Stack: {otherEx.StackTrace}");
                        _performanceMonitor.RecordDroppedFrame();
                        return;
                    }

                    long decodeTime = _frameTimer.ElapsedMilliseconds - frameStartTime;

                    LogToFile($"Decode result: {decodeResult}, DecodingState: {ds}, Time: {decodeTime}ms");

                    if (decodeResult && _decodedImage != null)
                    {
                        _receivedFrames++;
                        _totalBytesReceived += encodedData.Length;
                        _failedDecodes = 0;
                        _skippedFrames = 0;

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
                        }

                        LogToFile($"✅ Successfully decoded frame #{_receivedFrames}: {_decodedImage.Width}x{_decodedImage.Height}");

                        try
                        {
                            var bitmapSource = RgbImageToWriteableBitmap(_decodedImage);
                            LogToFile($"✅ Converted to WriteableBitmap: {bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");

                            FrameDecoded?.Invoke(bitmapSource);
                            LogToFile("✅ FrameDecoded event invoked - IMMEDIATE display update");

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
                    LogToFile($"❌ Outer exception in DecodeInternal: {decErr.Message}\n{decErr.StackTrace}");
                    _performanceMonitor.RecordDroppedFrame();
                    statusCallback?.Invoke($"Decoder error: {decErr.Message}");
                }
                finally
                {
                    LogToFile($"🔓 DecodeInternal UNLOCKED");
                }
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
        private Task _jitterBufferTask;
        private int _port;
        private IPEndPoint _senderEndPoint;
        private const int CONTROL_PORT_OFFSET = 2;

        // ✅ JITTER BUFFER SETTINGS - Like TWS earbuds!
        private const int JITTER_BUFFER_MS = 25;  // Wait 25ms for late packets
        private const int LOSS_TIMEOUT_MS = 60;   // After 60ms, packet is truly lost

        // ✅ FRAME BUFFER - Added IMMEDIATELY when frame starts
        private Dictionary<long, BufferedFrame> _frameBuffer = new Dictionary<long, BufferedFrame>();
        private long _lastDeliveredFrame = -1;
        private long _lastSeenSequence = 0;

        // I-Frame request state
        private bool _waitingForIFrame = false;
        private DateTime _lastIFrameRequestTime = DateTime.MinValue;
        private const int IFRAME_REQUEST_COOLDOWN_MS = 1000;

        // Packet gap tracking (for detecting potential loss)
        private List<PacketGap> _detectedGaps = new List<PacketGap>();

        // Statistics
        private int _totalFramesDelivered = 0;
        private long _totalPacketsReceived = 0;
        private long _duplicatePackets = 0;
        private long _outOfOrderPackets = 0;
        private long _lostPackets = 0;
        private long _recoveredPackets = 0;
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
            LogToFile($"🚀 FrameReceiver initialized with {JITTER_BUFFER_MS}ms jitter buffer (TWS-style)");
        }

        public void InitializeReceiver(int port, System.Windows.Threading.Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _port = port;
                _udpClient = new UdpClient(_port);
                _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024;

                _controlClient = new UdpClient();

                LogToFile($"✅ UDP socket created on port {_port}");
                LogToFile($"✅ Control client created for I-Frame requests");
                LogToFile($"📋 Jitter buffer: {JITTER_BUFFER_MS}ms | Loss timeout: {LOSS_TIMEOUT_MS}ms");

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
            _jitterBufferTask = Task.Run(() => JitterBufferLoop(_cancellationTokenSource.Token));

            LogToFile("▶️ Started receiving loop with jitter buffer (assembly → buffer → decoder)");
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            LogToFile($"🔄 Receive loop started on port {_port}");

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


                    if (!ParsePacketHeader(packet, out long seqNum, out long frameNum, out int totalPackets, out byte[] payload))
                    {
                        LogToFile($"❌ Failed to parse packet header");
                        continue;
                    }

                    // Track out-of-order packets
                    if (seqNum <= _lastSeenSequence && _lastSeenSequence > 0)
                    {
                        _outOfOrderPackets++;
                        if (_outOfOrderPackets % 10 == 1)
                        {
                            LogToFile($"📊 Out-of-order: Seq {seqNum} (expected > {_lastSeenSequence})");
                        }
                    }

                    // Detect gaps (potential packet loss or out-of-order)
                    if (seqNum > _lastSeenSequence + 1 && _lastSeenSequence > 0)
                    {
                        long gapStart = _lastSeenSequence + 1;
                        long gapEnd = seqNum - 1;
                        long gapSize = gapEnd - gapStart + 1;

                        LogToFile($"⚠️ Gap detected: Seq {gapStart} to {gapEnd} ({gapSize} packets)");

                        _detectedGaps.Add(new PacketGap
                        {
                            StartSeq = gapStart,
                            EndSeq = gapEnd,
                            DetectedAt = DateTime.Now,
                            IFrameRequested = false
                        });
                    }

                    // Check if this packet fills a gap (late arrival = recovery!)
                    var filledGaps = _detectedGaps.Where(g => seqNum >= g.StartSeq && seqNum <= g.EndSeq).ToList();
                    if (filledGaps.Any())
                    {
                        _recoveredPackets++;
                        LogToFile($"✅ Recovered packet {seqNum} (arrived late, gap filled!)");
                    }

                    _lastSeenSequence = Math.Max(_lastSeenSequence, seqNum);

                    // ✅ ADD TO BUFFER IMMEDIATELY when frame starts (like TWS buffer)
                    if (!_frameBuffer.ContainsKey(frameNum))
                    {
                        _frameBuffer[frameNum] = new BufferedFrame
                        {
                            FrameNumber = frameNum,
                            Assembler = new FrameAssembler(frameNum, totalPackets),
                            FirstPacketTime = DateTime.Now
                        };
                        LogToFile($"📦 Frame #{frameNum} added to buffer (expects {totalPackets} packets)");
                    }

                    BufferedFrame bufferedFrame = _frameBuffer[frameNum];

                    bool isNewPacket = bufferedFrame.Assembler.AddPacket(seqNum, payload);

                    if (!isNewPacket)
                    {
                        _duplicatePackets++;
                        if (_duplicatePackets % 10 == 1)
                        {
                            LogToFile($"🔄 Duplicate packet: Frame {frameNum}, Seq {seqNum}");
                        }
                    }

                    // DON'T deliver here - let JitterBufferLoop handle timing!
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
        }

        /// <summary>
        /// Jitter Buffer Loop - Like TWS sync timing
        /// Checks buffer every 5ms and decides when to deliver frames
        /// </summary>
        private async Task JitterBufferLoop(CancellationToken ct)
        {
            LogToFile("🔄 Jitter buffer loop started (TWS-style timing)");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5, ct);

                    DateTime now = DateTime.Now;
                    var framesToDeliver = new List<long>();

                    // ✅ SAFETY: Create snapshot of frame numbers to avoid collection modification
                    var frameNumbers = _frameBuffer.Keys.ToList();

                    foreach (var frameNum in frameNumbers)
                    {
                        // ✅ SAFETY: Check if frame still exists
                        if (!_frameBuffer.ContainsKey(frameNum))
                        {
                            continue;
                        }

                        BufferedFrame bufferedFrame = _frameBuffer[frameNum];
                        FrameAssembler assembler = bufferedFrame.Assembler;

                        double frameAge = (now - bufferedFrame.FirstPacketTime).TotalMilliseconds;

                        if (assembler.IsComplete)
                        {
                            if (frameAge >= JITTER_BUFFER_MS)
                            {
                                LogToFile($"⏱️ Frame #{frameNum} complete + {frameAge:F0}ms aged → Ready to deliver");
                                framesToDeliver.Add(frameNum);
                            }
                            else
                            {
                                double remaining = JITTER_BUFFER_MS - frameAge;
                                if (_frameBuffer.Count <= 2)
                                {
                                    LogToFile($"⏳ Frame #{frameNum} complete but waiting {remaining:F0}ms for late packets");
                                }
                            }
                        }
                        else if (frameAge >= LOSS_TIMEOUT_MS)
                        {
                            LogToFile($"⏰ Frame #{frameNum} TIMEOUT after {frameAge:F0}ms: {assembler.ReceivedPackets}/{assembler.TotalPackets} packets");

                            if (!_waitingForIFrame)
                            {
                                LogToFile($"📡 Requesting I-frame (confirmed packet loss)");
                                await RequestIFrameAsync();
                            }

                            framesToDeliver.Add(frameNum);
                        }
                    }

                    // Deliver frames in order
                    foreach (var frameNum in framesToDeliver.OrderBy(f => f))
                    {
                        // ✅ SAFETY: Double-check frame still exists
                        if (!_frameBuffer.ContainsKey(frameNum))
                        {
                            LogToFile($"⚠️ Frame #{frameNum} disappeared from buffer before delivery");
                            continue;
                        }

                        BufferedFrame bufferedFrame = _frameBuffer[frameNum];
                        FrameAssembler assembler = bufferedFrame.Assembler;

                        if (assembler.IsComplete)
                        {
                            try
                            {
                                // ✅ GetCompleteFrame is now thread-safe
                                byte[] completeFrame = assembler.GetCompleteFrame();
                                LogToFile($"✅ Frame #{frameNum} COMPLETE: {assembler.ReceivedPackets}/{assembler.TotalPackets} packets, {completeFrame.Length} bytes");

                                if (_waitingForIFrame)
                                {
                                    bool isIFrame = IsIFrame(completeFrame);
                                    if (isIFrame)
                                    {
                                        LogToFile($"🎯 I-FRAME RECEIVED! Frame #{frameNum} - resuming");
                                        _waitingForIFrame = false;
                                        _frameBuffer.Clear();
                                        DeliverToDecoder(frameNum, completeFrame);
                                    }
                                    else
                                    {
                                        LogToFile($"⏸️ Frame #{frameNum} not I-Frame, discarding");
                                    }
                                }
                                else
                                {
                                    DeliverToDecoder(frameNum, completeFrame);
                                }
                            }
                            catch (InvalidOperationException ex)
                            {
                                // ✅ SAFETY: Frame became incomplete between check and GetCompleteFrame
                                LogToFile($"❌ Frame #{frameNum} became incomplete: {ex.Message}");
                                // Don't record dropped frame here - already handled elsewhere
                            }
                        }
                        else
                        {
                            int missing = assembler.TotalPackets - assembler.ReceivedPackets;
                            LogToFile($"🗑️ Discarding incomplete frame #{frameNum} ({assembler.ReceivedPackets}/{assembler.TotalPackets}, {missing} packets lost)");
                            _lostPackets += missing;
                            FrameDroppedForUI?.Invoke();
                        }

                        _frameBuffer.Remove(frameNum);
                    }

                    // Check for gaps that should trigger I-frame request
                    var expiredGaps = _detectedGaps.Where(g =>
                        !g.IFrameRequested &&
                        (now - g.DetectedAt).TotalMilliseconds >= LOSS_TIMEOUT_MS
                    ).ToList();

                    if (expiredGaps.Any() && !_waitingForIFrame)
                    {
                        long totalConfirmedLost = expiredGaps.Sum(g => g.EndSeq - g.StartSeq + 1);
                        LogToFile($"⚠️ CONFIRMED LOSS: {totalConfirmedLost} packets after {LOSS_TIMEOUT_MS}ms");

                        await RequestIFrameAsync();

                        foreach (var gap in expiredGaps)
                        {
                            gap.IFrameRequested = true;
                        }
                    }

                    _detectedGaps.RemoveAll(g => (now - g.DetectedAt).TotalMilliseconds > 5000);
                    CleanupOldFrames();
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Jitter buffer error: {ex.Message}");
                }
            }

            LogToFile("🛑 Jitter buffer loop ended");
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

        private void DeliverToDecoder(long frameNum, byte[] frameData)
        {
            _totalFramesDelivered++;
            _lastDeliveredFrame = frameNum;

            EncodedDataReceived?.Invoke(frameData);

            LogToFile($"📤 Frame #{frameNum} delivered to decoder ({_totalFramesDelivered} total) → IMMEDIATE decode");
            LogToFile("────────────────────────────────────────");
        }

        private void CleanupOldFrames()
        {
            if (_lastDeliveredFrame < 0)
                return;

            var toRemove = _frameBuffer.Keys.Where(f => f < _lastDeliveredFrame - 10).ToList();

            foreach (var frameNum in toRemove)
            {
                var bufferedFrame = _frameBuffer[frameNum];
                LogToFile($"🗑️ Cleaning up old frame #{frameNum} ({bufferedFrame.Assembler.ReceivedPackets}/{bufferedFrame.Assembler.TotalPackets} packets)");
                _frameBuffer.Remove(frameNum);
            }
        }

        private void LogStatistics()
        {
            double lossPercent = _totalPacketsReceived > 0 ? (_lostPackets * 100.0 / _totalPacketsReceived) : 0;
            double recoveryPercent = (_lostPackets + _recoveredPackets) > 0 ? (_recoveredPackets * 100.0 / (_lostPackets + _recoveredPackets)) : 0;

            LogToFile("═══════════════════════════════════════");
            LogToFile("📊 FINAL STATISTICS:");
            LogToFile($"  Total Packets Received: {_totalPacketsReceived}");
            LogToFile($"  Lost Packets: {_lostPackets} ({lossPercent:F2}%)");
            LogToFile($"  Recovered Packets: {_recoveredPackets} ({recoveryPercent:F1}% recovery rate) ✅");
            LogToFile($"  Out-of-Order Packets: {_outOfOrderPackets}");
            LogToFile($"  Duplicate Packets: {_duplicatePackets}");
            LogToFile($"  Total Frames Delivered: {_totalFramesDelivered}");
            LogToFile($"  I-Frame Requests Sent: {_iframeRequestsSent}");
            LogToFile($"  Last Frame Number: {_lastDeliveredFrame}");
            LogToFile($"  Last Sequence Number: {_lastSeenSequence}");
            LogToFile($"  Jitter Buffer: {JITTER_BUFFER_MS}ms | Loss Timeout: {LOSS_TIMEOUT_MS}ms");
            LogToFile("═══════════════════════════════════════");
        }

        public void StopReceiving()
        {
            LogToFile("🛑 Stopping receiver...");

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            _jitterBufferTask?.Wait(TimeSpan.FromSeconds(2));

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
    /// Buffered frame - holds assembler + timing info (like TWS audio buffer)
    /// </summary>
    internal class BufferedFrame
    {
        public long FrameNumber { get; set; }
        public FrameAssembler Assembler { get; set; }
        public DateTime FirstPacketTime { get; set; }
    }

    /// <summary>
    /// Tracks packet gaps for recovery detection
    /// </summary>
    internal class PacketGap
    {
        public long StartSeq { get; set; }
        public long EndSeq { get; set; }
        public DateTime DetectedAt { get; set; }
        public bool IFrameRequested { get; set; }
    }

    /// <summary>
    /// Thread-safe assembler for packets belonging to a single frame
    /// </summary>
    internal class FrameAssembler
    {
        public long FrameNumber { get; }
        public int TotalPackets { get; }

        // ✅ CRITICAL: Add lock for thread safety
        private readonly object _lock = new object();

        private Dictionary<long, byte[]> _packets = new Dictionary<long, byte[]>();

        public FrameAssembler(long frameNumber, int totalPackets)
        {
            FrameNumber = frameNumber;
            TotalPackets = totalPackets;
        }

        /// <summary>
        /// Thread-safe property to check completion
        /// </summary>
        public bool IsComplete
        {
            get
            {
                lock (_lock)
                {
                    return _packets.Count == TotalPackets;
                }
            }
        }

        /// <summary>
        /// Thread-safe property to get received packet count
        /// </summary>
        public int ReceivedPackets
        {
            get
            {
                lock (_lock)
                {
                    return _packets.Count;
                }
            }
        }

        /// <summary>
        /// Thread-safe: Add a packet to this frame. Returns true if packet was new, false if duplicate.
        /// </summary>
        public bool AddPacket(long seqNum, byte[] payload)
        {
            lock (_lock)
            {
                if (_packets.ContainsKey(seqNum))
                {
                    return false; // Duplicate
                }

                _packets[seqNum] = payload;
                return true;
            }
        }

        /// <summary>
        /// Thread-safe: Get the complete frame data by concatenating all packets in sequence order
        /// </summary>
        public byte[] GetCompleteFrame()
        {
            lock (_lock)
            {
                // ✅ Check completion inside the lock
                if (_packets.Count != TotalPackets)
                {
                    throw new InvalidOperationException(
                        $"Frame {FrameNumber} is not complete: {_packets.Count}/{TotalPackets} packets");
                }

                // ✅ Create a snapshot of packets while locked
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
}