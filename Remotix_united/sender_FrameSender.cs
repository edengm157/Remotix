using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace sender
{
    /// <summary>
    /// שולח frames מקודדים למספר receivers באמצעות Registration + Heartbeats
    /// Receivers נרשמים דינמית על ידי שליחת heartbeats
    /// </summary>
    public class FrameSender : IDisposable
    {
        private UdpClient _udpClient;
        private UdpClient _heartbeatListener;

        // רשימת receivers פעילים (דינמית)
        private Dictionary<string, ReceiverInfo> _activeReceivers = new Dictionary<string, ReceiverInfo>();
        private readonly object _receiversLock = new object();

        // Tasks לניהול heartbeats
        private Task _heartbeatTask;
        private Task _cleanupTask;
        private CancellationTokenSource _cancellationTokenSource;

        // הגדרות
        private const int FRAME_PORT = 12345;
        private const int HEARTBEAT_PORT = 12347;
        private const int HEARTBEAT_TIMEOUT_MS = 10000; // 10 שניות

        // סטטיסטיקות
        private long _totalFramesSent = 0;
        private long _totalPacketsSent = 0;
        private readonly string _logPath = "sender_framesender.log";

        public FrameSender()
        {
            // מחק log ישן
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            // UDP client לשליחת frames (ללא binding!)
            _udpClient = new UdpClient();

            // UDP client להאזנה ל-heartbeats
            _heartbeatListener = new UdpClient(HEARTBEAT_PORT);

            _cancellationTokenSource = new CancellationTokenSource();

            // התחל להאזין ל-heartbeats
            _heartbeatTask = Task.Run(() => ListenForHeartbeats(_cancellationTokenSource.Token));

            // נקה receivers לא פעילים
            _cleanupTask = Task.Run(() => CleanupInactiveReceivers(_cancellationTokenSource.Token));

            LogToFile("═══════════════════════════════════════════════════");
            LogToFile("🚀 FrameSender initialized with Multi-Receiver support");
            LogToFile($"📡 Listening for receiver heartbeats on port {HEARTBEAT_PORT}");
            LogToFile($"📤 Sending frames on port {FRAME_PORT}");
            LogToFile("═══════════════════════════════════════════════════");
        }

        /// <summary>
        /// האזנה ל-heartbeats מreceivers
        /// כל receiver ששולח heartbeat נוסף לרשימה אוטומטית
        /// </summary>
        private async Task ListenForHeartbeats(CancellationToken ct)
        {
            LogToFile("🔄 Heartbeat listener started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _heartbeatListener.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("HEARTBEAT"))
                    {
                        string receiverId = result.RemoteEndPoint.Address.ToString();

                        lock (_receiversLock)
                        {
                            bool isNew = !_activeReceivers.ContainsKey(receiverId);

                            _activeReceivers[receiverId] = new ReceiverInfo
                            {
                                Endpoint = new IPEndPoint(result.RemoteEndPoint.Address, FRAME_PORT),
                                LastSeen = DateTime.Now,
                                ReceiverId = receiverId
                            };

                            if (isNew)
                            {
                                LogToFile($"🆕 New receiver connected: {receiverId}");
                                LogToFile($"📊 Active receivers: {_activeReceivers.Count}");
                            }
                        }

                        // שלח ACK חזרה
                        try
                        {
                            byte[] ack = Encoding.UTF8.GetBytes("ACK");
                            await _heartbeatListener.SendAsync(ack, ack.Length, result.RemoteEndPoint);
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"❌ Failed to send ACK to {receiverId}: {ex.Message}");
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    // UDP client נסגר - צא מהלולאה
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Heartbeat listener error: {ex.Message}");
                }
            }

            LogToFile("🛑 Heartbeat listener stopped");
        }

        /// <summary>
        /// מנקה receivers שלא שלחו heartbeat זמן רב
        /// </summary>
        private async Task CleanupInactiveReceivers(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, ct); // בדוק כל 5 שניות

                    lock (_receiversLock)
                    {
                        var inactive = _activeReceivers
                            .Where(r => (DateTime.Now - r.Value.LastSeen).TotalMilliseconds > HEARTBEAT_TIMEOUT_MS)
                            .Select(r => r.Key)
                            .ToList();

                        foreach (var receiverId in inactive)
                        {
                            LogToFile($"🗑️ Receiver disconnected (timeout): {receiverId}");
                            _activeReceivers.Remove(receiverId);
                            LogToFile($"📊 Active receivers: {_activeReceivers.Count}");
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Task בוטל - צא מהלולאה
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Cleanup error: {ex.Message}");
                }
            }

            LogToFile("🛑 Cleanup task stopped");
        }

        /// <summary>
        /// שולח frame לכל הreceivers הפעילים
        /// </summary>
        public async Task SendFrame(byte[] frameData, long frameNumber, bool isKeyFrame)
        {
            // קבל רשימת receivers נוכחית
            List<ReceiverInfo> receivers;
            lock (_receiversLock)
            {
                receivers = _activeReceivers.Values.ToList();
            }

            if (receivers.Count == 0)
            {
                // אין receivers - לא שולח
                if (_totalFramesSent % 30 == 0) // לוג כל 30 frames
                {
                    LogToFile($"⚠️ No active receivers - frame #{frameNumber} not sent");
                }
                return;
            }

            // חלק את הframe לpackets (MTU = 1400 bytes)
            const int MAX_PAYLOAD_SIZE = 1400;
            const int HEADER_SIZE = 25; // frameNum(8) + seqNum(8) + packetIndex(4) + totalPackets(4) + isKeyFrame(1)

            int payloadSize = MAX_PAYLOAD_SIZE - HEADER_SIZE;
            int totalPackets = (int)Math.Ceiling((double)frameData.Length / payloadSize);

            List<byte[]> packets = new List<byte[]>();

            for (int i = 0; i < totalPackets; i++)
            {
                int offset = i * payloadSize;
                int currentPayloadSize = Math.Min(payloadSize, frameData.Length - offset);

                byte[] packet = new byte[HEADER_SIZE + currentPayloadSize];

                // Header
                BitConverter.GetBytes(frameNumber).CopyTo(packet, 0);
                BitConverter.GetBytes(_totalPacketsSent + i).CopyTo(packet, 8);
                BitConverter.GetBytes(i).CopyTo(packet, 16);
                BitConverter.GetBytes(totalPackets).CopyTo(packet, 20);
                packet[24] = (byte)(isKeyFrame ? 1 : 0);

                // Payload
                Buffer.BlockCopy(frameData, offset, packet, HEADER_SIZE, currentPayloadSize);

                packets.Add(packet);
            }

            // שלח לכל receiver בנפרד
            foreach (var receiver in receivers)
            {
                foreach (var packetData in packets)
                {
                    try
                    {
                        await _udpClient.SendAsync(
                            packetData,
                            packetData.Length,
                            receiver.Endpoint
                        );
                    }
                    catch (Exception ex)
                    {
                        LogToFile($"❌ Failed to send packet to {receiver.ReceiverId}: {ex.Message}");
                    }
                }
            }

            _totalPacketsSent += packets.Count;
            _totalFramesSent++;

            // לוג כל 30 frames
            if (frameNumber % 30 == 0)
            {
                LogToFile($"📤 Sent frame #{frameNumber} ({packets.Count} packets, {frameData.Length} bytes, {(isKeyFrame ? "I-Frame" : "P-Frame")}) to {receivers.Count} receiver(s)");
            }
        }

        /// <summary>
        /// מחזיר מספר receivers מחוברים
        /// </summary>
        public int GetActiveReceiverCount()
        {
            lock (_receiversLock)
            {
                return _activeReceivers.Count;
            }
        }

        /// <summary>
        /// מחזיר רשימת IPs של receivers מחוברים
        /// </summary>
        public List<string> GetActiveReceiverIds()
        {
            lock (_receiversLock)
            {
                return _activeReceivers.Keys.ToList();
            }
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
            LogToFile("🛑 Disposing FrameSender...");

            _cancellationTokenSource?.Cancel();

            // חכה לtasks להסתיים
            try
            {
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
                _cleanupTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            // סגור sockets
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _heartbeatListener?.Close();
            _heartbeatListener?.Dispose();
            _heartbeatListener = null;

            _cancellationTokenSource?.Dispose();

            LogToFile($"📊 Final stats: {_totalFramesSent} frames, {_totalPacketsSent} packets sent");
            LogToFile("🗑️ FrameSender disposed");
        }

        /// <summary>
        /// מידע על receiver פעיל
        /// </summary>
        private class ReceiverInfo
        {
            public IPEndPoint Endpoint { get; set; }
            public DateTime LastSeen { get; set; }
            public string ReceiverId { get; set; }
        }
    }
}

/*
═══════════════════════════════════════════════════════════════════════
שימוש:
═══════════════════════════════════════════════════════════════════════

// יצירה
var frameSender = new FrameSender();

// שליחת frame
await frameSender.SendFrame(encodedData, frameNumber, isKeyFrame);

// בדיקת מספר receivers
int count = frameSender.GetActiveReceiverCount();
Console.WriteLine($"Active receivers: {count}");

// בסוף
frameSender.Dispose();

═══════════════════════════════════════════════════════════════════════
*/

