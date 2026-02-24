using System;
using System.Windows;

namespace RemotixApp
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Load current IP address
            IpAddressTextBox.Text = AppSettings.Instance.ServerIP;
            
            // Wire up event handler
            SaveSettingsButton.Click += SaveSettingsButton_Click;
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            string ipAddress = IpAddressTextBox.Text.Trim();

            // Basic validation
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                MessageBox.Show("Please enter an IP address.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Simple IP validation (can be improved)
            if (!IsValidIP(ipAddress))
            {
                MessageBox.Show("Please enter a valid IP address (e.g., 192.168.1.100 or 127.0.0.1).", 
                    "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save settings
            AppSettings.Instance.ServerIP = ipAddress;
            AppSettings.Instance.Save();

            MessageBox.Show("Settings saved successfully!", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Close window
            this.Close();
        }

        private bool IsValidIP(string ipAddress)
        {
            // Allow "localhost" as a special case
            if (ipAddress.ToLower() == "localhost")
                return true;

            // Check if it's a valid IPv4 address
            string[] parts = ipAddress.Split('.');
            if (parts.Length != 4)
                return false;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int value))
                    return false;
                if (value < 0 || value > 255)
                    return false;
            }

            return true;
        }
    }
}
