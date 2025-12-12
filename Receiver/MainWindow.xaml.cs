using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Receiver
{
    public partial class MainWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;
        private WriteableBitmap _displayBitmap;

        public MainWindow()
        {
            InitializeComponent();

            Closed += MainWindow_Closed;
        }
        private void InitializeDisplay(int width, int height)
        {
            Dispatcher.Invoke(() =>
            {
                _displayBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
                DisplayImage.Source = _displayBitmap;
            });
        }
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use default port 12345 (matching sender)
                int port = 12345;

                // 🔧 FIX: Recreate objects if they were disposed
                if (_frameReceiver == null)
                {
                    _frameReceiver = new FrameReceiver();
                }

                if (_videoDecoder == null)
                {
                    _videoDecoder = new VideoDecoder();
                }

                // Initialize receiver on specified port
                _frameReceiver.InitializeReceiver(port, Dispatcher, UpdateStatus);

                // Initialize decoder
                _videoDecoder.InitializeDecoder(Dispatcher, UpdateStatus);

                // Wire up the pipeline: Receiver -> Decoder -> Display
                _frameReceiver.EncodedDataReceived += OnEncodedDataReceived;
                _videoDecoder.FrameDecoded += OnFrameDecoded;

                // Handle size changes during streaming
                // Handle size changes during streaming
                // Handle size changes during streaming
                _videoDecoder.SizeChanged += (width, height) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Screen size changed to {width}x{height}, reinitializing...";

                        // Clear old display
                        DisplayImage.Source = null;
                        _displayBitmap = null;
                    });

                    _videoDecoder.ReinitializeDecoder(width, height, Dispatcher, UpdateStatus);
                };

                // Start receiving
                _frameReceiver.StartReceiving();

                // Update UI
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = $"Listening on port {port}... Make sure sender is sending to localhost:{port}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting receiver:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error starting receiver";
            }
        }

        private void OnEncodedDataReceived(byte[] encodedData)
        {
            _videoDecoder?.DecodeAndDisplayFrame(encodedData, Dispatcher, UpdateStatus);
        }

        private void OnFrameDecoded(BitmapSource bitmapSource)
        {
            Dispatcher.Invoke(() =>
            {
                // If bitmap doesn't exist or size changed, recreate it
                if (_displayBitmap == null ||
                    _displayBitmap.PixelWidth != bitmapSource.PixelWidth ||
                    _displayBitmap.PixelHeight != bitmapSource.PixelHeight)
                {
                    InitializeDisplay(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                }

                // Copy the decoded frame to our persistent bitmap
                try
                {
                    _displayBitmap.Lock();

                    int stride = bitmapSource.PixelWidth * 3; // BGR24 = 3 bytes per pixel
                    byte[] pixels = new byte[stride * bitmapSource.PixelHeight];
                    bitmapSource.CopyPixels(pixels, stride, 0);

                    Marshal.Copy(pixels, 0, _displayBitmap.BackBuffer, pixels.Length);

                    _displayBitmap.AddDirtyRect(new Int32Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
                }
                finally
                {
                    _displayBitmap.Unlock();
                }
            });
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // 🔧 FIX: Properly dispose and recreate for next start

            // Unsubscribe from events first
            if (_frameReceiver != null)
            {
                _frameReceiver.EncodedDataReceived -= OnEncodedDataReceived;
                _frameReceiver.StopReceiving();
                _frameReceiver.Dispose();
                _frameReceiver = null;
            }

            if (_videoDecoder != null)
            {
                _videoDecoder.FrameDecoded -= OnFrameDecoded;
                _videoDecoder.SizeChanged -= null;
                _videoDecoder.Dispose();
                _videoDecoder = null;
            }

            // Clear display
            Dispatcher.Invoke(() =>
            {
                DisplayImage.Source = null;
                _displayBitmap = null;
            });

            // Update UI
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopped - ready to start again";
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // Clean up on window close
            if (_frameReceiver != null)
            {
                _frameReceiver.EncodedDataReceived -= OnEncodedDataReceived;
                _frameReceiver.Dispose();
            }

            if (_videoDecoder != null)
            {
                _videoDecoder.FrameDecoded -= OnFrameDecoded;
                _videoDecoder.SizeChanged -= null;
                _videoDecoder.Dispose();
            }
        }
    }
}