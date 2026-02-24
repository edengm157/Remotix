/*
═══════════════════════════════════════════════════════════════════════
קובץ: FrameReceiver.cs (או חלק מ-VideoClasses.cs)
תיאור: FrameReceiver עם שליחת heartbeats ל-SENDER
מיקום: Receiver/ (בפרויקט RECEIVER)

שימוש:
1. העתק את הקוד הזה
2. החלף את FrameReceiver בפרויקט RECEIVER
3. עדכן את SENDER_IP לIP האמיתי של מחשב ה-SENDER
═══════════════════════════════════════════════════════════════════════
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Receiver
{
    /// <summary>
    /// מקבל frames מה-SENDER ושולח heartbeats כדי להישאר רשום
    /// </summary>
    public class FrameReceiver : IDisposable
    {
        private UdpClient _udpClient;
        private UdpClient _heartbeatSender;
        private UdpClient _controlClient;

        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private Task _heartbeatTask;
        private Task _jitterBufferTask;

        private bool _isRunning = false;

        // ⚠️ חשוב: עדכן את ה-IP הזה ל-IP של מחשב ה-SENDER!
        private string _senderIp = "10.0.0.22"; // ⚠️ שנה את זה!

        private const int FRAME_PORT = 12345;
        private const int HEARTBEAT_PORT = 12347;
        private const int CONTROL_PORT = 12348;
        private const int HEARTBEAT_INTERVAL_MS = 3000; // שלח heartbeat כל 3 שניות

        // Frame assembly
        private Dictionary<long, BufferedFrame> _frameBuffer = new Dictionary<long, BufferedFrame>();
        private readonly object _bufferLock = new object();

        private long _lastDeliveredFrame = -1;
        private long _lastSeenSequence = -1;

        // Jitter buffer
        private const int JITTER_BUFFER_MS = 50;
        private const int LOSS_TIMEOUT_MS = 200;

        // I-Frame requests
        private long _lastIFrameRequest = 0;
        private int _iframeRequestsSent = 0;

        // Statistics
        private long _totalPacketsReceived = 0;
        private long _lostPackets = 0;
        private long _recoveredPackets = 0;
        private long _outOfOrderPackets = 0;
        private long _duplicatePackets = 0;
        private long _totalFramesDelivered = 0;

        private readonly string _logPath = "receiver_framesReceiver.log";

        public event Action<byte[]> EncodedDataReceived;
        public event Action FrameDroppedForUI;

        public FrameReceiver()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }

            LogToFile("═══════════════════════════════════════════════════");
            LogToFile("🎬 FrameReceiver initialized with Heartbeat support");
            LogToFile($"📡 Will send heartbeats to: {_senderIp}:{HEARTBEAT_PORT}");
            LogToFile("═══════════════════════════════════════════════════");
        }

        /// <summary>
        /// עדכן את IP של ה-SENDER
        /// </summary>
        public void SetSenderIp(string senderIp)
        {
            _senderIp = senderIp;
            LogToFile($"📡 Sender IP updated to: {_senderIp}");
        }

        public void StartReceiving()
        {
            if (_isRunning)
            {
                LogToFile("⚠️ Already running");
                return;
            }

            try
            {
                // UDP client לקבלת frames
                _udpClient = new UdpClient(FRAME_PORT);
                _udpClient.Client.ReceiveBufferSize = 512 * 1024;

                // UDP client לשליחת heartbeats
                _heartbeatSender = new UdpClient();

                // UDP client לשליחת I-Frame requests
                _controlClient = new UdpClient();

                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // התחל לשלוח heartbeats
                _heartbeatTask = Task.Run(() => SendHeartbeats(_cancellationTokenSource.Token));

                // התחל לקבל frames
                _receiveTask = Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token));

                // התחל jitter buffer processing
                _jitterBufferTask = Task.Run(() => JitterBufferLoop(_cancellationTokenSource.Token));

                LogToFile($"✅ Started receiving from {_senderIp}");
                LogToFile($"💓 Sending heartbeats every {HEARTBEAT_INTERVAL_MS}ms");
            }
            catch (Exception ex)
            {
                LogToFile($"❌ Error starting: {ex.Message}");
                throw;
            }
        }

        #region Heartbeat Management

        private async Task SendHeartbeats(CancellationToken ct)
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(_senderIp), HEARTBEAT_PORT);
            byte[] heartbeat = Encoding.UTF8.GetBytes("HEARTBEAT");

            LogToFile("💓 Heartbeat sender started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _heartbeatSender.SendAsync(heartbeat, heartbeat.Length, endpoint);
                    LogToFile($"💓 Heartbeat sent to {_senderIp}:{HEARTBEAT_PORT}");

                    // חכה ל-ACK (אופציונלי - לא חובה)
                    var receiveTask = _heartbeatSender.ReceiveAsync();
                    var timeoutTask = Task.Delay(1000, ct);

                    if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
                    {
                        var result = await receiveTask;
                        string ack = Encoding.UTF8.GetString(result.Buffer);
                        if (ack == "ACK")
                        {
                            LogToFile($"✅ Heartbeat acknowledged by sender");
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Heartbeat error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(HEARTBEAT_INTERVAL_MS, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            LogToFile("🛑 Heartbeat sender stopped");
        }

        #endregion

        #region Frame Receiving

        //private async Task ReceiveLoop(CancellationToken ct)
        //{
        //    LogToFile("🔄 Receive loop started");

        //    while (!ct.IsCancellationRequested)
        //    {
        //        try
        //        {
        //            var result = await _udpClient.ReceiveAsync();
        //            byte[] data = result.Buffer;

        //            if (data.Length < 25)
        //            {
        //                LogToFile($"⚠️ Invalid packet size: {data.Length} bytes");
        //                continue;
        //            }

        //            // Parse header
        //            long frameNumber = BitConverter.ToInt64(data, 0);
        //            long sequenceNumber = BitConverter.ToInt64(data, 8);
        //            int packetIndex = BitConverter.ToInt32(data, 16);
        //            int totalPackets = BitConverter.ToInt32(data, 20);
        //            bool isKeyFrame = data[24] == 1;

        //            // Extract payload
        //            byte[] payload = new byte[data.Length - 25];
        //            Buffer.BlockCopy(data, 25, payload, 0, payload.Length);

        //            _totalPacketsReceived++;

        //            // Detect out-of-order
        //            if (sequenceNumber <= _lastSeenSequence && _lastSeenSequence != -1)
        //            {
        //                _outOfOrderPackets++;
        //            }
        //            _lastSeenSequence = Math.Max(_lastSeenSequence, sequenceNumber);

        //            // Add to buffer
        //            lock (_bufferLock)
        //            {
        //                if (!_frameBuffer.ContainsKey(frameNumber))
        //                {
        //                    _frameBuffer[frameNumber] = new BufferedFrame
        //                    {
        //                        FrameNumber = frameNumber,
        //                        Assembler = new FrameAssembler(frameNumber, totalPackets),
        //                        FirstPacketTime = DateTime.Now
        //                    };

        //                    LogToFile($"📦 New frame #{frameNumber} started ({totalPackets} packets, {(isKeyFrame ? "I-Frame" : "P-Frame")})");
        //                }

        //                var bufferedFrame = _frameBuffer[frameNumber];
        //                bool isNew = bufferedFrame.Assembler.AddPacket(sequenceNumber, payload);

        //                if (!isNew)
        //                {
        //                    _duplicatePackets++;
        //                }

        //                LogToFile($"📥 Packet #{sequenceNumber} → Frame #{frameNumber} [{bufferedFrame.Assembler.ReceivedPackets}/{bufferedFrame.Assembler.TotalPackets}]");
        //            }
        //        }
        //        catch (ObjectDisposedException)
        //        {
        //            break;
        //        }
        //        catch (Exception ex)
        //        {
        //            LogToFile($"❌ Receive error: {ex.Message}");
        //        }
        //    }

        //    LogToFile($"🛑 Receive loop ended");
        //}

        // ✅ FIXED VERSION - ReceiveLoop method for FrameReceiver.cs
        // Replace the entire ReceiveLoop method (around line 4306) with this:

        private async Task ReceiveLoop(CancellationToken ct)
        {
            LogToFile("🔄 Receive loop started");
            LogToFile("📋 Expecting TEXT header format: 'seq#frame#nnn'");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    byte[] data = result.Buffer;

                    if (data.Length < 10)
                    {
                        LogToFile($"⚠️ Packet too small: {data.Length} bytes");
                        continue;
                    }

                    // ✅ Parse TEXT header format: "seq#frame#nnn"
                    // Convert entire packet to string to find delimiters
                    string fullData = System.Text.Encoding.ASCII.GetString(data);

                    // Find the two '#' delimiters
                    int firstHash = fullData.IndexOf('#');
                    if (firstHash < 0)
                    {
                        LogToFile($"⚠️ Invalid packet: No first '#' delimiter found");
                        LogToFile($"   First 50 bytes: {System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(50, data.Length))}");
                        continue;
                    }

                    int secondHash = fullData.IndexOf('#', firstHash + 1);
                    if (secondHash < 0)
                    {
                        LogToFile($"⚠️ Invalid packet: No second '#' delimiter found");
                        LogToFile($"   First 50 bytes: {System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(50, data.Length))}");
                        continue;
                    }

                    // Parse sequence number (before first #)
                    string seqStr = fullData.Substring(0, firstHash);
                    if (!long.TryParse(seqStr, out long sequenceNumber))
                    {
                        LogToFile($"⚠️ Invalid sequence number: '{seqStr}'");
                        continue;
                    }

                    // Parse frame number (between first and second #)
                    string frameStr = fullData.Substring(firstHash + 1, secondHash - firstHash - 1);
                    if (!long.TryParse(frameStr, out long frameNumber))
                    {
                        LogToFile($"⚠️ Invalid frame number: '{frameStr}'");
                        continue;
                    }

                    // Parse total packets (3 digits after second #)
                    string totalStr = fullData.Substring(secondHash + 1, 3);
                    if (!int.TryParse(totalStr, out int totalPackets))
                    {
                        LogToFile($"⚠️ Invalid total packets: '{totalStr}'");
                        continue;
                    }

                    // Calculate header length: "seq#frame#nnn" = all text up to and including the 3 digits
                    int headerLength = secondHash + 4; // +4 for the 3 digits + null terminator or next byte

                    // Sanity check
                    if (headerLength >= data.Length)
                    {
                        LogToFile($"⚠️ Header length ({headerLength}) >= packet length ({data.Length})");
                        LogToFile($"   Header would be: seq={sequenceNumber}, frame={frameNumber}, total={totalPackets}");
                        continue;
                    }

                    // Extract payload (everything after header)
                    int payloadLength = data.Length - headerLength;
                    byte[] payload = new byte[payloadLength];
                    Buffer.BlockCopy(data, headerLength, payload, 0, payloadLength);

                    _totalPacketsReceived++;

                    // Log first packet of each frame with details
                    bool isFirstPacket = !_frameBuffer.ContainsKey(frameNumber);
                    if (isFirstPacket || _totalPacketsReceived % 10 == 0)
                    {
                        LogToFile($"📦 Packet received: Seq={sequenceNumber}, Frame={frameNumber}, " +
                                 $"Total={totalPackets}, HeaderLen={headerLength}, PayloadLen={payloadLength}");
                    }

                    // Detect out-of-order
                    if (sequenceNumber <= _lastSeenSequence && _lastSeenSequence != -1)
                    {
                        _outOfOrderPackets++;
                        LogToFile($"⚠️ Out-of-order: Seq {sequenceNumber} (last was {_lastSeenSequence})");
                    }
                    _lastSeenSequence = Math.Max(_lastSeenSequence, sequenceNumber);

                    // Add to buffer
                    lock (_bufferLock)
                    {
                        if (!_frameBuffer.ContainsKey(frameNumber))
                        {
                            _frameBuffer[frameNumber] = new BufferedFrame
                            {
                                FrameNumber = frameNumber,
                                Assembler = new FrameAssembler(frameNumber, totalPackets),
                                FirstPacketTime = DateTime.Now
                            };

                            LogToFile($"🆕 New frame #{frameNumber} started (expecting {totalPackets} packets)");
                        }

                        var bufferedFrame = _frameBuffer[frameNumber];
                        bool isNew = bufferedFrame.Assembler.AddPacket(sequenceNumber, payload);

                        if (!isNew)
                        {
                            _duplicatePackets++;
                            LogToFile($"⚠️ Duplicate packet: Seq={sequenceNumber}");
                        }
                        else
                        {
                            int received = bufferedFrame.Assembler.ReceivedPackets;
                            int total = bufferedFrame.Assembler.TotalPackets;

                            // Log progress for first, middle, and last packets
                            if (received == 1 || received == total || received % 5 == 0)
                            {
                                LogToFile($"📥 Frame #{frameNumber} progress: [{received}/{total}] packets");
                            }

                            // Check if frame is complete
                            if (bufferedFrame.Assembler.IsComplete)
                            {
                                LogToFile($"✅ Frame #{frameNumber} COMPLETE! All {total} packets received");
                            }
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    LogToFile("🛑 UDP client disposed - stopping receive loop");
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Receive error: {ex.Message}");
                    LogToFile($"   Exception type: {ex.GetType().Name}");
                    if (ex.InnerException != null)
                    {
                        LogToFile($"   Inner exception: {ex.InnerException.Message}");
                    }
                }
            }

            LogToFile($"🛑 Receive loop ended");
            LogToFile($"📊 Total packets received: {_totalPacketsReceived}");
            LogToFile($"📊 Out-of-order packets: {_outOfOrderPackets}");
            LogToFile($"📊 Duplicate packets: {_duplicatePackets}");
        }

        private async Task JitterBufferLoop(CancellationToken ct)
        {
            LogToFile($"🔄 Jitter buffer loop started ({JITTER_BUFFER_MS}ms buffer)");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(JITTER_BUFFER_MS, ct);

                    bool needIFrame = false;

                    lock (_bufferLock)
                    {
                        var readyFrames = _frameBuffer
                            .Where(f => f.Value.Assembler.IsComplete)
                            .OrderBy(f => f.Key)
                            .ToList();

                        foreach (var frameEntry in readyFrames)
                        {
                            long frameNum = frameEntry.Key;
                            var bufferedFrame = frameEntry.Value;

                            try
                            {
                                byte[] completeFrame = bufferedFrame.Assembler.GetCompleteFrame();
                                DeliverToDecoder(frameNum, completeFrame);
                                _frameBuffer.Remove(frameNum);
                            }
                            catch (Exception ex)
                            {
                                LogToFile($"❌ Error delivering frame #{frameNum}: {ex.Message}");
                            }
                        }

                        var timedOutFrames = _frameBuffer
                            .Where(f => !f.Value.Assembler.IsComplete &&
                                       (DateTime.Now - f.Value.FirstPacketTime).TotalMilliseconds > LOSS_TIMEOUT_MS)
                            .ToList();

                        foreach (var frameEntry in timedOutFrames)
                        {
                            long frameNum = frameEntry.Key;
                            var bufferedFrame = frameEntry.Value;

                            int missing = bufferedFrame.Assembler.TotalPackets - bufferedFrame.Assembler.ReceivedPackets;
                            _lostPackets += missing;

                            LogToFile($"⏱️ Frame #{frameNum} timed out ({bufferedFrame.Assembler.ReceivedPackets}/{bufferedFrame.Assembler.TotalPackets} packets, {missing} lost)");

                            needIFrame = true;
                            FrameDroppedForUI?.Invoke();
                            _frameBuffer.Remove(frameNum);
                        }

                        CleanupOldFrames();
                    }

                    if (needIFrame)
                    {
                        await RequestIFrame();
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Jitter buffer error: {ex.Message}");
                }
            }

            LogToFile("🛑 Jitter buffer loop ended");
        }

        private async Task RequestIFrame()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (now - _lastIFrameRequest < 1000)
            {
                return;
            }

            _lastIFrameRequest = now;

            var endpoint = new IPEndPoint(IPAddress.Parse(_senderIp), CONTROL_PORT);
            byte[] request = Encoding.UTF8.GetBytes("REQUEST_IFRAME");

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await _controlClient.SendAsync(request, request.Length, endpoint);
                    _iframeRequestsSent++;
                    LogToFile($"📤 I-Frame request sent (attempt {i + 1}/3)");

                    if (i < 2)
                    {
                        await Task.Delay(50);
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Failed to send I-Frame request: {ex.Message}");
                }
            }
        }

        private void DeliverToDecoder(long frameNum, byte[] frameData)
        {
            _totalFramesDelivered++;
            _lastDeliveredFrame = frameNum;

            EncodedDataReceived?.Invoke(frameData);

            LogToFile($"📤 Frame #{frameNum} delivered to decoder ({_totalFramesDelivered} total)");
        }

        private void CleanupOldFrames()
        {
            if (_lastDeliveredFrame < 0)
                return;

            var toRemove = _frameBuffer.Keys.Where(f => f < _lastDeliveredFrame - 10).ToList();

            foreach (var frameNum in toRemove)
            {
                var bufferedFrame = _frameBuffer[frameNum];
                LogToFile($"🗑️ Cleaning up old frame #{frameNum} ({bufferedFrame.Assembler.ReceivedPackets}/{bufferedFrame.Assembler.TotalPackets} packets)");
                _frameBuffer.Remove(frameNum);
            }
        }

        #endregion

        #region Statistics & Cleanup

        private void LogStatistics()
        {
            double lossPercent = _totalPacketsReceived > 0 ? (_lostPackets * 100.0 / _totalPacketsReceived) : 0;
            double recoveryPercent = (_lostPackets + _recoveredPackets) > 0 ? (_recoveredPackets * 100.0 / (_lostPackets + _recoveredPackets)) : 0;

            LogToFile("═══════════════════════════════════════");
            LogToFile("📊 FINAL STATISTICS:");
            LogToFile($"  Total Packets Received: {_totalPacketsReceived}");
            LogToFile($"  Lost Packets: {_lostPackets} ({lossPercent:F2}%)");
            LogToFile($"  Recovered Packets: {_recoveredPackets} ({recoveryPercent:F1}% recovery rate)");
            LogToFile($"  Out-of-Order Packets: {_outOfOrderPackets}");
            LogToFile($"  Duplicate Packets: {_duplicatePackets}");
            LogToFile($"  Total Frames Delivered: {_totalFramesDelivered}");
            LogToFile($"  I-Frame Requests Sent: {_iframeRequestsSent}");
            LogToFile($"  Last Frame Number: {_lastDeliveredFrame}");
            LogToFile("═══════════════════════════════════════");
        }

        public void StopReceiving()
        {
            LogToFile("🛑 Stopping receiver...");

            _cancellationTokenSource?.Cancel();
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
            _jitterBufferTask?.Wait(TimeSpan.FromSeconds(2));

            LogStatistics();
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            StopReceiving();

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _heartbeatSender?.Close();
            _heartbeatSender?.Dispose();
            _heartbeatSender = null;

            _controlClient?.Close();
            _controlClient?.Dispose();
            _controlClient = null;

            _cancellationTokenSource?.Dispose();

            LogToFile("🗑️ FrameReceiver disposed");
        }

        #endregion
    }

    internal class BufferedFrame
    {
        public long FrameNumber { get; set; }
        public FrameAssembler Assembler { get; set; }
        public DateTime FirstPacketTime { get; set; }
    }

    internal class FrameAssembler
    {
        public long FrameNumber { get; }
        public int TotalPackets { get; }

        private readonly object _lock = new object();
        private Dictionary<long, byte[]> _packets = new Dictionary<long, byte[]>();

        public FrameAssembler(long frameNumber, int totalPackets)
        {
            FrameNumber = frameNumber;
            TotalPackets = totalPackets;
        }

        public bool IsComplete
        {
            get
            {
                lock (_lock)
                {
                    return _packets.Count == TotalPackets;
                }
            }
        }

        public int ReceivedPackets
        {
            get
            {
                lock (_lock)
                {
                    return _packets.Count;
                }
            }
        }

        public bool AddPacket(long seqNum, byte[] payload)
        {
            lock (_lock)
            {
                if (_packets.ContainsKey(seqNum))
                {
                    return false; // Duplicate
                }

                _packets[seqNum] = payload;
                return true;
            }
        }

        public byte[] GetCompleteFrame()
        {
            lock (_lock)
            {
                if (_packets.Count != TotalPackets)
                {
                    throw new InvalidOperationException(
                        $"Frame {FrameNumber} is not complete: {_packets.Count}/{TotalPackets} packets");
                }

                var orderedPackets = _packets.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
                int totalSize = orderedPackets.Sum(p => p.Length);

                byte[] result = new byte[totalSize];
                int offset = 0;

                foreach (var packet in orderedPackets)
                {
                    Buffer.BlockCopy(packet, 0, result, offset, packet.Length);
                    offset += packet.Length;
                }

                return result;
            }
        }
    }
}

/*
═══════════════════════════════════════════════════════════════════════
שימוש:
═══════════════════════════════════════════════════════════════════════

// יצירה
var frameReceiver = new FrameReceiver();

// עדכן IP של SENDER (אופציונלי - אם לא עשית זאת ב-constructor)
frameReceiver.SetSenderIp("192.168.1.100"); // ה-IP האמיתי!

// התחל קבלה
frameReceiver.StartReceiving();

// האזן לframes
frameReceiver.EncodedDataReceived += (data) => {
    // העבר לdecoder...
};

// בסוף
frameReceiver.StopReceiving();
frameReceiver.Dispose();

═══════════════════════════════════════════════════════════════════════
*/

