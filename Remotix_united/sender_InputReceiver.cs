/*
═══════════════════════════════════════════════════════════════════════
קובץ: InputReceiver.cs
תיאור: מקבל ומעבד input מכמה משתמשים בו-זמנית (Collaborative Control)
מיקום: sender/ (בפרויקט SENDER)

שימוש:
1. העתק את הקוד הזה
2. החלף את InputReceiver.cs בפרויקט SENDER
3. ודא שיש לך 
═══════════════════════════════════════════════════════════════════════
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace sender
{
    /// <summary>
    /// מנהל קלט שיתופי - מאפשר למספר משתמשים לשלוט במקביל
    /// - תנועות עכבר וגלילה: ממוצע של כל המשתמשים
    /// - קליקים ומקלדת: מעבר ישירות (FIFO)
    /// </summary>
    public class InputReceiver : IDisposable
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const int MOUSEEVENTF_WHEEL = 0x0800;

        #endregion

        #region Configuration

        private const int TICK_RATE_MS = 16; // ~60Hz processing
        private const int MAX_EVENT_AGE_MS = 100; // סנן אירועים ישנים מדי
        private const int INPUT_PORT = 12346;

        #endregion

        #region Data Structures

        private class InputBatch
        {
            public List<MouseMoveEvent> MouseMoves = new List<MouseMoveEvent>();
            public List<WheelEvent> WheelEvents = new List<WheelEvent>();
            public Queue<DirectEvent> DirectEvents = new Queue<DirectEvent>();
        }

        private class MouseMoveEvent
        {
            public int DeltaX;
            public int DeltaY;
            public string SenderId;
            public long Timestamp;
        }

        private class WheelEvent
        {
            public int Delta;
            public string SenderId;
            public long Timestamp;
        }

        private class DirectEvent
        {
            public InputEvent Event;
            public long ReceivedTime;
        }

        #endregion

        #region Private Fields

        private UdpClient _udpClient;
        private InputBatch _currentBatch = new InputBatch();
        private readonly object _batchLock = new object();

        // סטטיסטיקות משתמשים
        private Dictionary<string, long> _eventCountByUser = new Dictionary<string, long>();
        private Dictionary<string, DateTime> _lastSeenByUser = new Dictionary<string, DateTime>();
        private long _totalEventsProcessed = 0;
        private int _activeUsers = 0;

        // Tasks
        private CancellationTokenSource _cancellationTokenSource;
        private Task _processingTask;
        private Task _receiveTask;
        private bool _isRunning = false;

        // מיקום סמן נוכחי
        private int _currentCursorX = 0;
        private int _currentCursorY = 0;
        private int _screenWidth;
        private int _screenHeight;

        private readonly string _logPath = "sender_collaborative_input.log";
        private Stopwatch _stopwatch = new Stopwatch();

        #endregion

        public InputReceiver()
        {
            // מחק log ישן
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            // קבל גודל מסך
            _screenWidth = GetSystemMetrics(0);  // SM_CXSCREEN
            _screenHeight = GetSystemMetrics(1); // SM_CYSCREEN

            // קבל מיקום סמן נוכחי
            GetCursorPos(out POINT p);
            _currentCursorX = p.X;
            _currentCursorY = p.Y;

            WriteLog("═══════════════════════════════════════════════════");
            WriteLog("🎮 Collaborative Input Manager initialized");
            WriteLog($"📐 Screen: {_screenWidth}x{_screenHeight}");
            WriteLog($"🖱️ Initial cursor: ({_currentCursorX}, {_currentCursorY})");
            WriteLog($"⏱️ Tick rate: {TICK_RATE_MS}ms (~{1000 / TICK_RATE_MS}Hz)");
            WriteLog("═══════════════════════════════════════════════════");
        }

        public void Start()
        {
            if (_isRunning)
            {
                WriteLog("⚠️ Already running");
                return;
            }

            try
            {
                _udpClient = new UdpClient(INPUT_PORT);
                _udpClient.Client.ReceiveBufferSize = 128 * 1024;

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // התחל processing loop (60Hz)
                _processingTask = Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token));

                // התחל receive loop
                _receiveTask = Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token));

                _stopwatch.Start();

                WriteLog($"✅ Started on port {INPUT_PORT} - accepting multiple controllers");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error starting: {ex.Message}");
                throw;
            }
        }

        #region Network Receiving

        private async Task ReceiveLoop(CancellationToken ct)
        {
            WriteLog("🔄 Receive loop started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    byte[] data = result.Buffer;
                    string sourceIp = result.RemoteEndPoint.Address.ToString();

                    if (data.Length < 26)
                    {
                        WriteLog($"⚠️ Invalid packet from {sourceIp}: {data.Length} bytes (expected 26+)");
                        continue;
                    }

                    var inputEvent = InputEvent.FromBytes(data);
                    QueueInputEvent(inputEvent, sourceIp);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ Receive error: {ex.Message}");
                }
            }

            WriteLog($"🛑 Receive loop ended. Total events: {_totalEventsProcessed}");
        }

        public void QueueInputEvent(InputEvent evt, string sourceIp)
        {
            if (!_isRunning) return;

            // עדכן סטטיסטיקות משתמש
            if (!_eventCountByUser.ContainsKey(evt.SenderId))
            {
                _eventCountByUser[evt.SenderId] = 0;
                WriteLog($"🆕 New user: {evt.SenderId} from {sourceIp}");
            }
            _eventCountByUser[evt.SenderId]++;
            _lastSeenByUser[evt.SenderId] = DateTime.Now;

            // הוסף לbatch הנוכחי
            lock (_batchLock)
            {
                switch (evt.Type)
                {
                    case InputEventType.MouseMove:
                        _currentBatch.MouseMoves.Add(new MouseMoveEvent
                        {
                            DeltaX = evt.Data1,
                            DeltaY = evt.Data2,
                            SenderId = evt.SenderId,
                            Timestamp = evt.Timestamp
                        });
                        break;

                    case InputEventType.MouseWheel:
                        _currentBatch.WheelEvents.Add(new WheelEvent
                        {
                            Delta = evt.Data1,
                            SenderId = evt.SenderId,
                            Timestamp = evt.Timestamp
                        });
                        break;

                    case InputEventType.KeyDown:
                    case InputEventType.KeyUp:
                    case InputEventType.MouseLeftDown:
                    case InputEventType.MouseLeftUp:
                    case InputEventType.MouseRightDown:
                    case InputEventType.MouseRightUp:
                    case InputEventType.MouseMiddleDown:
                    case InputEventType.MouseMiddleUp:
                        _currentBatch.DirectEvents.Enqueue(new DirectEvent
                        {
                            Event = evt,
                            ReceivedTime = _stopwatch.ElapsedMilliseconds
                        });
                        break;
                }
            }

            _totalEventsProcessed++;
        }

        #endregion

        #region Processing Loop (Collaborative Control)

        private async Task ProcessingLoop(CancellationToken ct)
        {
            WriteLog("🔄 Processing loop started (collaborative control)");

            while (!ct.IsCancellationRequested)
            {
                long tickStart = _stopwatch.ElapsedMilliseconds;

                try
                {
                    ProcessCurrentBatch();
                    UpdateActiveUsers();

                    // לוג סטטיסטיקות כל 5 שניות
                    if (_stopwatch.ElapsedMilliseconds % 5000 < TICK_RATE_MS)
                    {
                        LogStatistics();
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ Processing error: {ex.Message}");
                }

                // חכה עד הטיק הבא
                long tickDuration = _stopwatch.ElapsedMilliseconds - tickStart;
                int sleepTime = Math.Max(0, TICK_RATE_MS - (int)tickDuration);

                if (sleepTime > 0)
                {
                    await Task.Delay(sleepTime, ct);
                }
            }

            WriteLog("🛑 Processing loop ended");
        }

        private void ProcessCurrentBatch()
        {
            InputBatch batch;

            // החלף batch (atomic swap)
            lock (_batchLock)
            {
                batch = _currentBatch;
                _currentBatch = new InputBatch();
            }

            long now = _stopwatch.ElapsedMilliseconds;

            // ═══════════════════════════════════════════════════════
            // 1. תזוזות עכבר - ממוצע Δx̄ = (Σ Δx_i) / N
            // ═══════════════════════════════════════════════════════
            if (batch.MouseMoves.Count > 0)
            {
                // סנן אירועים ישנים מדי
                var recentMoves = batch.MouseMoves
                    .Where(m => (now - m.Timestamp) < MAX_EVENT_AGE_MS)
                    .ToList();

                if (recentMoves.Count > 0)
                {
                    // חשב ממוצע delta
                    int avgDeltaX = (int)Math.Round(recentMoves.Average(m => m.DeltaX));
                    int avgDeltaY = (int)Math.Round(recentMoves.Average(m => m.DeltaY));

                    // חשב מיקום חדש
                    int newX = _currentCursorX + avgDeltaX;
                    int newY = _currentCursorY + avgDeltaY;

                    // הגבל לגבולות מסך
                    newX = Math.Max(0, Math.Min(_screenWidth - 1, newX));
                    newY = Math.Max(0, Math.Min(_screenHeight - 1, newY));

                    // עדכן מיקום
                    _currentCursorX = newX;
                    _currentCursorY = newY;

                    // הזז סמן
                    SetCursorPos(_currentCursorX, _currentCursorY);

                    // לוג (רק אם יש תזוזה משמעותית)
                    if (Math.Abs(avgDeltaX) > 5 || Math.Abs(avgDeltaY) > 5)
                    {
                        WriteLog($"🖱️ Mouse: Avg Δ({avgDeltaX:+#;-#;0}, {avgDeltaY:+#;-#;0}) from {recentMoves.Count} user(s) → ({_currentCursorX}, {_currentCursorY})");
                    }
                }
            }

            // ═══════════════════════════════════════════════════════
            // 2. גלילה - ממוצע
            // ═══════════════════════════════════════════════════════
            if (batch.WheelEvents.Count > 0)
            {
                var recentWheels = batch.WheelEvents
                    .Where(w => (now - w.Timestamp) < MAX_EVENT_AGE_MS)
                    .ToList();

                if (recentWheels.Count > 0)
                {
                    int avgDelta = (int)Math.Round(recentWheels.Average(w => w.Delta));

                    // בצע גלילה
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, avgDelta, 0);

                    WriteLog($"🎡 Wheel: Avg Δ{avgDelta:+#;-#;0} from {recentWheels.Count} user(s)");
                }
            }

            // ═══════════════════════════════════════════════════════
            // 3. אירועים ישירים (קליקים, מקלדת) - FIFO
            // ═══════════════════════════════════════════════════════
            while (batch.DirectEvents.Count > 0)
            {
                var directEvent = batch.DirectEvents.Dequeue();

                // בדוק שהאירוע לא ישן מדי
                if ((now - directEvent.ReceivedTime) < MAX_EVENT_AGE_MS)
                {
                    ExecuteDirectEvent(directEvent.Event);
                }
                else
                {
                    WriteLog($"⚠️ Dropped old event: {directEvent.Event}");
                }
            }
        }

        private void ExecuteDirectEvent(InputEvent evt)
        {
            try
            {
                switch (evt.Type)
                {
                    case InputEventType.KeyDown:
                        keybd_event((byte)evt.Data1, 0, 0, 0);
                        WriteLog($"⌨️ KeyDown: {evt.SenderId} → VK=0x{evt.Data1:X2}");
                        break;

                    case InputEventType.KeyUp:
                        keybd_event((byte)evt.Data1, 0, KEYEVENTF_KEYUP, 0);
                        WriteLog($"⌨️ KeyUp: {evt.SenderId} → VK=0x{evt.Data1:X2}");
                        break;

                    case InputEventType.MouseLeftDown:
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        WriteLog($"🖱️ LeftClick Down: {evt.SenderId}");
                        break;

                    case InputEventType.MouseLeftUp:
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        WriteLog($"🖱️ LeftClick Up: {evt.SenderId}");
                        break;

                    case InputEventType.MouseRightDown:
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        WriteLog($"🖱️ RightClick Down: {evt.SenderId}");
                        break;

                    case InputEventType.MouseRightUp:
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        WriteLog($"🖱️ RightClick Up: {evt.SenderId}");
                        break;

                    case InputEventType.MouseMiddleDown:
                        mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                        WriteLog($"🖱️ MiddleClick Down: {evt.SenderId}");
                        break;

                    case InputEventType.MouseMiddleUp:
                        mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                        WriteLog($"🖱️ MiddleClick Up: {evt.SenderId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error executing {evt.Type} from {evt.SenderId}: {ex.Message}");
            }
        }

        #endregion

        #region Statistics

        private void UpdateActiveUsers()
        {
            var now = DateTime.Now;

            lock (_batchLock)
            {
                // ספור רק משתמשים שהיו פעילים ב-5 שניות האחרונות
                _activeUsers = _lastSeenByUser
                    .Count(u => (now - u.Value).TotalSeconds < 5);
            }
        }

        private void LogStatistics()
        {
            lock (_batchLock)
            {
                WriteLog("═══════════════════════════════════════");
                WriteLog($"📊 Collaborative Control Stats:");
                WriteLog($"  Active users: {_activeUsers}");
                WriteLog($"  Total events: {_totalEventsProcessed}");

                if (_eventCountByUser.Count > 0)
                {
                    WriteLog($"  Events by user:");
                    foreach (var user in _eventCountByUser.OrderByDescending(u => u.Value).Take(5))
                    {
                        var lastSeen = _lastSeenByUser.ContainsKey(user.Key)
                            ? $"{(DateTime.Now - _lastSeenByUser[user.Key]).TotalSeconds:F1}s ago"
                            : "never";
                        WriteLog($"    • {user.Key}: {user.Value} events (last: {lastSeen})");
                    }
                }

                WriteLog($"  Cursor: ({_currentCursorX}, {_currentCursorY})");
                WriteLog("═══════════════════════════════════════");
            }
        }

        #endregion

        public void Stop()
        {
            WriteLog("🛑 Stopping collaborative input...");

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            _processingTask?.Wait(TimeSpan.FromSeconds(2));

            LogStatistics();
        }

        private void WriteLog(string message)
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
            Stop();

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _cancellationTokenSource?.Dispose();

            WriteLog("🗑️ Collaborative InputReceiver disposed");
        }
    }
}

/*
═══════════════════════════════════════════════════════════════════════
שימוש:
═══════════════════════════════════════════════════════════════════════

// יצירה
var inputReceiver = new InputReceiver();

// התחלה
inputReceiver.Start();

// בסוף
inputReceiver.Stop();
inputReceiver.Dispose();

═══════════════════════════════════════════════════════════════════════
*/

