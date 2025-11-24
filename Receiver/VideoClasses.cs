using H264Sharp;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;

namespace Receiver
{
    internal class VideoDecoder : IDisposable
    {
        private H264Decoder _videoDecoder;
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;

        public event Action<BitmapSource> FrameDecoded;

        public void InitializeDecoder(Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _videoDecoder = new H264Decoder();
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke("Decoder initialized and ready");
                });
            }
            catch (Exception initErr)
            {
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
                return;

            try
            {
                RgbImage decodedImage = null;

                // Decode the H264 bytes into an RgbImage
                bool decodeSuccess = _videoDecoder.Decode(encodedData, 0, encodedData.Length, noDelay: true, out DecodingState ds, ref decodedImage);

                // DEBUG: Log decode attempt
                if (_receivedFrames == 0)
                {
                    dispatcher?.Invoke(() =>
                    {
                        statusCallback?.Invoke($"First decode attempt: success={decodeSuccess}, state={ds}, image={decodedImage != null}");
                    });
                }

                if (decodeSuccess && decodedImage != null)
                {
                    _receivedFrames++;
                    _totalBytesReceived += encodedData.Length;

                    dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            var bitmapSource = RgbImageToWriteableBitmap(decodedImage);
                            FrameDecoded?.Invoke(bitmapSource);

                            // Update status every 15 frames
                            if (_receivedFrames % 15 == 0)
                            {
                                statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                    $"{decodedImage.Width}x{decodedImage.Height} | " +
                                    $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
                            }
                            else if (_receivedFrames == 1)
                            {
                                statusCallback?.Invoke($"First frame decoded! {decodedImage.Width}x{decodedImage.Height}");
                            }
                        }
                        catch (Exception convErr)
                        {
                            statusCallback?.Invoke($"Display error: {convErr.Message}");
                        }
                    });
                }
                else
                {
                    // DEBUG: Log why decode failed
                    if (_receivedFrames < 5)
                    {
                        dispatcher?.Invoke(() =>
                        {
                            statusCallback?.Invoke($"Decode failed: success={decodeSuccess}, state={ds}, hasImage={decodedImage != null}, dataSize={encodedData.Length}");
                        });
                    }
                }
            }
            catch (Exception decErr)
            {
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

            return wb;
        }

        public void DisposeDecoder()
        {
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
        public event Action<byte[]> EncodedDataReceived;

        private UdpClient _udpClient;
        private IPEndPoint _localEndPoint;
        private bool _isReceiving = false;
        private Task _receiveTask;
        private CancellationTokenSource _cancellationTokenSource;

        private Dispatcher _dispatcher;
        private Action<string> _statusCallback;

        // --- NEW: chunking constants and buffer
        private const int HEADER_SIZE = 5;
        private List<byte> _frameBuffer = new List<byte>();
        private int _chunksReceived = 0;

        public bool IsReceiving => _isReceiving;

        public FrameReceiver()
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, 12345);
        }

        public void InitializeReceiver(int port = 12345, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _dispatcher = dispatcher;
                _statusCallback = statusCallback;

                // CRITICAL FIX: Close existing UDP client if it exists
                if (_udpClient != null)
                {
                    try
                    {
                        _udpClient.Close();
                        _udpClient.Dispose();
                    }
                    catch { }
                    _udpClient = null;
                }

                _localEndPoint = new IPEndPoint(IPAddress.Any, port);
                _udpClient = new UdpClient();

                // Allow reuse of the address/port - this prevents "address already in use" errors
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(_localEndPoint);

                // Increase buffer size to reduce packet loss
                _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024; // 2MB buffer

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Receiver initialized on port {port} (listening on all interfaces)");
                });
            }
            catch (Exception initErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Receiver init error: {initErr.Message}");
                });
                throw;
            }
        }

        public void StartReceiving()
        {
            if (_isReceiving || _udpClient == null)
                return;

            _isReceiving = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(async () => await ReceiveLoop(_cancellationTokenSource.Token));

            _dispatcher?.Invoke(() =>
            {
                _statusCallback?.Invoke("Started receiving frames... Waiting for data from sender");
            });
        }

        public void StopReceiving()
        {
            if (!_isReceiving)
                return;

            _isReceiving = false;

            // Cancel the receive task
            _cancellationTokenSource?.Cancel();

            // CRITICAL FIX: Close the UDP client to unblock ReceiveAsync
            try
            {
                _udpClient?.Close();
            }
            catch { }

            // Wait for the task to complete
            try
            {
                _receiveTask?.Wait(2000);
            }
            catch { }

            _dispatcher?.Invoke(() =>
            {
                _statusCallback?.Invoke("Stopped receiving frames");
            });
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            int packetsReceived = 0;

            while (_isReceiving && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    packetsReceived++;

                    if (result.Buffer != null && result.Buffer.Length > 0)
                    {
                        // Log first packet for debugging
                        if (packetsReceived == 1)
                        {
                            _dispatcher?.Invoke(() =>
                            {
                                _statusCallback?.Invoke($"First packet received! Size: {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
                            });
                        }

                        // Process packet and check if frame is complete
                        byte[] completeFrame = ProcessPacket(result.Buffer);

                        if (completeFrame != null)
                        {
                            // Frame is complete, pass to decoder
                            EncodedDataReceived?.Invoke(completeFrame);
                        }
                    }
                }
                catch (SocketException) when (!_isReceiving)
                {
                    break; // Expected when stopping
                }
                catch (ObjectDisposedException)
                {
                    break; // Expected when stopping
                }
                catch (Exception recErr)
                {
                    _dispatcher?.Invoke(() =>
                    {
                        _statusCallback?.Invoke($"Receive error: {recErr.Message}");
                    });
                }
            }
        }

        private byte[] ProcessPacket(byte[] packet)
        {
            if (packet == null || packet.Length < HEADER_SIZE)
                return null;

            // Parse header
            int payloadSize = BitConverter.ToInt32(packet, 0);
            bool isLastChunk = packet[4] == 1;

            // Validate payload size
            if (payloadSize < 0 || payloadSize > packet.Length - HEADER_SIZE)
            {
                ResetBuffer();
                return null;
            }

            // Extract payload
            byte[] payload = new byte[payloadSize];
            Buffer.BlockCopy(packet, HEADER_SIZE, payload, 0, payloadSize);

            // Add to buffer
            _frameBuffer.AddRange(payload);
            _chunksReceived++;

            // If this is the last chunk, return the complete frame
            if (isLastChunk)
            {
                byte[] completeFrame = _frameBuffer.ToArray();
                int totalChunks = _chunksReceived;

                // Reset for next frame
                ResetBuffer();

                return completeFrame;
            }

            // Frame not yet complete
            return null;
        }

        private void ResetBuffer()
        {
            _frameBuffer.Clear();
            _chunksReceived = 0;
        }

        public void DisposeReceiver()
        {
            StopReceiving();

            try
            {
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
            catch { }

            _udpClient = null;
            _cancellationTokenSource?.Dispose();
        }

        public void Dispose()
        {
            DisposeReceiver();
        }
    }
}