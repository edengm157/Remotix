//using SharpDX.Direct3D11;
//using Windows.Graphics.DirectX.Direct3D11;
//using Device = SharpDX.Direct3D11.Device;
using H264Sharp;
using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Receiver
{
    internal class VideoDecoder : IDisposable
    {
        private H264Decoder _videoDecoder;     
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;

        // Event that fires when a frame is decoded and ready to display
        public event Action<WriteableBitmap> FrameDecoded;

        // Initialize decoder
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

        // DecodeAndDisplayFrame - receives encoded bytes and decodes them
        public void DecodeAndDisplayFrame(byte[] encodedData, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            if (_videoDecoder == null || encodedData == null || encodedData.Length == 0)
                return;

            try
            {
                // Decode the H264 data
                if (_videoDecoder.Decode(encodedData, encodedData.Length, out var decodedFrames))
                {
                    if (frame != null)
                    {
                        foreach (var frame in decodedFrames)
                        {
                            _receivedFrames++;
                            _totalBytesReceived += encodedData.Length;

                            // Convert decoded frame to WriteableBitmap for display
                            var bitmap = CreateBitmapFromFrame(frame);

                            // Raise event with decoded bitmap
                            dispatcher?.Invoke(() =>
                            {
                                FrameDecoded?.Invoke(bitmap);

                                if (_receivedFrames % 15 == 0)
                                {
                                    statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                        $"{frame.Width}x{frame.Height} | " +
                                        $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
                                }
                            });

                            frame.Dispose();
                        }
                        }
                }
                //if (_videoDecoder.Decode(encodedData, out var decodedFrames))
                //{
                    
                    
                //}
            }
            catch (Exception decErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Decoder error: {decErr.Message}");
                });
            }
        }

        // Convert RgbImage to WriteableBitmap
        private WriteableBitmap CreateBitmapFromFrame(RgbImage frame)
        {
            var bitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);

            bitmap.Lock();
            try
            {
                unsafe
                {
                    byte* dstPtr = (byte*)bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride;

                    // Copy frame data to bitmap
                    Marshal.Copy(frame.ImageBytes, 0, (IntPtr)dstPtr, frame.ImageBytes.Length);
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, frame.Width, frame.Height));
            }
            finally
            {
                bitmap.Unlock();
            }

            return bitmap;
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
        // Event raised when encoded data is received
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
            // Default local port - should match sender's target port
            _localEndPoint = new IPEndPoint(IPAddress.Any, 12345);
        }

        // Initialize UDP listener with optional custom port
        public void InitializeReceiver(int port = 12345, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _dispatcher = dispatcher;
                _statusCallback = statusCallback;

                _localEndPoint = new IPEndPoint(IPAddress.Any, port);
                _udpClient = new UdpClient(_localEndPoint);

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

        // Start receiving frames
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

        // Stop receiving frames
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

        // Main receive loop
        private async Task ReceiveLoop(CancellationToken token)
        {
            while (_isReceiving && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();

                    if (result.Buffer != null && result.Buffer.Length > 0)
                    {
                        // Raise event with received encoded data
                        EncodedDataReceived?.Invoke(result.Buffer);
                    }
                }
                catch (SocketException sockErr)
                {
                    if (_isReceiving)
                    {
                        _dispatcher?.Invoke(() =>
                        {
                            _statusCallback?.Invoke($"Socket error: {sockErr.Message}");
                        });
                    }
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // UDP client was disposed - exit gracefully
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
