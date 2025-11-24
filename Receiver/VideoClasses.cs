using H264Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Receiver
{
    internal class VideoDecoder : IDisposable
    {
        private H264Decoder _videoDecoder;
        private int _receivedFrames = 0;
        private long _totalBytesReceived = 0;
        private bool _hasReceivedSPS = false;
        private int _failedDecodes = 0;

        public event Action<BitmapSource> FrameDecoded;

        public void InitializeDecoder(Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _videoDecoder = new H264Decoder();
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke("Decoder initialized and ready");
                });
            }
            catch (Exception initErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Decoder init error: {initErr.Message}");
                });
                throw;
            }
        }

        public void DecodeAndDisplayFrame(byte[] encodedData, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            if (_videoDecoder == null || encodedData == null || encodedData.Length == 0)
                return;

            try
            {
                // ✅ בדוק אם יש SPS/PPS בפריים הזה
                if (!_hasReceivedSPS)
                {
                    bool hasSPS = false;
                    for (int i = 0; i < encodedData.Length - 4; i++)
                    {
                        if (encodedData[i] == 0x00 && encodedData[i + 1] == 0x00 &&
                            encodedData[i + 2] == 0x00 && encodedData[i + 3] == 0x01)
                        {
                            int nalType = encodedData[i + 4] & 0x1F;
                            if (nalType == 7) // SPS
                            {
                                hasSPS = true;
                                _hasReceivedSPS = true;
                                dispatcher?.Invoke(() =>
                                {
                                    statusCallback?.Invoke("✅ Received SPS/PPS - decoder ready!");
                                });
                                break;
                            }
                        }
                    }

                    if (!hasSPS)
                    {
                        _failedDecodes++;
                        if (_failedDecodes % 30 == 0)
                        {
                            dispatcher?.Invoke(() =>
                            {
                                statusCallback?.Invoke($"⚠️ Waiting for keyframe (SPS/PPS)... failed {_failedDecodes} times");
                            });
                        }
                        return; // לא ניתן לפענח בלי SPS/PPS
                    }
                }

                RgbImage decodedImage = null;

                // נסה לפענח את הפריים
                if (_videoDecoder.Decode(encodedData, 0, encodedData.Length, noDelay: true, out DecodingState ds, ref decodedImage))
                {
                    if (decodedImage != null)
                    {
                        _receivedFrames++;
                        _totalBytesReceived += encodedData.Length;
                        _failedDecodes = 0; // איפוס ספירת כשלונות

                        dispatcher?.Invoke(() =>
                        {
                            try
                            {
                                var bitmapSource = RgbImageToWriteableBitmap(decodedImage);
                                FrameDecoded?.Invoke(bitmapSource);

                                // עדכן סטטוס כל 15 פריימים
                                if (_receivedFrames % 15 == 0)
                                {
                                    statusCallback?.Invoke($"Decoded {_receivedFrames} frames | " +
                                        $"{decodedImage.Width}x{decodedImage.Height} | " +
                                        $"Received {_totalBytesReceived / 1024.0 / 1024.0:F2}MB");
                                }
                                else if (_receivedFrames == 1)
                                {
                                    statusCallback?.Invoke($"First frame decoded! {decodedImage.Width}x{decodedImage.Height}");
                                }
                            }
                            catch (Exception convErr)
                            {
                                statusCallback?.Invoke($"Display error: {convErr.Message}");
                            }
                        });
                    }
                }
                else
                {
                    _failedDecodes++;
                    // לוג רק את הכשלונות הראשונים
                    if (_failedDecodes <= 5)
                    {
                        dispatcher?.Invoke(() =>
                        {
                            statusCallback?.Invoke($"Decode failed: state={ds}, hasImage={decodedImage != null}, dataSize={encodedData.Length}");
                        });
                    }
                }
            }
            catch (Exception decErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Decoder error: {decErr.Message}");
                });
            }
        }

        private WriteableBitmap RgbImageToWriteableBitmap(RgbImage img)
        {
            int width = img.Width;
            int height = img.Height;
            int strideSrc = img.Stride;

            var wb = new WriteableBitmap(
                width,
                height,
                96, 96,
                PixelFormats.Bgr24,
                null);

            wb.Lock();

            int strideDst = wb.BackBufferStride;
            int bytesPerRow = width * 3;

            unsafe
            {
                byte* dst = (byte*)wb.BackBuffer;

                if (img.IsManaged && img.ManagedBytes != null)
                {
                    fixed (byte* srcPtr = img.ManagedBytes)
                    {
                        byte* src = srcPtr + img.dataOffset;

                        if (strideSrc == strideDst)
                        {
                            Buffer.MemoryCopy(src, dst, strideDst * height, strideDst * height);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + y * strideSrc,
                                    dst + y * strideDst,
                                    strideDst,
                                    bytesPerRow);
                            }
                        }
                    }
                }
                else
                {
                    byte* src = (byte*)(img.NativeBytes + img.dataOffset);

                    if (strideSrc == strideDst)
                    {
                        Buffer.MemoryCopy(src, dst, strideDst * height, strideDst * height);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                src + y * strideSrc,
                                dst + y * strideDst,
                                strideDst,
                                bytesPerRow);
                        }
                    }
                }
            }

            wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            wb.Unlock();
            wb.Freeze();

            return wb;
        }

        public void DisposeDecoder()
        {
            _videoDecoder?.Dispose();
            _videoDecoder = null;
        }

        public void Dispose()
        {
            DisposeDecoder();
        }
    }

    internal class FrameReceiver : IDisposable
    {
        public event Action<byte[]> EncodedDataReceived;

        private UdpClient _udpClient;
        private IPEndPoint _localEndPoint;
        private bool _isReceiving = false;
        private Task _receiveTask;
        private CancellationTokenSource _cancellationTokenSource;

        private Dispatcher _dispatcher;
        private Action<string> _statusCallback;

        // ✅ שינוי: שימוש ב-List<byte[]> במקום List<byte> - יעיל יותר
        private List<byte[]> _frameChunks = new List<byte[]>();
        private int _receivedChunks = 0;
        private int _totalFrames = 0;

        // קובץ לוג
        private readonly string _logPath = "receiver_log.txt";

        public bool IsReceiving => _isReceiving;

        public FrameReceiver()
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, 12345);

            // נקה קובץ לוג קודם
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
        }

        public void InitializeReceiver(int port = 12345, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                _dispatcher = dispatcher;
                _statusCallback = statusCallback;

                // ✅ תיקון: אם יש כבר UDP client, סגור אותו קודם
                if (_udpClient != null)
                {
                    try
                    {
                        _udpClient.Close();
                        _udpClient.Dispose();
                    }
                    catch { }
                    _udpClient = null;
                }

                _localEndPoint = new IPEndPoint(IPAddress.Any, port);
                _udpClient = new UdpClient();

                // אפשר שימוש חוזר בפורט
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(_localEndPoint);

                // הגדל את ה-buffer כדי למנוע אובדן חבילות
                _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024; // 2MB

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Receiver initialized on port {port} (listening on all interfaces)");
                });
            }
            catch (Exception initErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Receiver init error: {initErr.Message}");
                });
                throw;
            }
        }

        public void StartReceiving()
        {
            if (_isReceiving)
                return;

            // ✅ תיקון: אתחל מחדש אם אין UDP client
            if (_udpClient == null)
            {
                InitializeReceiver(12345, _dispatcher, _statusCallback);
            }

            _isReceiving = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(async () => await ReceiveLoop(_cancellationTokenSource.Token));

            _dispatcher?.Invoke(() =>
            {
                _statusCallback?.Invoke("Started receiving frames... Waiting for data from sender");
            });
        }

        public void StopReceiving()
        {
            if (!_isReceiving)
                return;

            _isReceiving = false;

            // בטל את ה-task
            _cancellationTokenSource?.Cancel();

            // ✅ תיקון: סגור את ה-UDP client כדי לשחרר את ה-ReceiveAsync
            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                catch { }
                _udpClient = null;
            }

            // המתן ל-task להסתיים
            try
            {
                _receiveTask?.Wait(1000);
            }
            catch { }

            // נקה את החלקים שנאספו
            _frameChunks.Clear();
            _receivedChunks = 0;

            _dispatcher?.Invoke(() =>
            {
                _statusCallback?.Invoke("Stopped receiving frames");
            });
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            LogToFile("🚀 ReceiveLoop STARTED - waiting for packets...");

            int packetsReceived = 0;

            while (_isReceiving && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    packetsReceived++;

                    if (packetsReceived == 1)
                    {
                        LogToFile($"📦 First packet received! Size: {result.Buffer?.Length ?? 0} bytes from {result.RemoteEndPoint}");

                        _dispatcher?.Invoke(() =>
                        {
                            _statusCallback?.Invoke($"First packet received from {result.RemoteEndPoint}!");
                        });
                    }

                    if (result.Buffer != null && result.Buffer.Length > 0)
                    {
                        ParseIncomingPacket(result.Buffer);
                    }
                }
                catch (SocketException) when (!_isReceiving)
                {
                    LogToFile("❌ SocketException - stopping (expected)");
                    break; // צפוי כאשר עוצרים
                }
                catch (ObjectDisposedException)
                {
                    LogToFile("❌ ObjectDisposedException - stopping (expected)");
                    break; // צפוי כאשר עוצרים
                }
                catch (Exception recErr)
                {
                    LogToFile($"❌ Receive error: {recErr.Message}");
                    _dispatcher?.Invoke(() =>
                    {
                        _statusCallback?.Invoke($"Receive error: {recErr.Message}");
                    });
                }
            }

            LogToFile("🛑 ReceiveLoop ENDED");
        }

        private void ParseIncomingPacket(byte[] packet)
        {
            if (packet.Length < 5)
            {
                LogToFile($"❌ Packet too small: {packet.Length} bytes");
                return;
            }

            // קרא את הכותרת: [payloadSize(4)][isLastChunk(1)][payload...]
            int payloadSize = BitConverter.ToInt32(packet, 0);
            bool isLastChunk = packet[4] == 1;

            // וודא שהחבילה תקינה
            if (payloadSize < 0 || packet.Length < 5 + payloadSize)
            {
                LogToFile($"❌ Corrupted packet: payloadSize={payloadSize}, packetLength={packet.Length}");
                _frameChunks.Clear();
                _receivedChunks = 0;
                return;
            }

            // חלץ את ה-payload
            byte[] payload = new byte[payloadSize];
            Buffer.BlockCopy(packet, 5, payload, 0, payloadSize);

            _frameChunks.Add(payload);
            _receivedChunks++;

            LogToFile($"✅ Chunk {_receivedChunks}: {payloadSize} bytes, isLast={isLastChunk}");

            if (!isLastChunk)
            {
                // עדיין צריך להמתין לחלקים נוספים
                return;
            }

            // ✅ זה החלק האחרון - בנה את הפריים המלא
            int totalSize = 0;
            foreach (var chunk in _frameChunks)
                totalSize += chunk.Length;

            byte[] fullFrame = new byte[totalSize];
            int offset = 0;

            foreach (var chunk in _frameChunks)
            {
                Buffer.BlockCopy(chunk, 0, fullFrame, offset, chunk.Length);
                offset += chunk.Length;
            }

            _totalFrames++;
            LogToFile($"🎬 FULL FRAME #{_totalFrames}: {_receivedChunks} chunks, {totalSize} bytes total");

            // לוג את ה-100 בתים הראשונים לבדיקה
            if (_totalFrames <= 2)
            {
                LogToFile($"Frame data (first 100 bytes): {BitConverter.ToString(fullFrame, 0, Math.Min(100, fullFrame.Length))}");
            }

            LogToFile("─────────────────────────────────────────");

            // נקה את רשימת החלקים לפריים הבא
            _frameChunks.Clear();
            _receivedChunks = 0;

            // שלח את הפריים המלא לדקודר
            EncodedDataReceived?.Invoke(fullFrame);
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
                // התעלם משגיאות בכתיבת לוג
            }
        }

        public void DisposeReceiver()
        {
            StopReceiving();
            _cancellationTokenSource?.Dispose();
        }

        public void Dispose()
        {
            DisposeReceiver();
        }
    }
}