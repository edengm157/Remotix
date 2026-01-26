using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Receiver
{
    /// <summary>
    /// שולח אירועי קלט דרך UDP לצד המקבל
    /// </summary>
    internal class InputSender : IDisposable
    {
        private UdpClient _udpClient;
        private IPEndPoint _remoteTarget;
        private ConcurrentQueue<InputEvent> _eventQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _sendTask;
        private bool _isRunning = false;

        private readonly string _logPath = "input_sender.log";
        private int _sentEvents = 0;

        public InputSender()
        {
            _udpClient = new UdpClient();
            //_remoteTarget = new IPEndPoint(IPAddress.Loopback, 12346); // פורט נפרד לאירועי קלט
            _remoteTarget = new IPEndPoint(IPAddress.Parse("10.0.0.7"), 12346);
            _eventQueue = new ConcurrentQueue<InputEvent>();

            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🚀 InputSender initialized - port 12346");
        }

        /// <summary>
        /// מתחיל את שליחת האירועים
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                WriteLog("⚠️ InputSender already running");
                return;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _sendTask = Task.Run(() => SendLoop(_cancellationTokenSource.Token));

            WriteLog("▶️ InputSender started");
        }

        /// <summary>
        /// מוסיף אירוע לתור לשליחה
        /// </summary>
        public void QueueEvent(InputEvent evt)
        {
            if (!_isRunning)
            {
                return;
            }

            _eventQueue.Enqueue(evt);
        }

        /// <summary>
        /// לולאת שליחה - שולחת אירועים מהתור
        /// </summary>
        private async Task SendLoop(CancellationToken ct)
        {
            WriteLog("🔄 Send loop started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_eventQueue.TryDequeue(out InputEvent evt))
                    {
                        byte[] data = evt.ToBytes();

                        int bytesSent = _udpClient.Send(data, data.Length, _remoteTarget);
                        _sentEvents++;

                        if (_sentEvents % 50 == 0 || _sentEvents <= 5)
                        {
                            WriteLog($"📤 Event #{_sentEvents} sent: {evt}");
                        }
                    }
                    else
                    {
                        // אין אירועים בתור - המתן קצת
                        await Task.Delay(1, ct);
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ Send error: {ex.Message}");
                }
            }

            WriteLog("🛑 Send loop ended");
        }

        /// <summary>
        /// עוצר את שליחת האירועים
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            _sendTask?.Wait(TimeSpan.FromSeconds(2));

            WriteLog($"✅ InputSender stopped - total events sent: {_sentEvents}");
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // התעלם משגיאות כתיבה
            }
        }

        public void Dispose()
        {
            Stop();
            _udpClient?.Close();
            _udpClient?.Dispose();
            WriteLog("🗑️ InputSender disposed");
        }
    }
}