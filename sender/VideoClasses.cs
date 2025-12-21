using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SharpDX.Direct3D11;
using H264Sharp;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Device = SharpDX.Direct3D11.Device;
using WinRT;

namespace sender
{
    internal class VideoEncoder : IDisposable
    {
        private H264Encoder _videoEnc;
        private byte[] _scratchFrame;
        private byte[] _bgrFrame;

        private long _totalBytes = 0;
        private int _sentFrames = 0;

        private readonly string _logPath = "h264_sent.log";

        private long _sequenceNumber = 0;
        private long _frameNumber = 0;
        private const int MAX_UDP_PAYLOAD = 1400;

        // I-Frame request handling
        private bool _forceNextIFrame = false;
        private DateTime _lastIFrameRequestTime = DateTime.MinValue;
        private int _iframeRequestsReceived = 0;

        // Performance monitoring and adaptation
        private PerformanceMonitor _performanceMonitor;
        private AdaptiveQualityController _qualityController;
        private bool _adaptiveQualityEnabled = true;
        private System.Diagnostics.Stopwatch _frameTimer = new System.Diagnostics.Stopwatch();

        public event Action<byte[], int, int> FrameDataReady;
        public event Action<PerformanceMonitor> MetricsUpdated;

        // Expose monitoring
        public PerformanceMonitor PerformanceMonitor => _performanceMonitor;
        public AdaptiveQualityController QualityController => _qualityController;

        public VideoEncoder()
        {
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🚀 VideoEncoder initialized with I-Frame request handling");

            _performanceMonitor = new PerformanceMonitor();
            _qualityController = new AdaptiveQualityController(_performanceMonitor);

            // Subscribe to quality changes
            _qualityController.QualityAdjusted += OnQualityAdjusted;

            _frameTimer.Start();
        }

