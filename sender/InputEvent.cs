using System;
using System.Text;

namespace sender
{
    /// <summary>
    /// סוגי אירועי קלט שניתן לשלוח
    /// </summary>
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

    /// <summary>
    /// מבנה נתונים לאירוע קלט בודד
    /// </summary>
    [Serializable]
    public class InputEvent
    {
        public InputEventType Type { get; set; }
        public int Data1 { get; set; }  // VK Code for keyboard, X for mouse
        public int Data2 { get; set; }  // Modifiers for keyboard, Y for mouse
        public long Timestamp { get; set; }

        /// <summary>
        /// ממיר את האירוע למערך בתים לשליחה
        /// פורמט: [Type(1)] [Data1(4)] [Data2(4)] [Timestamp(8)] = 17 bytes
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] buffer = new byte[17];

            buffer[0] = (byte)Type;
            BitConverter.GetBytes(Data1).CopyTo(buffer, 1);
            BitConverter.GetBytes(Data2).CopyTo(buffer, 5);
            BitConverter.GetBytes(Timestamp).CopyTo(buffer, 9);

            return buffer;
        }

        /// <summary>
        /// יוצר אירוע ממערך בתים שהתקבל
        /// </summary>
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
                    return $"{Type}: Delta={Data1}, X={Data2}";
                default:
                    return $"{Type}: {Data1}, {Data2}";
            }
        }
    }
}