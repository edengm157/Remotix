using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Receiver
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // example for usge
            /*
    public partial class ReceiverWindow : Window
    {
        private FrameReceiver _frameReceiver;
        private VideoDecoder _videoDecoder;

        public ReceiverWindow()
        {
            InitializeComponent();
            
            _frameReceiver = new FrameReceiver();
            _videoDecoder = new VideoDecoder();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Initialize receiver on port 12345
            _frameReceiver.InitializeReceiver(12345, Dispatcher, UpdateStatus);
            
            // Initialize decoder
            _videoDecoder.InitializeDecoder(Dispatcher, UpdateStatus);
            
            // Wire up events
            _frameReceiver.EncodedDataReceived += (encodedData) =>
            {
                _videoDecoder.DecodeAndDisplayFrame(encodedData, Dispatcher, UpdateStatus);
            };
            
            _videoDecoder.FrameDecoded += (bitmap) =>
            {
                // Display bitmap in Image control
                DisplayImage.Source = bitmap;
            };
            
            // Start receiving
            _frameReceiver.StartReceiving();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _frameReceiver?.StopReceiving();
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _frameReceiver?.Dispose();
            _videoDecoder?.Dispose();
            base.OnClosing(e);
        }
    }
    */
        }
    }
}