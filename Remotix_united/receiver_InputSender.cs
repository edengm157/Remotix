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
    //internal class InputSender : IDisposable
    //{
    //    private UdpClient _udpClient;
    //    private IPEndPoint _remoteTarget;
    //    private ConcurrentQueue<InputEvent> _eventQueue;
    //    private CancellationTokenSource _cancellationTokenSource;
    //    private Task _sendTask;
    //    private bool _isRunning = false;

    //    private readonly string _logPath = "input_sender.log";
    //    private int _sentEvents = 0;

    //    public InputSender()
    //    {
    //        _udpClient = new UdpClient();
    //        //_remoteTarget = new IPEndPoint(IPAddress.Loopback, 12346); // פורט נפרד לאירועי קלט
    //        _remoteTarget = new IPEndPoint(IPAddress.Parse("10.0.0.7"), 12346);
    //        _eventQueue = new ConcurrentQueue<InputEvent>();

    //        if (File.Exists(_logPath))
    //        {
    //            File.Delete(_logPath);
    //        }
    //        WriteLog("🚀 InputSender initialized - port 12346");
    //    }

    //    /// <summary>
    //    /// מתחיל את שליחת האירועים
    //    /// </summary>
    //    public void Start()
    //    {
    //        if (_isRunning)
    //        {
    //            WriteLog("⚠️ InputSender already running");
    //            return;
    //        }

    //        _isRunning = true;
    //        _cancellationTokenSource = new CancellationTokenSource();
    //        _sendTask = Task.Run(() => SendLoop(_cancellationTokenSource.Token));

    //        WriteLog("▶️ InputSender started");
    //    }

    //    /// <summary>
    //    /// מוסיף אירוע לתור לשליחה
    //    /// </summary>
    //    public void QueueEvent(InputEvent evt)
    //    {
    //        if (!_isRunning)
    //        {
    //            return;
    //        }

    //        _eventQueue.Enqueue(evt);
    //    }

    //    /// <summary>
    //    /// לולאת שליחה - שולחת אירועים מהתור
    //    /// </summary>
    //    private async Task SendLoop(CancellationToken ct)
    //    {
    //        WriteLog("🔄 Send loop started");

    //        while (!ct.IsCancellationRequested)
    //        {
    //            try
    //            {
    //                if (_eventQueue.TryDequeue(out InputEvent evt))
    //                {
    //                    byte[] data = evt.ToBytes();

    //                    int bytesSent = _udpClient.Send(data, data.Length, _remoteTarget);
    //                    _sentEvents++;

    //                    if (_sentEvents % 50 == 0 || _sentEvents <= 5)
    //                    {
    //                        WriteLog($"📤 Event #{_sentEvents} sent: {evt}");
    //                    }
    //                }
    //                else
    //                {
    //                    // אין אירועים בתור - המתן קצת
    //                    await Task.Delay(1, ct);
    //                }
    //            }
    //            catch (Exception ex)
    //            {
    //                WriteLog($"❌ Send error: {ex.Message}");
    //            }
    //        }

    //        WriteLog("🛑 Send loop ended");
    //    }

    //    /// <summary>
    //    /// עוצר את שליחת האירועים
    //    /// </summary>
    //    public void Stop()
    //    {
    //        if (!_isRunning)
    //        {
    //            return;
    //        }

    //        _isRunning = false;
    //        _cancellationTokenSource?.Cancel();
    //        _sendTask?.Wait(TimeSpan.FromSeconds(2));

    //        WriteLog($"✅ InputSender stopped - total events sent: {_sentEvents}");
    //    }

    //    private void WriteLog(string message)
    //    {
    //        try
    //        {
    //            File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
    //        }
    //        catch
    //        {
    //            // התעלם משגיאות כתיבה
    //        }
    //    }

    //    public void Dispose()
    //    {
    //        Stop();
    //        _udpClient?.Close();
    //        _udpClient?.Dispose();
    //        WriteLog("🗑️ InputSender disposed");
    //    }
    //}

    /// <summary>
    /// שולח אירועי קלט ל-SENDER באמצעות UDP
    /// </summary>
    public class InputSender : IDisposable
    {
        private UdpClient _udpClient;
        private IPEndPoint _remoteTarget;

        private ConcurrentQueue<InputEvent> _eventQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _sendTask;

        private long _totalEventsSent = 0;
        private readonly string _logPath = "receiver_input_sender.log";

        // ⚠️ חשוב: עדכן את ה-IP הזה ל-IP של מחשב ה-SENDER!
        private const string SENDER_IP = "10.0.0.22"; // ⚠️ שנה את זה!
        private const int INPUT_PORT = 12346;

        public InputSender()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            _udpClient = new UdpClient();
            _remoteTarget = new IPEndPoint(IPAddress.Parse(SENDER_IP), INPUT_PORT);
            _eventQueue = new ConcurrentQueue<InputEvent>();

            LogToFile("═══════════════════════════════════════════════════");
            LogToFile($"📤 InputSender initialized - Target: {SENDER_IP}:{INPUT_PORT}");
            LogToFile("═══════════════════════════════════════════════════");

            // התחל send loop
            _cancellationTokenSource = new CancellationTokenSource();
            _sendTask = Task.Run(() => SendLoop(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// עדכן את IP של ה-SENDER
        /// </summary>
        public void SetSenderIp(string senderIp)
        {
            _remoteTarget = new IPEndPoint(IPAddress.Parse(senderIp), INPUT_PORT);
            LogToFile($"📡 Sender IP updated to: {senderIp}:{INPUT_PORT}");
        }

        /// <summary>
        /// הוסף אירוע לתור לשליחה
        /// </summary>
        public void QueueEvent(InputEvent evt)
        {
            _eventQueue.Enqueue(evt);
        }

        private async Task SendLoop(CancellationToken ct)
        {
            LogToFile("🔄 Send loop started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // שלח עד 100 אירועים בכל איטרציה
                    int sent = 0;
                    while (sent < 100 && _eventQueue.TryDequeue(out InputEvent evt))
                    {
                        try
                        {
                            byte[] data = evt.ToBytes();
                            await _udpClient.SendAsync(data, data.Length, _remoteTarget);

                            _totalEventsSent++;
                            sent++;

                            if (_totalEventsSent <= 5 || _totalEventsSent % 100 == 0)
                            {
                                LogToFile($"📤 Sent event #{_totalEventsSent}: {evt}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"❌ Failed to send event: {ex.Message}");
                        }
                    }

                    // אם לא שלחנו כלום, חכה קצת
                    if (sent == 0)
                    {
                        await Task.Delay(5, ct);
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Send loop error: {ex.Message}");
                    await Task.Delay(100, ct);
                }
            }

            LogToFile($"🛑 Send loop ended. Total events sent: {_totalEventsSent}");
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void Dispose()
        {
            LogToFile("🛑 Disposing InputSender...");

            _cancellationTokenSource?.Cancel();
            _sendTask?.Wait(TimeSpan.FromSeconds(2));

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _cancellationTokenSource?.Dispose();

            LogToFile($"📊 Final stats: {_totalEventsSent} events sent");
            LogToFile("🗑️ InputSender disposed");
        }
    }
}