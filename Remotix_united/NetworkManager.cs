using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RemotixApp
{
    /// <summary>
    /// NetworkManager - מנהל תקשורת עם שרת האימות (TCP)
    /// 
    /// ⚠️ Singleton Pattern - מופע יחיד שנשאר מחובר!
    /// 
    /// תקשורת שרת ↔ לקוח (Authentication + Room Management):
    ///   - פרוטוקול: TCP על פורט 12345
    ///   - פורמט: [Code(1)][Size(4)][JSON(N)]
    ///   - מטרה: Login, SignUp, Create Room, Join Room, Logout
    /// 
    /// החיבור נשאר פתוח מ-Login ועד Logout!
    /// </summary>
    
    // Protocol codes from the server
    public static class RequestCodes
    {
        public const byte LOGIN_REQUEST = 1;
        public const byte SIGN_UP_REQUEST = 2;
        public const byte LOGOUT_REQUEST = 3;
        public const byte CREATE_ROOM_REQUEST = 4;
        public const byte JOIN_ROOM_REQUEST = 5;
        public const byte LEAVE_ROOM_REQUEST = 6;
    }

    public static class ResponseCodes
    {
        public const byte SUCCESS = 1;
        public const byte ERROR = 0;
    }

    // Request structures
    public class LoginRequest
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    public class SignUpRequest
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    // Response structures
    public class LoginResponse
    {
        public int status { get; set; }
    }

    public class SignUpResponse
    {
        public int status { get; set; }
    }

    public class ErrorResponse
    {
        public string message { get; set; }
    }

    public class NetworkManager
    {
        // Singleton instance
        private static NetworkManager _instance;
        private static readonly object _lock = new object();

        private TcpClient _client;
        private NetworkStream _stream;
        private string _serverIP;
        private int _serverPort;
        private bool _isConnected;

        // Singleton pattern
        public static NetworkManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            string serverIP = AppSettings.Instance.ServerIP;
                            _instance = new NetworkManager(serverIP);
                        }
                    }
                }
                return _instance;
            }
        }

        private NetworkManager(string serverIP, int serverPort = 12345)
        {
            _serverIP = serverIP;
            _serverPort = serverPort;
            _isConnected = false;
        }

        public bool IsConnected => _isConnected && _client != null && _client.Connected;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (IsConnected)
                {
                    return true; // Already connected
                }

                _client = new TcpClient();
                await _client.ConnectAsync(_serverIP, _serverPort);
                _stream = _client.GetStream();
                _isConnected = true;
                
                Console.WriteLine($"Connected to server at {_serverIP}:{_serverPort}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
                _isConnected = false;
                Console.WriteLine("Disconnected from server");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Disconnect error: {ex.Message}");
            }
        }

        private byte[] SerializeRequest(byte requestCode, object requestData)
        {
            // Serialize the request data to JSON
            string jsonString = JsonSerializer.Serialize(requestData);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            // Calculate message size
            int messageSize = jsonBytes.Length;

            // Create buffer: [code(1)] + [size(4)] + [json]
            byte[] buffer = new byte[5 + messageSize];

            // Set request code
            buffer[0] = requestCode;

            // Set message size (big-endian)
            buffer[1] = (byte)((messageSize >> 24) & 0xFF);
            buffer[2] = (byte)((messageSize >> 16) & 0xFF);
            buffer[3] = (byte)((messageSize >> 8) & 0xFF);
            buffer[4] = (byte)(messageSize & 0xFF);

            // Copy JSON data
            Array.Copy(jsonBytes, 0, buffer, 5, messageSize);

            return buffer;
        }

        private async Task<(byte responseCode, string jsonData)> ReceiveResponseAsync()
        {
            try
            {
                // Read header (5 bytes)
                byte[] header = new byte[5];
                int bytesRead = 0;
                while (bytesRead < 5)
                {
                    int read = await _stream.ReadAsync(header, bytesRead, 5 - bytesRead);
                    if (read == 0)
                        throw new Exception("Connection closed by server");
                    bytesRead += read;
                }

                // Extract response code and message size
                byte responseCode = header[0];
                int messageSize = (header[1] << 24) | (header[2] << 16) | (header[3] << 8) | header[4];

                // Read message data
                byte[] messageData = new byte[messageSize];
                bytesRead = 0;
                while (bytesRead < messageSize)
                {
                    int read = await _stream.ReadAsync(messageData, bytesRead, messageSize - bytesRead);
                    if (read == 0)
                        throw new Exception("Connection closed by server");
                    bytesRead += read;
                }

                string jsonData = Encoding.UTF8.GetString(messageData);
                return (responseCode, jsonData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Receive failed: {ex.Message}");
                throw;
            }
        }

        public async Task<(bool success, string message)> LoginAsync(string username, string password)
        {
            try
            {
                if (!IsConnected)
                {
                    bool connected = await ConnectAsync();
                    if (!connected)
                        return (false, "Could not connect to server");
                }

                // Create and send login request
                var loginRequest = new LoginRequest { username = username, password = password };
                byte[] requestData = SerializeRequest(RequestCodes.LOGIN_REQUEST, loginRequest);
                await _stream.WriteAsync(requestData, 0, requestData.Length);

                // Receive response
                var (responseCode, jsonData) = await ReceiveResponseAsync();

                if (responseCode == ResponseCodes.ERROR)
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(jsonData);
                    return (false, errorResponse?.message ?? "Unknown error");
                }
                else if (responseCode == ResponseCodes.SUCCESS)
                {
                    var loginResponse = JsonSerializer.Deserialize<LoginResponse>(jsonData);
                    if (loginResponse.status == 1)
                    {
                        // ✅ Connection stays open!
                        return (true, "Login successful");
                    }
                    else
                    {
                        return (false, "Invalid username or password");
                    }
                }
                else
                {
                    return (false, $"Unexpected response code: {responseCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
                _isConnected = false;
                return (false, $"Login error: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> SignUpAsync(string username, string password)
        {
            try
            {
                if (!IsConnected)
                {
                    bool connected = await ConnectAsync();
                    if (!connected)
                        return (false, "Could not connect to server");
                }

                // Create and send signup request
                var signUpRequest = new SignUpRequest { username = username, password = password };
                byte[] requestData = SerializeRequest(RequestCodes.SIGN_UP_REQUEST, signUpRequest);
                await _stream.WriteAsync(requestData, 0, requestData.Length);

                // Receive response
                var (responseCode, jsonData) = await ReceiveResponseAsync();

                if (responseCode == ResponseCodes.ERROR)
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(jsonData);
                    return (false, errorResponse?.message ?? "Unknown error");
                }
                else if (responseCode == ResponseCodes.SUCCESS)
                {
                    var signUpResponse = JsonSerializer.Deserialize<SignUpResponse>(jsonData);
                    if (signUpResponse.status == 1)
                    {
                        // ✅ Connection stays open!
                        return (true, "Registration successful");
                    }
                    else
                    {
                        return (false, "Username already exists");
                    }
                }
                else
                {
                    return (false, $"Unexpected response code: {responseCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sign up failed: {ex.Message}");
                _isConnected = false;
                return (false, $"Registration error: {ex.Message}");
            }
        }

        // TODO: Add CreateRoom, JoinRoom, LeaveRoom, Logout methods here
    }
}
