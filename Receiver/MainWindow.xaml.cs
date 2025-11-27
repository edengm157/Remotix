using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Receiver
{
    public partial class MainWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;

        public MainWindow()
        {
            InitializeComponent();

            Closed += MainWindow_Closed;
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
                DisplayImage.Source = bitmapSource;
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
                _videoDecoder.Dispose();
                _videoDecoder = null;
            }

            // Clear display
            Dispatcher.Invoke(() =>
            {
                DisplayImage.Source = null;
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
                _videoDecoder.Dispose();
            }
        }
    }
}