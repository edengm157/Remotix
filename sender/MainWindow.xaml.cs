using sender;
using SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Windows;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Device = SharpDX.Direct3D11.Device;
namespace ScreenCaptureApp
{
    public partial class MainWindow : Window
    {
        //private GraphicsCaptureItem _captureItem;
        //private Direct3D11CaptureFramePool _framePool;
        //private GraphicsCaptureSession _session;
        //private SizeInt32 _lastSize;
        // Keeping names slightly uneven to feel more "human"
        //private IDirect3DDevice _winRTDevice;
        //private Device _mainD3D;
        //private bool _isRecording = false;
        //private TimeSpan _frameTimeStamp = TimeSpan.Zero;
        //// Leaving this here even though it's rarely referenced directly. I may revisit lowering FPS later.
        //private TimeSpan _frameInterval = TimeSpan.FromSeconds(1.0 / 15.0);
        //private UdpClient _udpClient;
        //private IPEndPoint _remoteTarget;
        //// Tracking counters (I should probably move this into a small class later)
        //private long _totalBytes = 0;
        //private int _sentFrames = 0;
        //private H264Encoder _videoEnc;
        //private byte[] _scratchFrame;
        private FrameCapturer _capturer;
        private VideoEncoder _encoder;
        public MainWindow()
        {
            //InitializeComponent();
            //InitD3D();
            //Closed += MainWindow_Closed;
            InitializeComponent();
            // Create the objects — InitD3D moved into capturer
            _capturer = new FrameCapturer();
            _encoder = new VideoEncoder();
            // Wire capturer -> encoder so capturer can forward frames to encoder
            _capturer.FrameReady += (tex, desc) =>
            {
                // Forward to encoder — keep async fire-and-forget like original
                _encoder.EncodeAndSendFrame(tex, desc, _capturer.MainDevice, _capturer.UdpClient, _capturer.RemoteTarget, this.Dispatcher, UpdateStatusFromEncoder);
            };
            // Initialize D3D + UDP etc (previously InitD3D)
            _capturer.InitD3D();
            Closed += MainWindow_Closed;
        }
        //private void InitD3D()
        //{
        //    // FYI: I tend to prefer hardware mode; if this fails on some machines, maybe fallback later.
        //    _mainD3D = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        //    // WinRT device creation helper
        //    _winRTDevice = Direct3D11Helper.CreateDevice(_mainD3D);
        //    // Simple UDP setup. Maybe make port configurable someday.
        //    _udpClient = new UdpClient();
        //    _remoteTarget = new IPEndPoint(IPAddress.Loopback, 12345);
        //}
        //private async void StartButton_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        var picker = new GraphicsCapturePicker();
        //        var hwnd = new WindowInteropHelper(this).Handle;
        //        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        //        // Let user pick window/monitor
        //        _captureItem = await picker.PickSingleItemAsync();
        //        if (_captureItem == null)
        //        {
        //            StatusText.Text = "User changed their mind.";
        //            return;
        //        }
        //        _lastSize = _captureItem.Size;
        //        // I originally used 1 buffer, but 2 is safer for async capture.
        //        _framePool =
        //            Direct3D11CaptureFramePool.CreateFreeThreaded(
        //                _winRTDevice,
        //                DirectXPixelFormat.B8G8R8A8UIntNormalized,
        //                2,
        //                _lastSize);
        //        _framePool.FrameArrived += FramePool_FrameArrived;
        //        _session = _framePool.CreateCaptureSession(_captureItem);
        //        // Always nice to show the cursor for debugging
        //        _session.IsCursorCaptureEnabled = true;
        //        // Init encoder based on current screen size
        //        InitializeEncoder(_lastSize.Width, _lastSize.Height);
        //        _isRecording = true;
        //        _frameTimeStamp = TimeSpan.Zero;
        //        _session.StartCapture();
        //        // UI changes
        //        StartButton.IsEnabled = false;
        //        StopButton.IsEnabled = true;
        //        StatusText.Text = "Capture started; encoder warming up...";
        //    }
        //    catch (Exception ex)
        //    {
        //        // Slightly more casual message
        //        MessageBox.Show($"Something tripped up while starting:\n{ex.Message}", "Oops", MessageBoxButton.OK, MessageBoxImage.Warning);
        //        StatusText.Text = "Error starting.";
        //    }
        //}
        //private void InitializeEncoder(int w, int h)
        //{
        //    try
        //    {
        //        _videoEnc = new H264Encoder();
        //        // Get default encoder params
        //        var encParams = _videoEnc.GetDefaultParameters();
        //        encParams.iPicWidth = w;
        //        encParams.iPicHeight = h;
        //        // Bitrate a bit high; maybe lower later when not debugging
        //        encParams.iTargetBitrate = 5000000;
        //        encParams.fMaxFrameRate = 15;
        //        encParams.iUsageType = EUsageType.SCREEN_CONTENT_REAL_TIME;
        //        encParams.iRCMode = RC_MODES.RC_BITRATE_MODE;
        //        encParams.iComplexityMode = ECOMPLEXITY_MODE.LOW_COMPLEXITY;
        //        // Single-layer usage; keep things simple for now
        //        encParams.iTemporalLayerNum = 1;
        //        encParams.iSpatialLayerNum = 1;
        //        encParams.sSpatialLayers[0].iVideoWidth = w;
        //        encParams.sSpatialLayers[0].iVideoHeight = h;
        //        encParams.sSpatialLayers[0].fFrameRate = 15;
        //        encParams.sSpatialLayers[0].iSpatialBitrate = 5000000;
        //        // I-Frame every 6 seconds (just kept original)
        //        encParams.bEnableFrameSkip = false;
        //        encParams.uiIntraPeriod = 90;
        //        _videoEnc.Initialize(encParams);
        //        // I always forget if BGRA is width*height*4, so leaving this explicit.
        //        int rawSize = w * h * 4;
        //        _scratchFrame = new byte[rawSize];
        //        Dispatcher.Invoke(() =>
        //        {
        //            StatusText.Text = $"Encoder ready {w}x{h}@15fps";
        //        });
        //    }
        //    catch (Exception initErr)
        //    {
        //        // Kept a detailed box because encoder issues usually need debugging.
        //        MessageBox.Show($"Encoder init crashed:\n{initErr}", "Encoder Error");
        //        throw;
        //    }
        //}
        //private void StopButton_Click(object sender, RoutedEventArgs e)
        //{
        //    StopCapture();
        //}
        //private void StopCapture()
        //{
        //    _isRecording = false;
        //    // Really should wrap these in try/catch individually, but leaving this for now.
        //    _session?.Dispose();
        //    _session = null;
        //    _framePool?.Dispose();
        //    _framePool = null;
        //    _videoEnc?.Dispose();
        //    _videoEnc = null;
        //    Dispatcher.Invoke(() =>
        //    {
        //        StatusText.Text = $"Stopped after {_sentFrames} frames; {_totalBytes / 1024 / 1024:F2} MB total";
        //    });
        //    StartButton.IsEnabled = true;
        //    StopButton.IsEnabled = false;
        //}
        //private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
        //{
        //    try
        //    {
        //        using var frame = sender.TryGetNextFrame();
        //        if (frame == null)
        //        {
        //            // This sometimes happens if frames arrive too fast
        //            return;
        //        }
        //        var sz = frame.ContentSize;
        //        if (sz.Width != _lastSize.Width || sz.Height != _lastSize.Height)
        //        {
        //            // Window got resized; need to rebuild framePool
        //            _lastSize = sz;
        //            // Just recycles the pool; safe enough
        //            _framePool.Recreate(
        //                _winRTDevice,
        //                DirectXPixelFormat.B8G8R8A8UIntNormalized,
        //                2,
        //                sz);
        //            // Might need to re-init encoder... but leaving that for later.
        //            return;
        //        }
        //        // Convert WinRT surface to SharpDX texture (nice helper)
        //        using var texture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
        //        EncodeAndSendFrame(texture);
        //    }
        //    catch (Exception frameErr)
        //    {
        //        Dispatcher.Invoke(() => { StatusText.Text = $"Frame err: {frameErr.Message}"; });
        //    }
        //}
        //private void EncodeAndSendFrame(Texture2D tex)
        //{
        //    if (!_isRecording || _videoEnc == null) return;
        //    try
        //    {
        //        // Staging description—yeah, a bit verbose, but I like expanding it
        //        var d = tex.Description;
        //        d.CpuAccessFlags = CpuAccessFlags.Read;
        //        d.Usage = ResourceUsage.Staging;
        //        d.OptionFlags = ResourceOptionFlags.None;
        //        d.BindFlags = BindFlags.None;
        //        // Move texture into CPU-readable staging resource
        //        using var stage = new Texture2D(_mainD3D, d);
        //        _mainD3D.ImmediateContext.CopyResource(tex, stage);
        //        var mapped = _mainD3D.ImmediateContext.MapSubresource(stage, 0, MapMode.Read, MapFlags.None);
        //        try
        //        {
        //            unsafe
        //            {
        //                var p = (byte*)mapped.DataPointer;
        //                int strideBytes = d.Width * 4;
        //                // Manual row copy loop; could be Buffer.MemoryCopy, but meh.
        //                for (int y = 0; y < d.Height; y++)
        //                {
        //                    Marshal.Copy((IntPtr)(p + y * mapped.RowPitch),
        //                                 _scratchFrame,
        //                                 y * strideBytes,
        //                                 strideBytes);
        //                }
        //            }
        //            // Wrap captured BGRA frame in H264Sharp RgbImage
        //            using (var rgb = new RgbImage(H264Sharp.ImageFormat.Bgra, d.Width, d.Height, _scratchFrame))
        //            {
        //                if (_videoEnc.Encode(rgb, out var outFrames))
        //                {
        //                    byte[] encodedBytes = outFrames.GetAllBytes();
        //                    if (encodedBytes != null && encodedBytes.Length > 0)
        //                    {
        //                        // UDP fire-and-forget
        //                        _ = _udpClient.SendAsync(encodedBytes, encodedBytes.Length, _remoteTarget);
        //                        _sentFrames++;
        //                        _totalBytes += encodedBytes.Length;
        //                        // Quick compression metric
        //                        double comp = (encodedBytes.Length * 100.0) / (d.Width * d.Height * 4);
        //                        // Updating once every second-ish
        //                        if (_sentFrames % 15 == 0)
        //                        {
        //                            Dispatcher.Invoke(() =>
        //                            {
        //                                StatusText.Text =
        //                                    $"Frame {_sentFrames} | {d.Width}x{d.Height} | {encodedBytes.Length / 1024.0:F1}KB | Total {_totalBytes / 1024.0 / 1024.0:F2}MB | Comp {comp:F1}%";
        //                            });
        //                        }
        //                    }
        //                }
        //            }
        //            _frameTimeStamp += _frameInterval;
        //        }
        //        finally
        //        {
        //            _mainD3D.ImmediateContext.UnmapSubresource(stage, 0);
        //        }
        //    }
        //    catch (Exception encErr)
        //    {
        //        Dispatcher.Invoke(() =>
        //        {
        //            StatusText.Text = $"Encoder issue: {encErr.Message}";
        //        });
        //    }
        //}
        //private void MainWindow_Closed(object sender, EventArgs e)
        //{
        //    StopCapture();
        //    // Clean up unmanaged resources
        //    _udpClient?.Close();
        //    _winRTDevice?.Dispose();
        //    _mainD3D?.Dispose();
        //}
        // Helper callback to allow encoder to update the StatusText safely
        private void UpdateStatusFromEncoder(string status)
        {
            Dispatcher.Invoke(() => { StatusText.Text = status; });
        }
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Delegate the picker and start logic to FrameCapturer.
                // We pass 'this' so FrameCapturer can initialize the picker with the window handle
                await _capturer.StartCaptureAsync(this);
                // Initialize encoder based on capture size (moved logic)
                var sz = _capturer.LastSize;
                if (sz.HasValue)
                {
                    _encoder.InitializeEncoder(sz.Value.Width, sz.Value.Height, this.Dispatcher, UpdateStatusFromEncoder);
                }
                // Update UI (same as original)
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = "Capture started; encoder warming up...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Something tripped up while starting:\n{ex.Message}", "Oops", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "Error starting.";
            }
        }
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop capture + encoder
            _capturer.StopCapture();
            _encoder.DisposeEncoder();
            // Restore UI
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // Stop everything and dispose
            _capturer.StopCapture();
            _capturer.DisposeAll();
            _encoder.DisposeEncoder();
        }
    }
    // Helper bridging WinRT D3D with SharpDX
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