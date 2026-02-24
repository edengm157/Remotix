using System;
using System.Windows;
using System.Windows.Input;

namespace RemotixApp
{
    /// <summary>
    /// Interaction logic for RegisterWindow.xaml
    /// </summary>
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();

            // Wire up event handlers
            RegisterButton.Click += RegisterButton_Click;
            BackToLoginButton.Click += BackToLoginButton_Click;
            
            // Allow Enter key navigation
            RegUsernameTextBox.KeyDown += RegUsernameTextBox_KeyDown;
            RegPasswordBox.KeyDown += RegPasswordBox_KeyDown;
            ConfirmPasswordBox.KeyDown += ConfirmPasswordBox_KeyDown;
        }

        private void RegUsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RegPasswordBox.Focus();
            }
        }

        private void RegPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmPasswordBox.Focus();
            }
        }

        private void ConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RegisterButton_Click(sender, e);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = RegUsernameTextBox.Text.Trim();
            string password = RegPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            // Validation
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter a username.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (username.Length < 3)
            {
                MessageBox.Show("Username must be at least 3 characters long.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter a password.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password.Length < 4)
            {
                MessageBox.Show("Password must be at least 4 characters long.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password != confirmPassword)
            {
                MessageBox.Show("Passwords do not match.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ConfirmPasswordBox.Clear();
                ConfirmPasswordBox.Focus();
                return;
            }

            // Disable button during registration
            RegisterButton.IsEnabled = false;
            RegisterButton.Content = "SIGNING UP...";

            try
            {
                // ✅ Use singleton instance
                var (success, message) = await NetworkManager.Instance.SignUpAsync(username, password);

                if (success)
                {
                    MessageBox.Show("Registration successful! You can now log in.", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Go back to login window
                    LoginWindow loginWindow = new LoginWindow();
                    loginWindow.Show();
                    
                    // Close registration window
                    // ✅ Connection stays open
                    this.Close();
                }
                else
                {
                    MessageBox.Show(message, "Registration Failed", 
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
                RegisterButton.IsEnabled = true;
                RegisterButton.Content = "SIGN UP";
            }
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            // Go back to login window
            LoginWindow loginWindow = new LoginWindow();
            loginWindow.Show();
            
            // Close registration window
            this.Close();
        }

        // ✅ REMOVED OnClosed - no Disconnect!
    }
}
