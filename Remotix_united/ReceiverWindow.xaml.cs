using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Receiver;

namespace RemotixApp
{
    public partial class ReceiverWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;
        private WriteableBitmap _displayBitmap;
        private InputLogger _inputLogger;
        private System.Windows.Threading.DispatcherTimer _metricsUpdateTimer;

        public ReceiverWindow()
        {
            InitializeComponent();

            _metricsUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _metricsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _metricsUpdateTimer.Tick += MetricsUpdateTimer_Tick;

            Closed += ReceiverWindow_Closed;
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

                if (_inputLogger == null)
                {
                    _inputLogger = new InputLogger();
                }

                _videoDecoder.InitializeDecoder(Dispatcher, UpdateStatus);

                _frameReceiver.FrameDroppedForUI += () =>
                {
                    _videoDecoder.PerformanceMonitor.RecordDroppedFrame();
                };

                _frameReceiver.EncodedDataReceived += OnEncodedDataReceived;
                _videoDecoder.FrameDecoded += OnFrameDecoded;
                _videoDecoder.MetricsUpdated += OnMetricsUpdated;

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
                _inputLogger.StartLogging();

                _metricsUpdateTimer.Start();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = $"📡 Listening on port {port} | Sending input to remote (port 12346)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting receiver:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "❌ Error starting receiver";
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

                    _displayBitmap.AddDirtyRect(new Int32Rect(0, 0,
                        bitmapSource.PixelWidth, bitmapSource.PixelHeight));
                }
                finally
                {
                    _displayBitmap.Unlock();
                }
            });
        }

        private void OnMetricsUpdated(PerformanceMonitor monitor)
        {
            UpdateMetricsDisplay(monitor);
        }

        private void MetricsUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_videoDecoder?.PerformanceMonitor != null)
            {
                UpdateMetricsDisplay(_videoDecoder.PerformanceMonitor);
            }
        }

        private void UpdateMetricsDisplay(PerformanceMonitor monitor)
        {
            Dispatcher.Invoke(() =>
            {
                FpsText.Text = monitor.CurrentFPS.ToString("F1");
                BitrateText.Text = monitor.CurrentBitrateMbps.ToString("F2");
                LatencyText.Text = monitor.AverageLatencyMs.ToString("F0");
                PacketLossText.Text = monitor.PacketLossPercent.ToString("F1");
                TotalFramesText.Text = monitor.TotalFrames.ToString();
                QualityIndicator.Text = monitor.GetQualityIndicator();

                if (monitor.CurrentFPS >= 25)
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                else if (monitor.CurrentFPS >= 15)
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15));
                else
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                if (monitor.PacketLossPercent < 1)
                    PacketLossText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                else if (monitor.PacketLossPercent < 5)
                    PacketLossText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15));
                else
                    PacketLossText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
            });
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _metricsUpdateTimer.Stop();

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
                _videoDecoder.MetricsUpdated -= OnMetricsUpdated;
                _videoDecoder.Dispose();
                _videoDecoder = null;
            }

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "🛑 Stopped";
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void ReceiverWindow_Closed(object sender, EventArgs e)
        {
            _metricsUpdateTimer?.Stop();

            if (_inputLogger != null)
            {
                _inputLogger.Dispose();
            }

            if (_frameReceiver != null)
            {
                _frameReceiver.EncodedDataReceived -= OnEncodedDataReceived;
                _frameReceiver.FrameDroppedForUI -= null;
                _frameReceiver.Dispose();
            }

            if (_videoDecoder != null)
            {
                _videoDecoder.FrameDecoded -= OnFrameDecoded;
                _videoDecoder.MetricsUpdated -= OnMetricsUpdated;
                _videoDecoder.SizeChanged -= null;
                _videoDecoder.Dispose();
            }
        }
    }
}
