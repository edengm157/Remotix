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
    //internal class VideoEncoder
    //{
    //    private FrameCapturer cupturer;
    //    private static Byte[] encodedFrame; // need to checl id this variable will be already with something in it or not while the complieration. if not add '?' after the []
    //    private int bitrate;
    //    private int framerate;
    //    private string resolution;
    //    private bool isInitialized;
    //    // constructor
    //    public VideoEncoder(FrameCapturer cupturer, int bitrate, int framerate, string resolution)
    //    {
    //        this.cupturer = cupturer;
    //        this.bitrate = bitrate;
    //        this.framerate = framerate;
    //        this.resolution = resolution;
    //        this.isInitialized = false;
    //    }
    //    public FrameCapturer Cupturer { get { return cupturer; } set { cupturer = value; } }             // getters and setters
    //    public Byte[] EncodedFrame { get { return encodedFrame; } set { encodedFrame = value; } }       // getters and setters
    //    public int Bitrate { get { return bitrate; } set { bitrate = value; } }                        // getters and setters 
    //    public int Framerate { get { return framerate; } set { framerate = value; } }                 // getters and setters
    //    public string Resolution { get { return resolution; } set { resolution = value; } }          // getters and setters
    //    public bool UsInitialized { get { return isInitialized; } set { isInitialized = value; } }  // getters and setters

    //    public override string ToString() { return "VideoEncoder: " + cupturer.ToString() + "\n" + bitrate + "kbps\n" + framerate + "fps\n" + resolution + "Initialized: " + isInitialized; } // toString method
    //}
    internal class VideoEncoder : IDisposable
    {
        private H264Encoder _videoEnc;
        private byte[] _scratchFrame;

        private bool _isRecording = false;
        private TimeSpan _frameTimeStamp = TimeSpan.Zero;
        private TimeSpan _frameInterval = TimeSpan.FromSeconds(1.0 / 15.0);

        private long _totalBytes = 0;
        private int _sentFrames = 0;

        // Initialize encoder with width/height (moved verbatim from original InitializeEncoder)
        // dispatcher + statusCallback allow updating StatusText in MainWindow via callback
        public void InitializeEncoder(int w, int h, Dispatcher dispatcher = null, Action<string> statusCallback = null)
        {
            try
            {
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
                encParams.uiIntraPeriod = 90;

                _videoEnc.Initialize(encParams);

                int rawSize = w * h * 4;
                _scratchFrame = new byte[rawSize];

                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder ready {w}x{h}@15fps");
                });
            }
            catch (Exception initErr)
            {
                // keep behavior similar to original
                throw;
            }
        }

        // EncodeAndSendFrame method takes the SharpDX Texture2D (copied logic from original)
        // Parameters:
        // - tex: the SharpDX texture to encode
        // - desc: texture description (width/height etc)
        // - mainD3D: device used for CopyResource / MapSubresource
        // - udpClient, remoteTarget: for send
        // - dispatcher, statusCallback: to update UI
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

                    using (var rgb = new RgbImage(H264Sharp.ImageFormat.Bgra, d.Width, d.Height, _scratchFrame))
                    {
                        if (_videoEnc.Encode(rgb, out var outFrames))
                        {
                            byte[] encodedBytes = outFrames.GetAllBytes();

                            if (encodedBytes != null && encodedBytes.Length > 0)
                            {
                                // UDP send
                                _ = udpClient.SendAsync(encodedBytes, encodedBytes.Length, remoteTarget);

                                _sentFrames++;
                                _totalBytes += encodedBytes.Length;

                                double comp = (encodedBytes.Length * 100.0) / (d.Width * d.Height * 4);

                                if (_sentFrames % 15 == 0)
                                {
                                    dispatcher?.Invoke(() =>
                                    {
                                        statusCallback?.Invoke($"Frame {_sentFrames} | {d.Width}x{d.Height} | {encodedBytes.Length / 1024.0:F1}KB | Total {_totalBytes / 1024.0 / 1024.0:F2}MB | Comp {comp:F1}%");
                                    });
                                }
                            }
                        }
                    }

                    _frameTimeStamp += _frameInterval;
                }
                finally
                {
                    mainD3D.ImmediateContext.UnmapSubresource(stage, 0);
                }
            }
            catch (Exception encErr)
            {
                dispatcher?.Invoke(() =>
                {
                    statusCallback?.Invoke($"Encoder issue: {encErr.Message}");
                });
            }
        }

        public void DisposeEncoder()
        {
            _videoEnc?.Dispose();
            _videoEnc = null;
        }

        public void Dispose()
        {
            DisposeEncoder();
        }
    }

    //internal class FrameCapturer
    //{
    //    protected string sourceWindow;
    //    protected Frame currentFrame;
    //    // constructor
    //    public FrameCapturer(Frame currentFrame, string sourceWindow)
    //    {
    //        this.currentFrame = currentFrame;
    //        this.sourceWindow = sourceWindow;
    //    }

    //    public Frame CurrentFrame { get { return currentFrame; } set { currentFrame = value; } }                          // getters and setters
    //    public string SourceWindow { get { return sourceWindow; } set { sourceWindow = value; } }                        // getters and setters

    //    public override string ToString() { return "FrameCupturer: " + sourceWindow + " " + currentFrame.ToString(); } // toString method
    //}

    //internal class Frame
    //{
    //    protected int timestamp;
    //    protected double width;
    //    protected double height;
    //    // constructor
    //    public Frame(int timestamp, double width, double height)
    //    {
    //        this.timestamp = timestamp;
    //        this.width = width;
    //        this.height = height;
    //    }

    //    public int Timestamp { get { return timestamp; } set { timestamp = value; } }                        // getters and setters
    //    public double Width { get { return width; } set { width = value; } }                                // getters and setters
    //    public double Height { get { return height; } set { height = value; } }                            // getters and setters

    //    public override string ToString() { return "Frame: " + timestamp + " " + width + "x" + height; } // toString method
    //}
    internal class FrameCapturer : IDisposable
    {
        // Publicly accessible resources needed by the encoder
        public Device MainDevice => _mainD3D;
        public UdpClient UdpClient => _udpClient;
        public IPEndPoint RemoteTarget => _remoteTarget;
        public SizeInt32? LastSize => _lastSize;

        // Event raised when a new SharpDX Texture2D is ready for encoding.
        // The handler receives the texture and its Description
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
            // Defaults — match original intent
            _udpClient = new UdpClient();
            _remoteTarget = new IPEndPoint(IPAddress.Loopback, 12345);
        }

        public void InitD3D()
        {
            // Copied InitD3D body from original MainWindow.InitD3D
            _mainD3D = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _winRTDevice = Direct3D11Helper.CreateDevice(_mainD3D);
            // UDP already created in ctor
        }

        // StartCaptureAsync encapsulates the original StartButton_Click pick-and-start flow.
        // We need the Window instance only to get the HWND for the picker initialization.
        public async System.Threading.Tasks.Task StartCaptureAsync(Window win)
        {
            var picker = new GraphicsCapturePicker();
            var hwnd = new WindowInteropHelper(win).Handle;

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // Let user pick window/monitor
            _captureItem = await picker.PickSingleItemAsync();
            if (_captureItem == null)
            {
                // Caller will handle UI message; just return
                return;
            }

            _lastSize = _captureItem.Size;

            // Create frame pool
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
            // Equivalent to original StopCapture but only disposes the capture objects
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

                // Raise event to hand the texture to the encoder
                FrameReady?.Invoke(texture, texture.Description);
            }
            catch (Exception frameErr)
            {
                // Minimal internal handling; UI will be notified by the caller if needed
                // (In original code, StatusText updated via Dispatcher; here we don't have UI reference)
            }
        }

        // Dispose helpers
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

    // Helper bridging WinRT D3D with SharpDX (moved verbatim from original)
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