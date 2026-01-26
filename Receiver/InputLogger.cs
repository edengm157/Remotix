using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Receiver
{
    /// <summary>
    /// מערכת לוגינג של כל פעולות המקלדת והעכבר במקבל ושליחתן לצד השולח
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

        private readonly string _logPath = "receiver_input_capture.log";
        private bool _isLogging = false;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private DateTime _lastMouseMoveLog = DateTime.MinValue;
        private const int MOUSE_MOVE_LOG_INTERVAL_MS = 500;

        private InputSender _inputSender;

        public InputLogger()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🎮 Receiver InputLogger initialized - will send commands to remote sender");
            _inputSender = new InputSender();
        }

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

                _keyboardProc = KeyboardHookCallback;
                _keyboardHookID = SetHook(_keyboardProc, WH_KEYBOARD_LL);

                _mouseProc = MouseHookCallback;
                _mouseHookID = SetHook(_mouseProc, WH_MOUSE_LL);

                _isLogging = true;

                _inputSender.Start();
                WriteLog("");
                WriteLog("▶️ INPUT LOGGING & SENDING TO REMOTE STARTED");
                WriteLog($"⏰ Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                WriteLog("─────────────────────────────────────────────────────");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error starting input logging: {ex.Message}");
                throw;
            }
        }

        public void StopLogging()
        {
            if (!_isLogging)
            {
                return;
            }

            try
            {
                _stopwatch.Stop();

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
                _inputSender.Stop();

                WriteLog("─────────────────────────────────────────────────────");
                WriteLog($"⏱️ Total Capture Duration: {_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
                WriteLog("⏹️ INPUT LOGGING & SENDING TO REMOTE STOPPED");
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
                    int modifiers = GetCurrentModifiersAsInt();

                    WriteLog($"⌨️ [{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] KEYDOWN: {keyName}");

                    _inputSender.QueueEvent(new InputEvent
                    {
                        Type = InputEventType.KeyDown,
                        Data1 = vkCode,
                        Data2 = modifiers,
                        Timestamp = _stopwatch.ElapsedMilliseconds
                    });
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    string keyName = GetKeyName(vkCode);
                    WriteLog($"⌨️ [{_stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}] KEYUP: {keyName}");

                    _inputSender.QueueEvent(new InputEvent
                    {
                        Type = InputEventType.KeyUp,
                        Data1 = vkCode,
                        Data2 = 0,
                        Timestamp = _stopwatch.ElapsedMilliseconds
                    });
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
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseLeftDown,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_LBUTTONUP:
                        WriteLog($"🖱️ {timestamp} LEFT CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseLeftUp,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_RBUTTONDOWN:
                        WriteLog($"🖱️ {timestamp} RIGHT CLICK DOWN at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseRightDown,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_RBUTTONUP:
                        WriteLog($"🖱️ {timestamp} RIGHT CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseRightUp,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_MBUTTONDOWN:
                        WriteLog($"🖱️ {timestamp} MIDDLE CLICK DOWN at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseMiddleDown,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_MBUTTONUP:
                        WriteLog($"🖱️ {timestamp} MIDDLE CLICK UP at ({hookStruct.pt.x}, {hookStruct.pt.y})");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseMiddleUp,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_MOUSEWHEEL:
                        int delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                        WriteLog($"🖱️ {timestamp} SCROLL delta={delta}");
                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseWheel,
                            Data1 = delta,
                            Data2 = hookStruct.pt.x,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;

                    case WM_MOUSEMOVE:
                        if ((DateTime.Now - _lastMouseMoveLog).TotalMilliseconds >= MOUSE_MOVE_LOG_INTERVAL_MS)
                        {
                            WriteLog($"🖱️ {timestamp} MOUSE MOVE to ({hookStruct.pt.x}, {hookStruct.pt.y})");
                            _lastMouseMoveLog = DateTime.Now;
                        }

                        _inputSender.QueueEvent(new InputEvent
                        {
                            Type = InputEventType.MouseMove,
                            Data1 = hookStruct.pt.x,
                            Data2 = hookStruct.pt.y,
                            Timestamp = _stopwatch.ElapsedMilliseconds
                        });
                        break;
                }
            }

            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        private string GetKeyName(int vkCode)
        {
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
                case 0x2D: return "INSERT";
                case 0x2E: return "DELETE";
                default:
                    if (vkCode >= 0x30 && vkCode <= 0x39)
                        return ((char)vkCode).ToString();
                    if (vkCode >= 0x41 && vkCode <= 0x5A)
                        return ((char)vkCode).ToString();
                    if (vkCode >= 0x60 && vkCode <= 0x69)
                        return $"NUMPAD_{vkCode - 0x60}";
                    if (vkCode >= 0x70 && vkCode <= 0x7B)
                        return $"F{vkCode - 0x6F}";
                    return $"KEY_0x{vkCode:X2}";
            }
        }

        private int GetCurrentModifiersAsInt()
        {
            int mods = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                mods |= 1;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                mods |= 2;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                mods |= 4;
            return mods;
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"{message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            StopLogging();
            _inputSender?.Dispose();
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

    // ===== InputEvent classes for Receiver =====
    public enum InputEventType : byte
    {
        KeyDown = 1,
        KeyUp = 2,
        MouseMove = 3,
        MouseLeftDown = 4,
        MouseLeftUp = 5,
        MouseRightDown = 6,
        MouseRightUp = 7,
        MouseMiddleDown = 8,
        MouseMiddleUp = 9,
        MouseWheel = 10
    }

    [Serializable]
    public class InputEvent
    {
        public InputEventType Type { get; set; }
        public int Data1 { get; set; }
        public int Data2 { get; set; }
        public long Timestamp { get; set; }

        public byte[] ToBytes()
        {
            byte[] buffer = new byte[17];
            buffer[0] = (byte)Type;
            BitConverter.GetBytes(Data1).CopyTo(buffer, 1);
            BitConverter.GetBytes(Data2).CopyTo(buffer, 5);
            BitConverter.GetBytes(Timestamp).CopyTo(buffer, 9);
            return buffer;
        }

        public static InputEvent FromBytes(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 17)
                throw new ArgumentException("Invalid buffer size");

            return new InputEvent
            {
                Type = (InputEventType)buffer[0],
                Data1 = BitConverter.ToInt32(buffer, 1),
                Data2 = BitConverter.ToInt32(buffer, 5),
                Timestamp = BitConverter.ToInt64(buffer, 9)
            };
        }

        public override string ToString()
        {
            switch (Type)
            {
                case InputEventType.KeyDown:
                case InputEventType.KeyUp:
                    return $"{Type}: VK=0x{Data1:X2}, Modifiers=0x{Data2:X2}";
                case InputEventType.MouseMove:
                case InputEventType.MouseLeftDown:
                case InputEventType.MouseLeftUp:
                case InputEventType.MouseRightDown:
                case InputEventType.MouseRightUp:
                case InputEventType.MouseMiddleDown:
                case InputEventType.MouseMiddleUp:
                    return $"{Type}: X={Data1}, Y={Data2}";
                case InputEventType.MouseWheel:
                    return $"{Type}: Delta={Data1}";
                default:
                    return $"{Type}: {Data1}, {Data2}";
            }
        }
    }
}