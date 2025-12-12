using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Receiver
{
    public partial class MainWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;
        private WriteableBitmap _displayBitmap;
        private InputLogger _inputLogger; // מקליט פעולות ושולח אותן לסנדר

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
                int port = 12345;

                if (_frameReceiver == null)
                {
                    _frameReceiver = new FrameReceiver();
                }

                if (_videoDecoder == null)
                {
                    _videoDecoder = new VideoDecoder();
                }

                // יצירת InputLogger
                if (_inputLogger == null)
                {
                    _inputLogger = new InputLogger();
                }

                _frameReceiver.InitializeReceiver(port, Dispatcher, UpdateStatus);
                _videoDecoder.InitializeDecoder(Dispatcher, UpdateStatus);

                _frameReceiver.EncodedDataReceived += OnEncodedDataReceived;
                _videoDecoder.FrameDecoded += OnFrameDecoded;

                _videoDecoder.SizeChanged += (width, height) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Screen size changed to {width}x{height}, reinitializing...";
                        DisplayImage.Source = null;
                        _displayBitmap = null;
                    });

                    _videoDecoder.ReinitializeDecoder(width, height, Dispatcher, UpdateStatus);
                };

                _frameReceiver.StartReceiving();

                // התחלת לוגינג ושליחת פעולות לסנדר
                _inputLogger.StartLogging();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = $"Listening on port {port} + sending input to remote (port 12346)";
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
                if (_displayBitmap == null ||
                    _displayBitmap.PixelWidth != bitmapSource.PixelWidth ||
                    _displayBitmap.PixelHeight != bitmapSource.PixelHeight)
                {
                    InitializeDisplay(bitmapSource.PixelWidth, bitmapSource.PixelHeight);
                }

                try
                {
                    _displayBitmap.Lock();

                    int stride = bitmapSource.PixelWidth * 3;
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
            // עצירת InputLogger
            if (_inputLogger != null)
            {
                _inputLogger.StopLogging();
                _inputLogger.Dispose();
                _inputLogger = null;
            }

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

            Dispatcher.Invoke(() =>
            {
                DisplayImage.Source = null;
                _displayBitmap = null;
            });

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "🛑 Stopped - ready to start again";
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // ניקוי InputLogger
            if (_inputLogger != null)
            {
                _inputLogger.Dispose();
            }

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