using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace sender
{
    /// <summary>
    /// מקבל פעולות קלט מהמקבל ומבצע אותן במחשב ששולח את המסך
    /// </summary>
    internal class InputReceiver : IDisposable
    {
        private UdpClient _udpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private bool _isRunning = false;

        private long _totalEventsReceived = 0;
        private long _totalEventsExecuted = 0;
        private readonly string _logPath = "sender_input_receiver.log";

        // פורט לאירועי קלט
        private const int INPUT_PORT = 12346;

        // Win32 API constants
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_ABSOLUTE = 0x8000;

        public InputReceiver()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🎮 Sender InputReceiver initialized - will execute commands from remote viewer");
        }

        public void Start()
        {
            if (_isRunning)
                return;

            try
            {
                _udpClient = new UdpClient(INPUT_PORT);
                _udpClient.Client.ReceiveBufferSize = 64 * 1024;

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                _receiveTask = Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token));

                WriteLog($"▶️ Started receiving remote input commands on port {INPUT_PORT}");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error starting receiver: {ex.Message}");
                throw;
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            WriteLog("🔄 Receive loop started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    byte[] data = result.Buffer;

                    if (data.Length < 17)
                    {
                        WriteLog($"⚠️ Invalid packet size: {data.Length} bytes");
                        continue;
                    }

                    var inputEvent = InputEvent.FromBytes(data);
                    _totalEventsReceived++;

                    if (_totalEventsReceived % 50 == 0 || _totalEventsReceived <= 5)
                    {
                        WriteLog($"📥 Remote command #{_totalEventsReceived} received: {inputEvent}");
                    }

                    ExecuteInputEvent(inputEvent);
                }
                catch (ObjectDisposedException)
                {
                    WriteLog("⚠️ UDP client disposed, exiting receive loop");
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ Receive error: {ex.Message}");
                }
            }

            WriteLog($"🛑 Receive loop ended. Total: {_totalEventsReceived}, Executed: {_totalEventsExecuted}");
        }

        private void ExecuteInputEvent(InputEvent evt)
        {
            try
            {
                switch (evt.Type)
                {
                    case InputEventType.KeyDown:
                        SimulateKeyPress(evt.Data1, true);
                        break;

                    case InputEventType.KeyUp:
                        SimulateKeyPress(evt.Data1, false);
                        break;

                    case InputEventType.MouseMove:
                        SimulateMouseMove(evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseLeftDown:
                        SimulateMouseClick(MOUSEEVENTF_LEFTDOWN, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseLeftUp:
                        SimulateMouseClick(MOUSEEVENTF_LEFTUP, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseRightDown:
                        SimulateMouseClick(MOUSEEVENTF_RIGHTDOWN, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseRightUp:
                        SimulateMouseClick(MOUSEEVENTF_RIGHTUP, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseMiddleDown:
                        SimulateMouseClick(MOUSEEVENTF_MIDDLEDOWN, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseMiddleUp:
                        SimulateMouseClick(MOUSEEVENTF_MIDDLEUP, evt.Data1, evt.Data2);
                        break;

                    case InputEventType.MouseWheel:
                        SimulateMouseWheel(evt.Data1);
                        break;
                }

                _totalEventsExecuted++;
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error executing event {evt.Type}: {ex.Message}");
            }
        }

        private void SimulateKeyPress(int vkCode, bool isDown)
        {
            keybd_event((byte)vkCode, 0, isDown ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP, 0);
        }

        private void SimulateMouseMove(int x, int y)
        {
            int screenWidth = GetSystemMetrics(0);
            int screenHeight = GetSystemMetrics(1);

            int absoluteX = (x * 65536) / screenWidth;
            int absoluteY = (y * 65536) / screenHeight;

            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, absoluteX, absoluteY, 0, 0);
        }

        private void SimulateMouseClick(int mouseEvent, int x, int y)
        {
            SimulateMouseMove(x, y);
            mouse_event(mouseEvent, 0, 0, 0, 0);
        }

        private void SimulateMouseWheel(int delta)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, 0);
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            WriteLog("🛑 Stopping input receiver...");

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            try
            {
                _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ Error waiting for receive task: {ex.Message}");
            }

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            WriteLog($"✅ Stopped. Total: {_totalEventsReceived}, Executed: {_totalEventsExecuted}");
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
            WriteLog("🗑️ InputReceiver disposed");
        }

        #region Win32 API

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        #endregion
    }
}