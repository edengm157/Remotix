using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace sender
{
    /// <summary>
    /// מערכת לוגינג של כל פעולות המקלדת והעכבר במהלך ה-Capture
    /// </summary>
    internal class InputLogger : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEMOVE = 0x0200;

        private IntPtr _keyboardHookID = IntPtr.Zero;
        private IntPtr _mouseHookID = IntPtr.Zero;
        private LowLevelKeyboardProc _keyboardProc;
        private LowLevelMouseProc _mouseProc;

        private readonly string _logPath = "input_capture.log";
        private bool _isLogging = false;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        // למניעת spam של mouse move
        private DateTime _lastMouseMoveLog = DateTime.MinValue;
        private const int MOUSE_MOVE_LOG_INTERVAL_MS = 500;

        public InputLogger()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🎮 InputLogger initialized");
            WriteLog("📝 This log tracks all keyboard and mouse activity during screen capture");
            WriteLog("═══════════════════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// מתחיל את תהליך הלוגינג
        /// </summary>
        public void StartLogging()
        {
            if (_isLogging)
            {
                WriteLog("⚠️ Logging already active");
                return;
            }

            try
            {
                _stopwatch.Restart();

                // התקנת hook למקלדת
                _keyboardProc = KeyboardHookCallback;
                _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);

                // התקנת hook לעכבר
                _mouseProc = MouseHookCallback;
                _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);

                _isLogging = true;

                WriteLog("");
                WriteLog("▶️ INPUT LOGGING STARTED");
                WriteLog($"⏰ Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                WriteLog("───────────────────────────────────────────────────────────────────────");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error starting input logging: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// עוצר את תהליך הלוגינג
        /// </summary>
        public void StopLogging()
        {
            if (!_isLogging)
            {
                return;
            }

            try
            {
                _stopwatch.Stop();

                // הסרת hooks
                if (_keyboardHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookID);
                    _keyboardHookID = IntPtr.Zero;
                }

                if (_mouseHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookID);
                    _mouseHookID = IntPtr.Zero;
                }

                _isLogging = false;

                WriteLog("───────────────────────────────────────────────────────────────────────");
                WriteLog($"⏱️ Total Capture Duration: {_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
                WriteLog("⏹️ INPUT LOGGING STOPPED");
                WriteLog("");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error stopping input logging: {ex.Message}");
            }
        }

        private IntPtr SetHook(Delegate proc, int hookType)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isLogging)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    string keyName = GetKeyName(vkCode);
                    string modifiers = GetCurrentModifiers();

                    WriteLog($"⌨️ [{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] KEYDOWN: {keyName}{modifiers}");
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    string keyName = GetKeyName(vkCode);
                    WriteLog($"⌨️ [{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] KEYUP: {keyName}");
                }
            }

            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isLogging)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                string timestamp = $"[{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}]";

                switch ((int)wParam)
                {
                    case WM_LBUTTONDOWN:
                        WriteLog($"🖱️ {timestamp} LEFT CLICK DOWN at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_LBUTTONUP:
                        WriteLog($"🖱️ {timestamp} LEFT CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_RBUTTONDOWN:
                        WriteLog($"🖱️ {timestamp} RIGHT CLICK DOWN at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_RBUTTONUP:
                        WriteLog($"🖱️ {timestamp} RIGHT CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_MBUTTONDOWN:
                        WriteLog($"🖱️ {timestamp} MIDDLE CLICK DOWN at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_MBUTTONUP:
                        WriteLog($"🖱️ {timestamp} MIDDLE CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_MOUSEWHEEL:
                        int delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                        string direction = delta > 0 ? "UP ⬆️" : "DOWN ⬇️";
                        int scrollAmount = Math.Abs(delta / 120); // 120 units = 1 "click"
                        WriteLog($"🖱️ {timestamp} SCROLL {direction} ({scrollAmount} clicks, delta={delta}) at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        break;

                    case WM_MOUSEMOVE:
                        // לוג mouse move רק כל חצי שנייה למניעת spam
                        if ((DateTime.Now - _lastMouseMoveLog).TotalMilliseconds >= MOUSE_MOVE_LOG_INTERVAL_MS)
                        {
                            WriteLog($"🖱️ {timestamp} MOUSE MOVE to ({hookStruct.pt.x}, {hookStruct.pt.y})");
                            _lastMouseMoveLog = DateTime.Now;
                        }
                        break;
                }
            }

            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private string GetKeyName(int vkCode)
        {
            // מיפוי של מקשים נפוצים
            switch (vkCode)
            {
                case 0x08: return "BACKSPACE";
                case 0x09: return "TAB";
                case 0x0D: return "ENTER";
                case 0x10: return "SHIFT";
                case 0x11: return "CTRL";
                case 0x12: return "ALT";
                case 0x1B: return "ESC";
                case 0x20: return "SPACE";
                case 0x21: return "PAGE_UP";
                case 0x22: return "PAGE_DOWN";
                case 0x23: return "END";
                case 0x24: return "HOME";
                case 0x25: return "LEFT_ARROW";
                case 0x26: return "UP_ARROW";
                case 0x27: return "RIGHT_ARROW";
                case 0x28: return "DOWN_ARROW";
                case 0x2C: return "PRINT_SCREEN";
                case 0x2D: return "INSERT";
                case 0x2E: return "DELETE";
                case 0x5B: return "LEFT_WIN";
                case 0x5C: return "RIGHT_WIN";
                case 0x70: return "F1";
                case 0x71: return "F2";
                case 0x72: return "F3";
                case 0x73: return "F4";
                case 0x74: return "F5";
                case 0x75: return "F6";
                case 0x76: return "F7";
                case 0x77: return "F8";
                case 0x78: return "F9";
                case 0x79: return "F10";
                case 0x7A: return "F11";
                case 0x7B: return "F12";
                case 0x90: return "NUM_LOCK";
                case 0x91: return "SCROLL_LOCK";
                default:
                    // אותיות ומספרים
                    if (vkCode >= 0x30 && vkCode <= 0x39) // 0-9
                        return ((char)vkCode).ToString();
                    if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
                        return ((char)vkCode).ToString();
                    if (vkCode >= 0x60 && vkCode <= 0x69) // Numpad 0-9
                        return $"NUMPAD_{vkCode - 0x60}";
                    return $"KEY_0x{vkCode:X2}";
            }
        }

        private string GetCurrentModifiers()
        {
            StringBuilder modifiers = new StringBuilder();

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers.Append(" + CTRL");
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers.Append(" + SHIFT");
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers.Append(" + ALT");

            return modifiers.ToString();
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"{message}\n");
            }
            catch
            {
                // התעלם משגיאות כתיבה
            }
        }

        public void Dispose()
        {
            StopLogging();
            WriteLog("🗑️ InputLogger disposed");
        }

        #region Win32 API Declarations

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        #endregion
    }
}

