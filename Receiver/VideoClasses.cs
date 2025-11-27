using H264Sharp;
using System;
using System.Collections.Generic;
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

namespace Receiver
{
    internal class VideoDecoder : IDisposable
    {
        private H264Decoder _videoDecoder;
        private RgbImage _decodedImage;  // 🔧 FIX: Pre-allocate the image buffer
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;
        private int _failedDecodes = 0;
        private int _skippedFrames = 0;
        private int _imageWidth = 1920;   // Will be updated on first decode
        private int _imageHeight = 1008;

        public event Action<BitmapSource> FrameDecoded;

        private readonly string _logPath = "receiver_decoder.log";

        public VideoDecoder()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile("🚀 VideoDecoder initialized");
        }

        public void InitializeDecoder(Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _videoDecoder = new H264Decoder();

                // 🔧 FIX: H264Sharp decoder MUST be initialized with parameters
                var decParam = new TagSVCDecodingParam();
                decParam.uiTargetDqLayer = 0xff;
                decParam.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
                decParam.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_AVC;

                _videoDecoder.Initialize(decParam);

                // 🔧 FIX: Pre-allocate the image buffer
                // We'll reallocate if size is different
                _decodedImage = new RgbImage(ImageFormat.Bgr, _imageWidth, _imageHeight);

                LogToFile($"✅ H264Sharp decoder initialized with AVC parameters, buffer: {_imageWidth}x{_imageHeight}");

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke("Decoder initialized and ready");
                });
            }
            catch (Exception initErr)
            {
                LogToFile($"❌ Decoder init error: {initErr.Message}");
                dispatcher?.Invoke(() =>
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
                return;
            }

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

                    if (_skippedFrames % 10 == 0)
                    {
                        dispatcher?.Invoke(() =>
                        {
                            statusCallback?.Invoke($"⏳ Waiting for keyframe... skipped {_skippedFrames} frames");
                        });
                    }
                    return;
                }

                // 🔧 FIX: Decode into pre-allocated buffer
                LogToFile("🔄 Calling Decode...");
                bool decodeResult = _videoDecoder.Decode(
                    encodedData,
                    0,
                    encodedData.Length,
                    noDelay: true,
                    out DecodingState ds,
                    ref _decodedImage);

                LogToFile($"Decode result: {decodeResult}, DecodingState: {ds}");

                if (decodeResult && _decodedImage != null)
                {
                    _receivedFrames++;
                    _totalBytesReceived += encodedData.Length;
                    _failedDecodes = 0;
                    _skippedFrames = 0;

                    // Check if image size changed (shouldn't happen but be safe)
                    if (_decodedImage.Width != _imageWidth || _decodedImage.Height != _imageHeight)
                    {
                        _imageWidth = _decodedImage.Width;
                        _imageHeight = _decodedImage.Height;
                        LogToFile($"📐 Image size changed to {_imageWidth}x{_imageHeight}");
                    }

                    LogToFile($"✅ Successfully decoded frame #{_receivedFrames}: {_decodedImage.Width}x{_decodedImage.Height}");

                    dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var bitmapSource = RgbImageToWriteableBitmap(_decodedImage);
                            LogToFile($"✅ Converted to WriteableBitmap: {bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");

                            FrameDecoded?.Invoke(bitmapSource);
                            LogToFile("✅ FrameDecoded event invoked");

                            // Update status periodically
                            if (_receivedFrames % 15 == 0)
                            {
                                statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                    $"{_decodedImage.Width}x{_decodedImage.Height} | " +
                                    $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
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
                    });
                }
                else
                {
                    _failedDecodes++;
                    LogToFile($"❌ Decode returned false: state={ds}, dataSize={encodedData.Length}");

                    // Log details for first few failures
                    if (_failedDecodes <= 3)
                    {
                        string firstBytes = BitConverter.ToString(encodedData, 0, Math.Min(50, encodedData.Length));
                        LogToFile($"First 50 bytes: {firstBytes}");

                        dispatcher?.Invoke(() =>
                        {
                            statusCallback?.Invoke($"Decode failed #{_failedDecodes}: state={ds}, size={encodedData.Length}");
                        });
                    }
                }
            }
            catch (Exception decErr)
            {
                LogToFile($"❌ Decoder exception: {decErr.Message}\n{decErr.StackTrace}");
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Decoder error: {decErr.Message}");
                });
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
            int bytesPerRow = width * 3;

            unsafe
            {
                byte* dst = (byte*)wb.BackBuffer;

                if (img.IsManaged && img.ManagedBytes != null)
                {
                    fixed (byte* srcPtr = img.ManagedBytes)
                    {
                        byte* src = srcPtr + img.dataOffset;

                        if (strideSrc == strideDst)
                        {
                            Buffer.MemoryCopy(src, dst, strideDst * height, strideDst * height);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + y * strideSrc,
                                    dst + y * strideDst,
                                    strideDst,
                                    bytesPerRow);
                            }
                        }
                    }
                }
                else
                {
                    byte* src = (byte*)(img.NativeBytes + img.dataOffset);

                    if (strideSrc == strideDst)
                    {
                        Buffer.MemoryCopy(src, dst, strideDst * height, strideDst * height);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                src + y * strideSrc,
                                dst + y * strideDst,
                                strideDst,
                                bytesPerRow);
                        }
                    }
                }
            }

            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            wb.Unlock();
            wb.Freeze();

            LogToFile("✅ WriteableBitmap conversion complete");
            return wb;
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void DisposeDecoder()
        {
            LogToFile("🛑 Decoder disposed");

            // Dispose the image buffer
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
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private int _port;

        private byte[] _frameBuffer = new byte[10 * 1024 * 1024];
        private int _frameBufferPosition = 0;
        private int _receivedChunks = 0;
        private int _totalFramesReceived = 0;

        public event Action<byte[]> EncodedDataReceived;

        private readonly string _logPath = "receiver_udp.log";

        private const int MAX_PACKET_SIZE = 1400;
        private const int HEADER_SIZE = 5;

        public FrameReceiver()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile("🚀 FrameReceiver initialized");
        }

        public void InitializeReceiver(int port, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _port = port;
                _udpClient = new UdpClient(_port);
                _udpClient.Client.ReceiveBufferSize = 1024 * 1024;

                LogToFile($"✅ UDP socket created on port {_port}");

                dispatcher?.Invoke(() =>
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

            LogToFile("▶️ Started receiving loop");
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

                    if (packet.Length < HEADER_SIZE)
                    {
                        LogToFile($"⚠️ Packet too small: {packet.Length} bytes");
                        continue;
                    }

                    int payloadSize = BitConverter.ToInt32(packet, 0);
                    bool isLastChunk = packet[4] == 1;

                    if (payloadSize < 0 || payloadSize > MAX_PACKET_SIZE - HEADER_SIZE)
                    {
                        LogToFile($"❌ Invalid payload size: {payloadSize}");
                        continue;
                    }

                    if (_frameBufferPosition + payloadSize > _frameBuffer.Length)
                    {
                        LogToFile($"❌ Frame buffer overflow! Resetting.");
                        _frameBufferPosition = 0;
                        _receivedChunks = 0;
                        continue;
                    }

                    Buffer.BlockCopy(packet, HEADER_SIZE, _frameBuffer, _frameBufferPosition, payloadSize);
                    _frameBufferPosition += payloadSize;
                    _receivedChunks++;

                    if (_receivedChunks == 1 || isLastChunk)
                    {
                        LogToFile($"  📦 Chunk {_receivedChunks}: received {payloadSize} bytes, isLast={isLastChunk}");
                    }

                    if (isLastChunk)
                    {
                        _totalFramesReceived++;

                        LogToFile($"✅ Complete frame received: {_receivedChunks} chunks, {_frameBufferPosition} total bytes");

                        byte[] frameData = new byte[_frameBufferPosition];
                        Buffer.BlockCopy(_frameBuffer, 0, frameData, 0, _frameBufferPosition);

                        EncodedDataReceived?.Invoke(frameData);

                        LogToFile($"📤 Frame #{_totalFramesReceived} sent to decoder");
                        LogToFile("────────────────────────────────────────");

                        _frameBufferPosition = 0;
                        _receivedChunks = 0;
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

            LogToFile("🛑 Receive loop ended");
        }

        public void StopReceiving()
        {
            LogToFile("🛑 Stopping receiver...");

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));

            LogToFile($"✅ Receiver stopped. Total frames received: {_totalFramesReceived}");
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

            LogToFile("🗑️ FrameReceiver disposed");
        }
    }
}