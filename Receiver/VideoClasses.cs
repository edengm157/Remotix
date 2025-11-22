using H264Sharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media.Imaging;

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
                // Signature: Decode(byte[] encoded, int offset, int count, bool noDelay, out DecodingState state, ref RgbImage img)
                if (_videoDecoder.Decode(encodedData, 0, encodedData.Length, noDelay: true, out DecodingState ds, ref decodedImage))
                {
                    if (decodedImage != null)
                    {
                        _receivedFrames++;
                        _totalBytesReceived += encodedData.Length;

                        dispatcher?.Invoke(() =>
                        {
                            try
                            {
                                // Use H264Sharp's built-in ToBitmap() extension method
                                // Then convert System.Drawing.Bitmap to WPF BitmapSource
                                using (var bitmap = decodedImage.ToBitmap())
                                {
                                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                        bitmap.GetHbitmap(),
                                        IntPtr.Zero,
                                        Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());

                                    // Freeze to make it cross-thread accessible
                                    bitmapSource.Freeze();

                                    FrameDecoded?.Invoke(bitmapSource);
                                }

                                // Update status every 15 frames
                                if (_receivedFrames % 15 == 0)
                                {
                                    statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                        $"{decodedImage.Width}x{decodedImage.Height} | " +
                                        $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
                                }
                            }
                            catch (Exception convErr)
                            {
                                statusCallback?.Invoke($"Display error: {convErr.Message}");
                            }
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

                _localEndPoint = new IPEndPoint(IPAddress.Any, port);
                _udpClient = new UdpClient(_localEndPoint);
                _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024; // 2MB buffer

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Receiver initialized on port {port}");
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
                _statusCallback?.Invoke("Started receiving frames...");
            });
        }

        public void StopReceiving()
        {
            if (!_isReceiving)
                return;

            _isReceiving = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _receiveTask?.Wait(1000);
            }
            catch (Exception) { }

            _dispatcher?.Invoke(() =>
            {
                _statusCallback?.Invoke("Stopped receiving frames");
            });
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (_isReceiving && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();

                    if (result.Buffer != null && result.Buffer.Length > 0)
                    {
                        // Pass H264 encoded bytes to decoder
                        EncodedDataReceived?.Invoke(result.Buffer);
                    }
                }
                catch (SocketException) when (!_isReceiving)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
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

        public void DisposeReceiver()
        {
            StopReceiving();
            _udpClient?.Close();
            _udpClient?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        public void Dispose()
        {
            DisposeReceiver();
        }
    }
}