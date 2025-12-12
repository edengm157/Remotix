using sender;
using SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Device = SharpDX.Direct3D11.Device;

namespace ScreenCaptureApp
{
    public partial class MainWindow : Window
    {
        private FrameCapturer _capturer;
        private VideoEncoder _encoder;
        private WriteableBitmap _previewBitmap;
        private InputLogger _inputLogger;

        public MainWindow()
        {
            InitializeComponent();
            _inputLogger = new InputLogger();

            // Create the objects — InitD3D moved into capturer
            _capturer = new FrameCapturer();
            _encoder = new VideoEncoder();

            // Wire capturer -> encoder so capturer can forward frames to encoder
            _capturer.FrameReady += (tex, desc) =>
            {
                // Forward to encoder — keep async fire-and-forget like original
                _encoder.EncodeAndSendFrame(tex, desc, _capturer.MainDevice, _capturer.UdpClient, _capturer.RemoteTarget, this.Dispatcher, UpdateStatusFromEncoder);
            };

            // Wire encoder -> preview update
            _encoder.FrameDataReady += (frameData, width, height) =>
            {
                UpdatePreview(frameData, width, height);
            };

            // Initialize D3D + UDP etc (previously InitD3D)
            _capturer.InitD3D();

            Closed += MainWindow_Closed;
        }

        private void InitializePreview(int w, int h)
        {
            Dispatcher.Invoke(() =>
            {
                _previewBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                PreviewImage.Source = _previewBitmap;
            });
        }

        private void UpdatePreview(byte[] frameData, int width, int height)
        {
            if (_previewBitmap == null)
            {
                InitializePreview(width, height);
            }

            Dispatcher.Invoke(() =>
            {
                try
                {
                    _previewBitmap.Lock();

                    // Copy frame data to bitmap
                    int stride = width * 4;
                    Marshal.Copy(frameData, 0, _previewBitmap.BackBuffer, frameData.Length);

                    _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
                finally
                {
                    _previewBitmap.Unlock();
                }
            });
        }

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
                    InitializePreview(sz.Value.Width, sz.Value.Height);
                }
                _capturer.SizeChanged += (width, height) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Screen size changed to {width}x{height}, reinitializing encoder...";
                    });

                    _encoder.DisposeEncoder();
                    _encoder.InitializeEncoder(width, height, this.Dispatcher, UpdateStatusFromEncoder);
                    InitializePreview(width, height);
                };

                // Update UI (same as original)
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;

                _inputLogger.StartLogging();

                StatusText.Text = "Capture started; encoder warming up... (Input logging active)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Something tripped up while starting:\n{ex.Message}", "Oops", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "Error starting.";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _inputLogger.StopLogging();

            // Stop capture + encoder
            _capturer.StopCapture();
            _encoder.DisposeEncoder();

            // Clear preview
            Dispatcher.Invoke(() =>
            {
                PreviewImage.Source = null;
                _previewBitmap = null;
            });

            // Restore UI
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _inputLogger?.Dispose();

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