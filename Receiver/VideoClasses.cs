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
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;
        private int _failedDecodes = 0;
        private int _skippedFrames = 0;

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
                LogToFile("✅ Decoder created successfully");

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

                // 🔧 FIX: בדוק אם יש SPS/PPS בפריים
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

                // 🔧 FIX: דלג על פריימים שאינם keyframes עד שמגיע הראשון
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

                RgbImage decodedImage = null;

                // נסה לפענח את הפריים
                LogToFile("🔄 Calling Decode...");
                bool decodeResult = _videoDecoder.Decode(encodedData, 0, encodedData.Length, noDelay: true, out DecodingState ds, ref decodedImage);

                LogToFile($"Decode result: {decodeResult}, DecodingState: {ds}, Image: {(decodedImage != null ? $"{decodedImage.Width}x{decodedImage.Height}" : "null")}");

                if (decodeResult && decodedImage != null)
                {
                    _receivedFrames++;
                    _totalBytesReceived += encodedData.Length;
                    _failedDecodes = 0;
                    _skippedFrames = 0; // אפס כי התחלנו לקבל פריימים

                    LogToFile($"✅ Successfully decoded frame #{_receivedFrames}: {decodedImage.Width}x{decodedImage.Height}, format: {decodedImage.Format}");

                    dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var bitmapSource = RgbImageToWriteableBitmap(decodedImage);
                            LogToFile($"✅ Converted to WriteableBitmap: {bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");

                            FrameDecoded?.Invoke(bitmapSource);
                            LogToFile("✅ FrameDecoded event invoked");

                            // עדכן סטטוס כל 15 פריימים
                            if (_receivedFrames % 15 == 0)
                            {
                                statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                    $"{decodedImage.Width}x{decodedImage.Height} | " +
                                    $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
                            }
                            else if (_receivedFrames == 1)
                            {
                                statusCallback?.Invoke($"✅ First frame decoded! {decodedImage.Width}x{decodedImage.Height}");
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
                    LogToFile($"❌ Decode FAILED: state={ds}, hasImage={decodedImage != null}, dataSize={encodedData.Length}");

                    // לוג את הבתים הראשונים לבדיקה
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

            LogToFile($"Converting image: {width}x{height}, stride: {strideSrc}, format: {img.Format}, isManaged: {img.IsManaged}");

            var wb = new WriteableBitmap(
                width,
                height,
                96, 96,
                PixelFormats.Bgr24,
                null);

            wb.Lock();

            int strideDst = wb.BackBufferStride;
            int bytesPerRow = width * 3;

            LogToFile($"Destination stride: {strideDst}, bytesPerRow: {bytesPerRow}");

            unsafe
            {
                byte* dst = (byte*)wb.BackBuffer;

                if (img.IsManaged && img.ManagedBytes != null)
                {
                    LogToFile("Using managed bytes path");
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
                    LogToFile("Using native bytes path");
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
                // התעלם משגיאות בכתיבת לוג
            }
        }

        public void DisposeDecoder()
        {
            LogToFile("🛑 Decoder disposed");
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

        private byte[] _frameBuffer = new byte[10 * 1024 * 1024]; // 10MB buffer
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
                _udpClient.Client.ReceiveBufferSize = 1024 * 1024; // 1MB buffer

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

                    // Parse header: [payloadSize(4)][isLastChunk(1)][payload]
                    int payloadSize = BitConverter.ToInt32(packet, 0);
                    bool isLastChunk = packet[4] == 1;

                    if (payloadSize < 0 || payloadSize > MAX_PACKET_SIZE - HEADER_SIZE)
                    {
                        LogToFile($"❌ Invalid payload size: {payloadSize}");
                        continue;
                    }

                    // Copy payload to frame buffer
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

                    // Log first and last chunks only
                    if (_receivedChunks == 1 || isLastChunk)
                    {
                        LogToFile($"  📦 Chunk {_receivedChunks}: received {payloadSize} bytes, isLast={isLastChunk}");
                    }

                    // If this is the last chunk, process the complete frame
                    if (isLastChunk)
                    {
                        _totalFramesReceived++;

                        LogToFile($"✅ Complete frame received: {_receivedChunks} chunks, {_frameBufferPosition} total bytes");

                        // Extract frame data
                        byte[] frameData = new byte[_frameBufferPosition];
                        Buffer.BlockCopy(_frameBuffer, 0, frameData, 0, _frameBufferPosition);

                        // Notify decoder
                        EncodedDataReceived?.Invoke(frameData);

                        LogToFile($"📤 Frame #{_totalFramesReceived} sent to decoder");
                        LogToFile("────────────────────────────────────────");

                        // Reset for next frame
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
                // Ignore logging errors
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