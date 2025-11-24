using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SharpDX.Direct3D11;
using H264Sharp;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Interop;
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
        private List<byte[]> _nalCache = new List<byte[]>();

        private long _totalBytes = 0;
        private int _sentFrames = 0;

        private readonly string _logPath = "h264_sent.log";

        private const int MAX_PACKET_SIZE = 1400;
        private const int HEADER_SIZE = 5;
        private const int MAX_PAYLOAD_SIZE = MAX_PACKET_SIZE - HEADER_SIZE;

        public event Action<byte[], int, int> FrameDataReady;

        public VideoEncoder()
        {
            // נקה לוג ישן
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
            WriteLog("🚀 VideoEncoder initialized - log started");
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

                encParams.iTargetBitrate = 5000000;
                encParams.fMaxFrameRate = 15;

                encParams.iUsageType = EUsageType.SCREEN_CONTENT_REAL_TIME;
                encParams.iRCMode = RC_MODES.RC_BITRATE_MODE;
                encParams.iComplexityMode = ECOMPLEXITY_MODE.LOW_COMPLEXITY;

                encParams.iTemporalLayerNum = 1;
                encParams.iSpatialLayerNum = 1;

                encParams.sSpatialLayers[0].iVideoWidth = w;
                encParams.sSpatialLayers[0].iVideoHeight = h;
                encParams.sSpatialLayers[0].fFrameRate = 15;
                encParams.sSpatialLayers[0].iSpatialBitrate = 5000000;

                encParams.bEnableFrameSkip = false;
                encParams.uiIntraPeriod = 30; // Keyframe every 2 seconds

                _videoEnc.Initialize(encParams);

                int rawSize = w * h * 4;
                _scratchFrame = new byte[rawSize];

                WriteLog($"✅ Encoder ready: {w}x{h}@15fps");

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder ready {w}x{h}@15fps");
                });
            }
            catch (Exception initErr)
            {
                WriteLog($"❌ Encoder init error: {initErr.Message}");
                throw;
            }
        }

        public void EncodeAndSendFrame(Texture2D tex, SharpDX.Direct3D11.Texture2DDescription desc, Device mainD3D, UdpClient udpClient, IPEndPoint remoteTarget, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            if (_videoEnc == null) return;

            try
            {
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

                    FrameDataReady?.Invoke(_scratchFrame, d.Width, d.Height);

                    using (var rgb = new RgbImage(H264Sharp.ImageFormat.Bgra, d.Width, d.Height, _scratchFrame))
                    {
                        if (_videoEnc.Encode(rgb, out var outFrames))
                        {
                            byte[] encodedBytes = outFrames.GetAllBytes();

                            if (encodedBytes != null && encodedBytes.Length > 0)
                            {
                                // חלץ SPS/PPS בפעם הראשונה
                                if (_sentFrames == 0)
                                {
                                    ExtractSPSPPS(encodedBytes);
                                }

                                // הוסף SPS/PPS לפריים הראשון וכל 30 פריימים (keyframes)
                                byte[] dataToSend = encodedBytes;
                                if (_nalCache.Count > 0 && (_sentFrames % 30 == 0 || _sentFrames == 0))
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        // כתוב SPS/PPS קודם
                                        foreach (var nal in _nalCache)
                                        {
                                            ms.Write(nal, 0, nal.Length);
                                        }
                                        // כתוב את הפריים
                                        ms.Write(encodedBytes, 0, encodedBytes.Length);
                                        dataToSend = ms.ToArray();
                                    }

                                    if (_sentFrames == 0)
                                    {
                                        WriteLog($"🎬 First frame with SPS/PPS headers: {dataToSend.Length} bytes");
                                        dispatcher?.Invoke(() =>
                                        {
                                            statusCallback?.Invoke($"First frame with headers: {dataToSend.Length} bytes");
                                        });
                                    }
                                }

                                // שלח בחלקים + לוג
                                SendFrameInChunks(dataToSend, udpClient, remoteTarget);

                                _sentFrames++;
                                _totalBytes += dataToSend.Length;

                                if (_sentFrames % 15 == 0 || _sentFrames <= 3)
                                {
                                    dispatcher?.Invoke(() =>
                                    {
                                        statusCallback?.Invoke($"Frame #{_sentFrames} | {dataToSend.Length} bytes | Total {_totalBytes / 1024.0 / 1024.0:F2}MB");
                                    });
                                }
                            }
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
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder issue: {encErr.Message}");
                });
            }
        }

        private void SendFrameInChunks(byte[] frameData, UdpClient udpClient, IPEndPoint remoteTarget)
        {
            if (frameData == null || frameData.Length == 0)
            {
                WriteLog("❌ SendFrameInChunks: frameData is null or empty!");
                return;
            }

            int totalChunks = (int)Math.Ceiling((double)frameData.Length / MAX_PAYLOAD_SIZE);

            WriteLog($"📤 Frame #{_sentFrames + 1}: {frameData.Length} bytes → {totalChunks} chunks to {remoteTarget}");

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                int offset = chunkIndex * MAX_PAYLOAD_SIZE;
                int payloadSize = Math.Min(MAX_PAYLOAD_SIZE, frameData.Length - offset);
                bool isLastChunk = (chunkIndex == totalChunks - 1);

                // צור חבילה: [payloadSize(4)][isLastChunk(1)][payload]
                byte[] packet = new byte[HEADER_SIZE + payloadSize];

                BitConverter.GetBytes(payloadSize).CopyTo(packet, 0);
                packet[4] = (byte)(isLastChunk ? 1 : 0);

                System.Buffer.BlockCopy(frameData, offset, packet, HEADER_SIZE, payloadSize);

                try
                {
                    int bytesSent = udpClient.Send(packet, packet.Length, remoteTarget);

                    // לוג רק לחלק הראשון והאחרון
                    if (chunkIndex == 0 || chunkIndex == totalChunks - 1)
                    {
                        WriteLog($"  ✅ Chunk {chunkIndex + 1}/{totalChunks}: sent {bytesSent} bytes (payload: {payloadSize}, isLast: {isLastChunk})");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"  ❌ FAILED chunk {chunkIndex + 1}: {ex.Message}");
                }
            }

            WriteLog($"✅ Frame #{_sentFrames + 1} sent: {totalChunks} chunks");
            WriteLog("─────────────────────────────────────────");
        }

        private void ExtractSPSPPS(byte[] data)
        {
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    int nalType = data[i + 4] & 0x1F;

                    if (nalType == 7 || nalType == 8) // SPS או PPS
                    {
                        int nalEnd = i + 4;
                        for (int j = i + 4; j < data.Length - 3; j++)
                        {
                            if (data[j] == 0x00 && data[j + 1] == 0x00 && data[j + 2] == 0x00 && data[j + 3] == 0x01)
                            {
                                nalEnd = j;
                                break;
                            }
                        }
                        if (nalEnd == i + 4) nalEnd = data.Length;

                        byte[] nal = new byte[nalEnd - i];
                        System.Buffer.BlockCopy(data, i, nal, 0, nal.Length);
                        _nalCache.Add(nal);

                        WriteLog($"📋 Extracted NAL type {nalType} ({(nalType == 7 ? "SPS" : "PPS")}): {nal.Length} bytes");
                    }
                }
            }
        }

        private void WriteLog(string message)
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

        public void DisposeEncoder()
        {
            WriteLog("🛑 Encoder disposed");
            _videoEnc?.Dispose();
            _videoEnc = null;
        }

        public void Dispose()
        {
            DisposeEncoder();
        }
    }

    internal class FrameCapturer : IDisposable
    {
        public Device MainDevice => _mainD3D;
        public UdpClient UdpClient => _udpClient;
        public IPEndPoint RemoteTarget => _remoteTarget;
        public SizeInt32? LastSize => _lastSize;

        public event Action<Texture2D, SharpDX.Direct3D11.Texture2DDescription> FrameReady;

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
            _remoteTarget = new IPEndPoint(IPAddress.Loopback, 12345);
        }

        public void InitD3D()
        {
            _mainD3D = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _winRTDevice = Direct3D11Helper.CreateDevice(_mainD3D);
        }

        public async System.Threading.Tasks.Task StartCaptureAsync(Window win)
        {
            var picker = new GraphicsCapturePicker();
            var hwnd = new WindowInteropHelper(win).Handle;

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
            catch (Exception frameErr)
            {
                // התעלם משגיאות בלכידת פריימים
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