        public void InitializeEncoder(int w, int h, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
                WriteLog($"⚙️ Initializing encoder: {w}x{h}");

                _videoEnc = new H264Encoder();

                var encParams = _videoEnc.GetDefaultParameters();

                encParams.iPicWidth = w;
                encParams.iPicHeight = h;

                // Use adaptive quality controller's target bitrate and FPS
                encParams.iTargetBitrate = _qualityController.TargetBitrate;
                encParams.fMaxFrameRate = _qualityController.TargetFPS;

                encParams.iUsageType = EUsageType.SCREEN_CONTENT_REAL_TIME;
                encParams.iRCMode = RC_MODES.RC_BITRATE_MODE;
                encParams.iComplexityMode = ECOMPLEXITY_MODE.LOW_COMPLEXITY;

                encParams.iTemporalLayerNum = 1;
                encParams.iSpatialLayerNum = 1;

                encParams.sSpatialLayers[0].iVideoWidth = w;
                encParams.sSpatialLayers[0].iVideoHeight = h;
                encParams.sSpatialLayers[0].fFrameRate = _qualityController.TargetFPS;
                encParams.sSpatialLayers[0].iSpatialBitrate = _qualityController.TargetBitrate;

                encParams.bEnableFrameSkip = false;
                encParams.uiIntraPeriod = 30;

                _videoEnc.Initialize(encParams);

                int rawSize = w * h * 4;
                _scratchFrame = new byte[rawSize];
                _bgrFrame = new byte[w * h * 3];

                WriteLog($"✅ Encoder ready: {w}x{h}@{_qualityController.TargetFPS:F0}fps, " +
                        $"{_qualityController.TargetBitrate / 1000000.0:F1}Mbps, BGR encoding");

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder: {w}x{h} | {_qualityController.GetSettingsDescription()}");
                });
            }
            catch (Exception initErr)
            {
                WriteLog($"❌ Encoder init error: {initErr.Message}");
                throw;
            }
        }

        /// <summary>
        /// Called by control listener when I-Frame request is received
        /// </summary>
        public void RequestIFrame()
        {
            // Deduplicate requests (receiver sends 3 times)
            if ((DateTime.Now - _lastIFrameRequestTime).TotalMilliseconds < 500)
            {
                WriteLog("🔄 Duplicate I-Frame request ignored (within 500ms)");
                return;
            }

            _lastIFrameRequestTime = DateTime.Now;
            _iframeRequestsReceived++;
            _forceNextIFrame = true;

            WriteLog($"📥 I-FRAME REQUEST RECEIVED (#{_iframeRequestsReceived}) - will force IDR on next frame");
        }

        public void EncodeAndSendFrame(Texture2D tex, SharpDX.Direct3D11.Texture2DDescription desc,
            Device mainD3D, UdpClient udpClient, IPEndPoint remoteTarget,
            Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            if (_videoEnc == null) return;

            int expectedRawSize = desc.Width * desc.Height * 4;
            int expectedBgrSize = desc.Width * desc.Height * 3;

            if (_scratchFrame == null || _bgrFrame == null ||
                _scratchFrame.Length != expectedRawSize || _bgrFrame.Length != expectedBgrSize)
            {
                WriteLog($"⚠️ Size mismatch! Frame: {desc.Width}x{desc.Height}, Buffer: {_scratchFrame?.Length ?? 0} bytes. Skipping frame.");
                _performanceMonitor.RecordDroppedFrame();
                return;
            }

            long frameStartTime = _frameTimer.ElapsedMilliseconds;

            try
            {
                // Check if we need to force an I-Frame
                if (_forceNextIFrame)
                {
                    WriteLog("🎯 FORCING I-FRAME (IDR) due to request");
                    _videoEnc.ForceIntraFrame();
                    _forceNextIFrame = false;
                }

                var d = tex.Description;
                d.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read;
                d.Usage = ResourceUsage.Staging;
                d.OptionFlags = ResourceOptionFlags.None;
                d.BindFlags = BindFlags.None;

                using var stage = new Texture2D(mainD3D, d);
                mainD3D.ImmediateContext.CopyResource(tex, stage);

                var mapped = mainD3D.ImmediateContext.MapSubresource(stage, 0, SharpDX.Direct3D11.MapMode.Read, MapFlags.None);

                try
                {
                    unsafe
                    {
                        var p = (byte*)mapped.DataPointer;
                        int strideBytes = d.Width * 4;

                        for (int y = 0; y < d.Height; y++)
                        {
                            Marshal.Copy((IntPtr)(p + y * mapped.RowPitch),
                                         _scratchFrame,
                                         y * strideBytes,
                                         strideBytes);
                        }
                    }

                    ConvertBgraToBgr(_scratchFrame, _bgrFrame, d.Width, d.Height);
                    FrameDataReady?.Invoke(_scratchFrame, d.Width, d.Height);

                    using (var bgr = new RgbImage(ImageFormat.Bgr, d.Width, d.Height, _bgrFrame))
                    {
                        if (_videoEnc.Encode(bgr, out var outFrames))
                        {
                            byte[] encodedBytes = outFrames.GetAllBytes();

                            if (encodedBytes != null && encodedBytes.Length > 0)
                            {
                                long encodeTime = _frameTimer.ElapsedMilliseconds - frameStartTime;

                                AnalyzeEncodedFrame(encodedBytes, outFrames.Length);
                                SendFrameInChunks(encodedBytes, udpClient, remoteTarget);

                                _sentFrames++;
                                _totalBytes += encodedBytes.Length;

                                // Record frame metrics
                                _performanceMonitor.RecordFrame(encodedBytes.Length, encodeTime);

                                // Check if quality adjustment is needed
                                if (_adaptiveQualityEnabled && _qualityController.UpdateQuality())
                                {
                                    WriteLog($"🔧 Quality adjusted: {_qualityController.GetSettingsDescription()}");

                                    // Re-initialize encoder with new settings
                                    ReinitializeWithNewQuality(d.Width, d.Height, dispatcher, statusCallback);
                                }

                                // Update UI with metrics
                                if (_sentFrames % 15 == 0 || _sentFrames <= 3)
                                {
                                    dispatcher?.Invoke(() =>
                                    {
                                        string status = $"Frame #{_sentFrames} | {_performanceMonitor.GetQualityIndicator()} " +
                                                      $"{_performanceMonitor.GetStatusString()}";
                                        statusCallback?.Invoke(status);
                                    });

                                    MetricsUpdated?.Invoke(_performanceMonitor);
                                }
                            }
                        }
                        else
                        {
                            _performanceMonitor.RecordDroppedFrame();
                        }
                    }
                }
                finally
                {
                    mainD3D.ImmediateContext.UnmapSubresource(stage, 0);
                }
            }
            catch (Exception encErr)
            {
                WriteLog($"❌ Encode error: {encErr.Message}");
                _performanceMonitor.RecordDroppedFrame();
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder issue: {encErr.Message}");
                });
            }
        }

        private void OnQualityAdjusted(int bitrate, float fps, QualityPreset preset)
        {
            WriteLog($"📊 Quality changed: {preset} | {bitrate / 1000000.0:F1} Mbps @ {fps:F0} FPS");
        }

        private void ReinitializeWithNewQuality(int width, int height, Dispatcher dispatcher, Action<string> statusCallback)
        {
            try
            {
                // Get current parameters
                var encParams = _videoEnc.GetDefaultParameters();

                // Update with new quality settings
                encParams.iPicWidth = width;
                encParams.iPicHeight = height;
                encParams.iTargetBitrate = _qualityController.TargetBitrate;
                encParams.fMaxFrameRate = _qualityController.TargetFPS;

                encParams.iUsageType = EUsageType.SCREEN_CONTENT_REAL_TIME;
                encParams.iRCMode = RC_MODES.RC_BITRATE_MODE;
                encParams.iComplexityMode = ECOMPLEXITY_MODE.LOW_COMPLEXITY;

                encParams.iTemporalLayerNum = 1;
                encParams.iSpatialLayerNum = 1;

                encParams.sSpatialLayers[0].iVideoWidth = width;
                encParams.sSpatialLayers[0].iVideoHeight = height;
                encParams.sSpatialLayers[0].fFrameRate = _qualityController.TargetFPS;
                encParams.sSpatialLayers[0].iSpatialBitrate = _qualityController.TargetBitrate;

                encParams.bEnableFrameSkip = false;
                encParams.uiIntraPeriod = 30;

                // Re-initialize encoder
                _videoEnc.Dispose();
                _videoEnc = new H264Encoder();
                _videoEnc.Initialize(encParams);

                WriteLog($"✅ Encoder re-initialized with new quality settings");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Failed to re-initialize encoder: {ex.Message}");
            }
        }

        public void SetAdaptiveQuality(bool enabled)
        {
            _adaptiveQualityEnabled = enabled;
            WriteLog($"🔧 Adaptive quality: {(enabled ? "ENABLED" : "DISABLED")}");
        }

        private void ConvertBgraToBgr(byte[] bgra, byte[] bgr, int width, int height)
        {
            int bgraIdx = 0;
            int bgrIdx = 0;

            for (int i = 0; i < width * height; i++)
            {
                bgr[bgrIdx++] = bgra[bgraIdx++];
                bgr[bgrIdx++] = bgra[bgraIdx++];
                bgr[bgrIdx++] = bgra[bgraIdx++];
                bgraIdx++;
            }
        }

        private void AnalyzeEncodedFrame(byte[] data, int numEncodedData)
        {
            bool hasSPS = false;
            bool hasPPS = false;
            bool hasIDR = false;
            bool hasP = false;

            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 &&
                    data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    int nalType = data[i + 4] & 0x1F;

                    if (nalType == 7) hasSPS = true;
                    if (nalType == 8) hasPPS = true;
                    if (nalType == 5) hasIDR = true;
                    if (nalType == 1) hasP = true;
                }
            }

            string frameType = hasIDR ? "IDR (Keyframe)" : hasP ? "P-frame" : "Unknown";
            string nalInfo = $"SPS={hasSPS}, PPS={hasPPS}, IDR={hasIDR}, P={hasP}";

            WriteLog($"📊 Frame #{_sentFrames + 1}: {frameType} | {data.Length} bytes | EncodedData count: {numEncodedData} | NALs: {nalInfo}");
        }

        private void SendFrameInChunks(byte[] frameData, UdpClient udpClient, IPEndPoint remoteTarget)
        {
            if (frameData == null || frameData.Length == 0)
            {
                WriteLog("❌ SendFrameInChunks: frameData is null or empty!");
                return;
            }

            _frameNumber++;

            // Calculate how many packets we need for this frame
            // Reserve space for header: "seq#frame#nnn" = roughly 20 bytes max
            const int MAX_HEADER_SIZE = 30; // Conservative estimate
            int maxPayloadPerPacket = MAX_UDP_PAYLOAD - MAX_HEADER_SIZE;

            int totalPackets = (int)Math.Ceiling((double)frameData.Length / maxPayloadPerPacket);
            string totalPacketsStr = totalPackets.ToString("D3"); // 3 digits: 001, 002, etc.

            WriteLog($"📤 Sending frame #{_frameNumber}: {frameData.Length} bytes → {totalPackets} packets");

            int offset = 0;
            int packetIndex = 1;

            while (offset < frameData.Length)
            {
                _sequenceNumber++;

                // Build header: "seq#frame#nnn"
                string header = $"{_sequenceNumber}#{_frameNumber}#{totalPacketsStr}";
                byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);

                // Calculate how much payload we can fit
                int availableSpace = MAX_UDP_PAYLOAD - MAX_HEADER_SIZE;
                int payloadSize = Math.Min(availableSpace, frameData.Length - offset);

                // Build complete packet: header + payload
                byte[] packet = new byte[headerBytes.Length + payloadSize];

                // Copy header
                System.Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);

                // Copy payload
                System.Buffer.BlockCopy(frameData, offset, packet, headerBytes.Length, payloadSize);

                try
                {
                    int bytesSent = udpClient.Send(packet, packet.Length, remoteTarget);

                    if (packetIndex == 1 || packetIndex == totalPackets)
                    {
                        WriteLog($"  ✅ Packet {packetIndex}/{totalPackets}: Seq={_sequenceNumber}, " +
                                $"Header={headerBytes.Length}B, Payload={payloadSize}B, Total={bytesSent}B");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  ❌ FAILED packet {packetIndex}/{totalPackets}, Seq={_sequenceNumber}: {ex.Message}");
                }

                offset += payloadSize;
                packetIndex++;
            }

            WriteLog($"✅ Frame #{_frameNumber} sent: {totalPackets} packets (Seq {_sequenceNumber - totalPackets + 1} to {_sequenceNumber})");
            WriteLog("─────────────────────────────────────────");
        }

        private void WriteLog(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch
            {
            }
        }

        public void DisposeEncoder()
        {
            WriteLog($"🛑 Encoder disposed. Final stats: {_performanceMonitor.GetStatusString()}");
            WriteLog($"📊 Total I-Frame requests received: {_iframeRequestsReceived}");
            _videoEnc?.Dispose();
            _videoEnc = null;
        }

        public void Dispose()
        {
            DisposeEncoder();
        }
    }

    /// <summary>
    /// Listens for I-Frame requests from the receiver on video port + 1
    /// </summary>
    internal class IFrameRequestListener : IDisposable
    {
        private UdpClient _controlListener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenTask;
        private int _listenPort;
        private readonly string _logPath = "iframe_listener.log";

        public event Action IFrameRequested;

        /// <param name="videoPort">The main video streaming port (control will be videoPort + 1)</param>
        public IFrameRequestListener(int videoPort)
        {
            _listenPort = videoPort + 2; // Control port = video port + 2

            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            LogToFile($"🎧 IFrameRequestListener initialized on port {_listenPort} (video port {videoPort} + 1)");
        }

        public void StartListening()
        {
            try
            {
                _controlListener = new UdpClient(_listenPort);
                _cancellationTokenSource = new CancellationTokenSource();
                _listenTask = Task.Run(() => ListenLoop(_cancellationTokenSource.Token));

                LogToFile($"▶️ Started listening for I-Frame requests on port {_listenPort}");
            }
            catch (Exception ex)
            {
                LogToFile($"❌ Failed to start listener: {ex.Message}");
                throw;
            }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            LogToFile("🔄 Listen loop started");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _controlListener.ReceiveAsync();
                    string message = Encoding.ASCII.GetString(result.Buffer);

                    if (message == "IFRAME_REQUEST")
                    {
                        LogToFile($"📥 I-Frame request received from {result.RemoteEndPoint}");
                        IFrameRequested?.Invoke();
                    }
                    else
                    {
                        LogToFile($"⚠️ Unknown control message: '{message}' from {result.RemoteEndPoint}");
                    }
                }
                catch (ObjectDisposedException)
                {
                    LogToFile("⚠️ Listener disposed, exiting loop");
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ Listen error: {ex.Message}");
                }
            }

            LogToFile("🛑 Listen loop ended");
        }

        public void StopListening()
        {
            LogToFile("🛑 Stopping listener...");

            _cancellationTokenSource?.Cancel();
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
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
            StopListening();

            _controlListener?.Close();
            _controlListener?.Dispose();
            _controlListener = null;

            LogToFile("🗑️ IFrameRequestListener disposed");
        }
    }

    internal class FrameCapturer : IDisposable
    {
        public Device MainDevice => _mainD3D;
        public UdpClient UdpClient => _udpClient;
        public IPEndPoint RemoteTarget => _remoteTarget;
        public SizeInt32? LastSize => _lastSize;

        public event Action<Texture2D, SharpDX.Direct3D11.Texture2DDescription> FrameReady;
        public event Action<int, int> SizeChanged;

        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32? _lastSize;

        private IDirect3DDevice _winRTDevice;
        private Device _mainD3D;

        private UdpClient _udpClient;
        private IPEndPoint _remoteTarget;

        public FrameCapturer()
        {
            _udpClient = new UdpClient();
            //_remoteTarget = new IPEndPoint(IPAddress.Loopback, 12345);
            _remoteTarget = new IPEndPoint(IPAddress.Parse("10.0.0.31"), 12345);
        }

        public void InitD3D()
        {
            _mainD3D = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _winRTDevice = Direct3D11Helper.CreateDevice(_mainD3D);
        }

        public async System.Threading.Tasks.Task StartCaptureAsync(System.Windows.Window win)
        {
            var picker = new GraphicsCapturePicker();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            _captureItem = await picker.PickSingleItemAsync();
            if (_captureItem == null)
            {
                return;
            }

            _lastSize = _captureItem.Size;

            _framePool =
                Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winRTDevice,
                    Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _lastSize.Value);

            _framePool.FrameArrived += FramePool_FrameArrived;

            _session = _framePool.CreateCaptureSession(_captureItem);

            _session.IsCursorCaptureEnabled = true;

            _session.StartCapture();
        }

        public void StopCapture()
        {
            _session?.Dispose();
            _session = null;

            _framePool?.Dispose();
            _framePool = null;
        }

        private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null)
                {
                    return;
                }

                var sz = frame.ContentSize;
                if (!_lastSize.HasValue || sz.Width != _lastSize.Value.Width || sz.Height != _lastSize.Value.Height)
                {
                    _lastSize = sz;

                    SizeChanged?.Invoke(sz.Width, sz.Height);

                    _framePool.Recreate(
                        _winRTDevice,
                        Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        sz);

                    return;
                }

                using var texture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);

                FrameReady?.Invoke(texture, texture.Description);
            }
            catch (Exception)
            {
            }
        }

        public void DisposeAll()
        {
            _udpClient?.Close();
            _winRTDevice?.Dispose();
            _mainD3D?.Dispose();
        }

        public void Dispose()
        {
            DisposeAll();
        }
    }

    static class Direct3D11Helper
    {
        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        static extern int CreateDirect3D11DeviceFromDXGIDeviceNative(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        public static IDirect3DDevice CreateDevice(Device dev)
        {
            using var dxgi = dev.QueryInterface<SharpDX.DXGI.Device>();

            var hr = CreateDirect3D11DeviceFromDXGIDeviceNative(dxgi.NativePointer, out var unkPtr);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            try
            {
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(unkPtr);
            }
            finally
            {
                Marshal.Release(unkPtr);
            }
        }

        public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surf)
        {
            var wrtObj = surf as IWinRTObject;
            if (wrtObj == null)
                throw new InvalidOperationException("Surface missing IWinRTObject");

            var objRef = wrtObj.NativeObject;
            var ptr = objRef.ThisPtr;

            var accGuid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

            var hr = Marshal.QueryInterface(ptr, ref accGuid, out IntPtr accPtr);
            if (hr != 0)
                throw new COMException($"QueryInterface failed: 0x{hr:X8}", hr);

            try
            {
                var vtbl = Marshal.ReadIntPtr(accPtr);
                var funcPtr = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);

                var getter = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(funcPtr);
                var texGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

                hr = getter(accPtr, ref texGuid, out IntPtr texPtr);
                if (hr != 0)
                    throw new COMException($"GetInterface failed (0x{hr:X8})");

                if (texPtr == IntPtr.Zero)
                    throw new Exception("Texture pointer was null.");

                return new Texture2D(texPtr);
            }
            finally
            {
                Marshal.Release(accPtr);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterfaceDelegate(IntPtr thisPtr, ref Guid iid, out IntPtr ppv);
    }
}