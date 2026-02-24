using System.Windows;

namespace RemotixApp
{
    public partial class MenuWindow : Window
    {
        public MenuWindow()
        {
            InitializeComponent();
        }

        private void SenderButton_Click(object sender, RoutedEventArgs e)
        {
            var senderWindow = new SenderWindow();
            senderWindow.Show();
            this.Hide();
            senderWindow.Closed += (s, args) => this.Show();
        }

        private void ReceiverButton_Click(object sender, RoutedEventArgs e)
        {
            var receiverWindow = new ReceiverWindow();
            receiverWindow.Show();
            this.Hide();
            receiverWindow.Closed += (s, args) => this.Show();
        }
    }
}