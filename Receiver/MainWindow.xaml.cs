using System;
using System.Windows;

namespace Receiver
{
    public partial class MainWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;

        public MainWindow()
        {
            InitializeComponent();

            _frameReceiver = new FrameReceiver();
            _videoDecoder = new VideoDecoder();

            Closed += MainWindow_Closed;
        }

        private void PortTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Optional: validate port input
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize receiver on port 12345 (matching sender)
                _frameReceiver.InitializeReceiver(12345, Dispatcher, UpdateStatus);

                // Initialize decoder
                _videoDecoder.InitializeDecoder(Dispatcher, UpdateStatus);

                // Wire up the pipeline: Receiver -> Decoder -> Display
                _frameReceiver.EncodedDataReceived += (encodedData) =>
                {
                    _videoDecoder.DecodeAndDisplayFrame(encodedData, Dispatcher, UpdateStatus);
                };

                _videoDecoder.FrameDecoded += (bitmap) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DisplayImage.Source = bitmap;
                    });
                };

                // Start receiving
                _frameReceiver.StartReceiving();

                // Update UI
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = "Waiting for frames...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting receiver:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error starting receiver";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _frameReceiver?.StopReceiving();

            // Clear display
            Dispatcher.Invoke(() =>
            {
                DisplayImage.Source = null;
            });

            // Update UI
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopped";
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _frameReceiver?.Dispose();
            _videoDecoder?.Dispose();
        }
    }
}