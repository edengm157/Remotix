using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using WinRT;

namespace ScreenCaptureApp
{
    public partial class MainWindow : Window
    {
        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;
        private IDirect3DDevice _device;
        private Device _d3dDevice;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDirect3D();
            Closed += MainWindow_Closed;
        }

        private void InitializeDirect3D()
        {
            _d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _device = Direct3D11Helper.CreateDevice(_d3dDevice);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use GraphicsCapturePicker for compatibility
                var picker = new GraphicsCapturePicker();

                // Get window handle for the picker
                var hwnd = new WindowInteropHelper(this).Handle;
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                // Let user select what to capture
                _captureItem = await picker.PickSingleItemAsync();

                if (_captureItem == null)
                {
                    StatusText.Text = "Capture cancelled";
                    return;
                }

                _lastSize = _captureItem.Size;

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _captureItem.Size);

                _framePool.FrameArrived += FramePool_FrameArrived;

                _session = _framePool.CreateCaptureSession(_captureItem);

                // Enable cursor capture to show the mouse in the captured frames
                _session.IsCursorCaptureEnabled = true;

                _session.StartCapture();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusText.Text = "Capture started, waiting for frames...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting capture: {ex.Message}\n\nStack: {ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            _session?.Dispose();
            _session = null;

            _framePool?.Dispose();
            _framePool = null;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopped";
        }

        private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null)
                {
                    Dispatcher.Invoke(() => StatusText.Text = "Frame is null");
                    return;
                }

                var newSize = frame.ContentSize;

                Dispatcher.Invoke(() => StatusText.Text = $"Capturing: {newSize.Width}x{newSize.Height}");

                if (newSize.Width != _lastSize.Width || newSize.Height != _lastSize.Height)
                {
                    _lastSize = newSize;
                    _framePool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
                    return;
                }

                using var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                ProcessFrame(bitmap);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Frame error: {ex.Message}";
                    MessageBox.Show($"Frame processing error:\n{ex.Message}\n\nStack:\n{ex.StackTrace}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ProcessFrame(Texture2D texture)
        {
            try
            {
                var desc = texture.Description;
                desc.CpuAccessFlags = CpuAccessFlags.Read;
                desc.Usage = ResourceUsage.Staging;
                desc.OptionFlags = ResourceOptionFlags.None;
                desc.BindFlags = BindFlags.None;

                using var stagingTexture = new Texture2D(_d3dDevice, desc);
                _d3dDevice.ImmediateContext.CopyResource(texture, stagingTexture);

                var dataBox = _d3dDevice.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);

                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        var bitmap = new WriteableBitmap(desc.Width, desc.Height, 96, 96, PixelFormats.Bgra32, null);
                        bitmap.Lock();

                        unsafe
                        {
                            var src = (byte*)dataBox.DataPointer;
                            var dst = (byte*)bitmap.BackBuffer;
                            var stride = desc.Width * 4;

                            for (int y = 0; y < desc.Height; y++)
                            {
                                System.Buffer.MemoryCopy(src + y * dataBox.RowPitch, dst + y * stride, stride, stride);
                            }
                        }

                        bitmap.AddDirtyRect(new Int32Rect(0, 0, desc.Width, desc.Height));
                        bitmap.Unlock();

                        CaptureImage.Source = bitmap;
                        StatusText.Text = $"Displaying: {desc.Width}x{desc.Height}";
                    });
                }
                finally
                {
                    _d3dDevice.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Process error: {ex.Message}";
                    MessageBox.Show($"Processing error:\n{ex.Message}\n\nStack:\n{ex.StackTrace}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            StopCapture();
            _device?.Dispose();
            _d3dDevice?.Dispose();
        }
    }

    // Helper class to bridge Windows.Graphics.DirectX.Direct3D11 and SharpDX
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
        static extern int CreateDirect3D11DeviceFromDXGIDeviceNative(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public static IDirect3DDevice CreateDevice(Device device)
        {
            using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();

            var hr = CreateDirect3D11DeviceFromDXGIDeviceNative(dxgiDevice.NativePointer, out var pUnknown);

            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                var d3dDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                return d3dDevice;
            }
            finally
            {
                Marshal.Release(pUnknown);
            }
        }

        public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
        {
            // Use WinRT's native interop to get the underlying COM pointer
            var surfaceNative = surface as IWinRTObject;
            if (surfaceNative == null)
            {
                throw new InvalidOperationException("Surface does not implement IWinRTObject");
            }

            // Get the native object reference
            var objRef = surfaceNative.NativeObject;
            var thisPtr = objRef.ThisPtr;

            // Query for the actual IDirect3DDxgiInterfaceAccess interface
            var accessGuid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            var hr = Marshal.QueryInterface(thisPtr, ref accessGuid, out IntPtr accessPtr);

            if (hr != 0)
            {
                throw new COMException($"Failed to QueryInterface for IDirect3DDxgiInterfaceAccess. HRESULT: 0x{hr:X8}", hr);
            }

            try
            {
                // IDirect3DDxgiInterfaceAccess has only one method: GetInterface
                // It's at vtable offset 3 (after IUnknown's 3 methods: QueryInterface, AddRef, Release)
                var vtbl = Marshal.ReadIntPtr(accessPtr);
                var getInterfacePtr = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);

                var getInterface = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(getInterfacePtr);

                var textureGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); // ID3D11Texture2D GUID
                hr = getInterface(accessPtr, ref textureGuid, out IntPtr texturePtr);

                if (hr != 0)
                {
                    throw new COMException($"GetInterface failed. HRESULT: 0x{hr:X8}", hr);
                }

                if (texturePtr == IntPtr.Zero)
                {
                    throw new Exception("GetInterface returned null pointer");
                }

                return new Texture2D(texturePtr);
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterfaceDelegate(IntPtr thisPtr, ref Guid iid, out IntPtr ppv);
    }
}