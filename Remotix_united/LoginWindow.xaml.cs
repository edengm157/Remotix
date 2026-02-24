using System;
using System.Windows;
using System.Windows.Input;

namespace RemotixApp
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            
            // Wire up event handlers
            LoginButton.Click += LoginButton_Click;
            GoToRegisterButton.Click += GoToRegisterButton_Click;
            
            // Allow Enter key to submit
            PasswordBox.KeyDown += PasswordBox_KeyDown;
            UsernameTextBox.KeyDown += UsernameTextBox_KeyDown;
        }

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PasswordBox.Focus();
            }
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text.Trim();
            string password = PasswordBox.Password;

            // Validation
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter a username.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter a password.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Disable button during login
            LoginButton.IsEnabled = false;
            LoginButton.Content = "LOGGING IN...";

            try
            {
                // ✅ Use singleton instance
                var (success, message) = await NetworkManager.Instance.LoginAsync(username, password);

                if (success)
                {
                    // Store the logged-in username
                    AppSettings.Instance.CurrentUsername = username;

                    // Show success message
                    MessageBox.Show("Login successful!", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Open MenuWindow
                    MenuWindow menuWindow = new MenuWindow();
                    menuWindow.Show();
                    
                    // Close login window
                    // ✅ Connection stays open - no Disconnect!
                    this.Close();
                }
                else
                {
                    MessageBox.Show(message, "Login Failed", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable button
                LoginButton.IsEnabled = true;
                LoginButton.Content = "LOGIN";
            }
        }

        private void GoToRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            // Open registration window
            RegisterWindow registerWindow = new RegisterWindow();
            registerWindow.Show();
            
            // Close login window
            this.Close();
        }

        // ✅ REMOVED OnClosed - no Disconnect!
        // Connection stays alive for MenuWindow
    }
}
