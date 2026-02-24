/*
═══════════════════════════════════════════════════════════════════════
קובץ: InputEvent.cs
תיאור: מבנה נתונים לאירועי קלט עם תמיכה במספר משתמשים
מיקום: sender/ (בפרויקט SENDER) וגם Receiver/ (בפרויקט RECEIVER)

⚠️ חשוב: הקובץ הזה צריך להיות **זהה** בשני הצדדים!

שימוש:
1. העתק את הקוד הזה
2. החלף את InputEvent.cs בשני הפרויקטים (SENDER ו-RECEIVER)
3. ודא שה-namespace תואם לפרויקט (sender או Receiver)
═══════════════════════════════════════════════════════════════════════
*/

using System;
using System.Text;

// שנה את ה-namespace בהתאם לפרויקט:
// בSENDER: namespace sender
// בRECEIVER: namespace Receiver
namespace sender // ⚠️ שנה ל-"Receiver" בפרויקט RECEIVER!
{
    /// <summary>
    /// סוגי אירועי קלט
    /// </summary>
    public enum InputEventType
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

    /// <summary>
    /// מבנה נתונים לאירוע קלט בודד
    /// תומך במספר משתמשים עם זיהוי SenderId + SequenceNumber
    /// </summary>
    public class InputEvent
    {
        public InputEventType Type { get; set; }

        /// <summary>
        /// Data1:
        /// - KeyDown/KeyUp: VK Code
        /// - MouseMove: Delta X
        /// - MouseWheel: Delta (גלילה)
        /// - Mouse clicks: X coordinate
        /// </summary>
        public int Data1 { get; set; }

        /// <summary>
        /// Data2:
        /// - KeyDown/KeyUp: Modifiers
        /// - MouseMove: Delta Y
        /// - Mouse clicks: Y coordinate
        /// </summary>
        public int Data2 { get; set; }

        /// <summary>
        /// זמן יצירת האירוע (Unix milliseconds)
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// מזהה המשתמש ששלח את האירוע (למשל: שם מחשב או username)
        /// </summary>
        public string SenderId { get; set; }

        /// <summary>
        /// מספר רץ לזיהוי סדר אירועים מאותו משתמש
        /// </summary>
        public long SequenceNumber { get; set; }

        /// <summary>
        /// ממיר את האירוע למערך בתים לשליחה
        /// פורמט: [Type(1)][Data1(4)][Data2(4)][Timestamp(8)][Seq(8)][SenderIdLen(1)][SenderId(var)]
        /// סה"כ: 26 + אורך SenderId bytes
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] senderBytes = Encoding.UTF8.GetBytes(SenderId ?? "");

            if (senderBytes.Length > 255)
            {
                throw new ArgumentException("SenderId too long (max 255 bytes)");
            }

            byte[] buffer = new byte[26 + senderBytes.Length];

            buffer[0] = (byte)Type;
            BitConverter.GetBytes(Data1).CopyTo(buffer, 1);
            BitConverter.GetBytes(Data2).CopyTo(buffer, 5);
            BitConverter.GetBytes(Timestamp).CopyTo(buffer, 9);
            BitConverter.GetBytes(SequenceNumber).CopyTo(buffer, 17);
            buffer[25] = (byte)senderBytes.Length;
            senderBytes.CopyTo(buffer, 26);

            return buffer;
        }

        /// <summary>
        /// יוצר InputEvent ממערך בתים שהתקבל
        /// </summary>
        public static InputEvent FromBytes(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 26)
                throw new ArgumentException($"Invalid buffer size: {buffer?.Length ?? 0} (minimum 26)");

            int senderLen = buffer[25];

            if (buffer.Length < 26 + senderLen)
                throw new ArgumentException($"Buffer too short for sender ID: {buffer.Length} < {26 + senderLen}");

            return new InputEvent
            {
                Type = (InputEventType)buffer[0],
                Data1 = BitConverter.ToInt32(buffer, 1),
                Data2 = BitConverter.ToInt32(buffer, 5),
                Timestamp = BitConverter.ToInt64(buffer, 9),
                SequenceNumber = BitConverter.ToInt64(buffer, 17),
                SenderId = Encoding.UTF8.GetString(buffer, 26, senderLen)
            };
        }

        public override string ToString()
        {
            string baseInfo = $"[{SenderId}#{SequenceNumber}] ";

            switch (Type)
            {
                case InputEventType.KeyDown:
                case InputEventType.KeyUp:
                    return $"{baseInfo}{Type}: VK=0x{Data1:X2}, Modifiers=0x{Data2:X2}";

                case InputEventType.MouseMove:
                    return $"{baseInfo}{Type}: ΔX={Data1:+#;-#;0}, ΔY={Data2:+#;-#;0}";

                case InputEventType.MouseLeftDown:
                case InputEventType.MouseLeftUp:
                case InputEventType.MouseRightDown:
                case InputEventType.MouseRightUp:
                case InputEventType.MouseMiddleDown:
                case InputEventType.MouseMiddleUp:
                    return $"{baseInfo}{Type}: X={Data1}, Y={Data2}";

                case InputEventType.MouseWheel:
                    return $"{baseInfo}{Type}: Delta={Data1}";

                default:
                    return $"{baseInfo}{Type}: {Data1}, {Data2}";
            }
        }
    }
}

/*
═══════════════════════════════════════════════════════════════════════
שימוש:
═══════════════════════════════════════════════════════════════════════

// יצירת אירוע
var evt = new InputEvent
{
    Type = InputEventType.MouseMove,
    Data1 = deltaX,  // ⚠️ חשוב: DELTA ולא קואורדינטות מוחלטות!
    Data2 = deltaY,
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    SenderId = "User123",
    SequenceNumber = 42
};

// המרה לbytes
byte[] data = evt.ToBytes();

// שליחה...
await udpClient.SendAsync(data, data.Length, endpoint);

// קבלה...
var result = await udpClient.ReceiveAsync();

// המרה חזרה
var receivedEvt = InputEvent.FromBytes(result.Buffer);

═══════════════════════════════════════════════════════════════════════
*/

