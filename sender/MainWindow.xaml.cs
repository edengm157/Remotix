using sender;
using SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Device = SharpDX.Direct3D11.Device;
using System;

namespace ScreenCaptureApp
{
    public partial class MainWindow : Window
    {
        private FrameCapturer _capturer;
        private VideoEncoder _encoder;
        private WriteableBitmap _previewBitmap;
        private InputReceiver _inputReceiver;
        private System.Windows.Threading.DispatcherTimer _metricsUpdateTimer;

        public MainWindow()
        {
            InitializeComponent();

            // Create the objects
            _capturer = new FrameCapturer();
            _encoder = new VideoEncoder();
            _inputReceiver = new InputReceiver();
            _inputReceiver.Start();

            // Wire capturer -> encoder
            _capturer.FrameReady += (tex, desc) =>
            {
                _encoder.EncodeAndSendFrame(tex, desc, _capturer.MainDevice,
                    _capturer.UdpClient, _capturer.RemoteTarget,
                    this.Dispatcher, UpdateStatusFromEncoder);
            };

            // Wire encoder -> preview update
            _encoder.FrameDataReady += (frameData, width, height) =>
            {
                UpdatePreview(frameData, width, height);
            };

            // Subscribe to metrics updates
            _encoder.MetricsUpdated += OnMetricsUpdated;

            // Initialize D3D + UDP
            _capturer.InitD3D();

            // Setup metrics update timer
            _metricsUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _metricsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500); // Update every 500ms
            _metricsUpdateTimer.Tick += MetricsUpdateTimer_Tick;

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

        private void UpdateStatusFromEncoder(string status)
        {
            Dispatcher.Invoke(() => { StatusText.Text = status; });
        }

        private void OnMetricsUpdated(PerformanceMonitor monitor)
        {
            // This is already on the UI thread
            UpdateMetricsDisplay(monitor);
        }

        private void MetricsUpdateTimer_Tick(object sender, EventArgs e)
        {
            // Periodically update metrics even if not triggered by frame
            if (_encoder?.PerformanceMonitor != null)
            {
                UpdateMetricsDisplay(_encoder.PerformanceMonitor);
            }
        }

        private void UpdateMetricsDisplay(PerformanceMonitor monitor)
        {
            Dispatcher.Invoke(() =>
            {
                FpsText.Text = monitor.CurrentFPS.ToString("F1");
                BitrateText.Text = monitor.CurrentBitrateMbps.ToString("F2");
                LatencyText.Text = monitor.AverageLatencyMs.ToString("F0");
                DroppedText.Text = monitor.DroppedFrames.ToString();
                QualityIndicator.Text = monitor.GetQualityIndicator();

                // Color code FPS text
                if (monitor.CurrentFPS >= 25)
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Green
                else if (monitor.CurrentFPS >= 15)
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Yellow
                else
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
            });
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _capturer.StartCaptureAsync(this);

                var sz = _capturer.LastSize;
                if (sz.HasValue)
                {
                    _encoder.InitializeEncoder(sz.Value.Width, sz.Value.Height,
                        this.Dispatcher, UpdateStatusFromEncoder);
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

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                QualityPresetComboBox.IsEnabled = true;

                // Start metrics timer
                _metricsUpdateTimer.Start();

                StatusText.Text = "🎬 Capture started | Monitoring performance...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting capture:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "❌ Error starting";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop metrics timer
            _metricsUpdateTimer.Stop();

            _capturer.StopCapture();
            _encoder.DisposeEncoder();

            Dispatcher.Invoke(() =>
            {
                PreviewImage.Source = null;
                _previewBitmap = null;

                // Reset metrics display
                FpsText.Text = "0.0";
                BitrateText.Text = "0.0";
                LatencyText.Text = "0";
                DroppedText.Text = "0";
                QualityIndicator.Text = "⚪";
            });

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            QualityPresetComboBox.IsEnabled = false;

            StatusText.Text = "⏹ Stopped";
        }

        private void AdaptiveQuality_Changed(object sender, RoutedEventArgs e)
        {
            if (_encoder != null)
            {
                bool enabled = AdaptiveQualityCheckBox.IsChecked == true;
                _encoder.SetAdaptiveQuality(enabled);

                QualityPresetComboBox.IsEnabled = !enabled && StopButton.IsEnabled;

                StatusText.Text = enabled ?
                    "✅ Adaptive quality enabled - will adjust automatically" :
                    "🔧 Manual quality control";
            }
        }

        private void QualityPreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_encoder?.QualityController != null && QualityPresetComboBox.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString();
                QualityPreset preset = tag switch
                {
                    "Ultra" => QualityPreset.Ultra,
                    "High" => QualityPreset.High,
                    "Medium" => QualityPreset.Medium,
                    "Low" => QualityPreset.Low,
                    "VeryLow" => QualityPreset.VeryLow,
                    _ => QualityPreset.High
                };

                _encoder.QualityController.SetPreset(preset);
                StatusText.Text = $"🎨 Quality preset changed to: {preset}";
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _metricsUpdateTimer?.Stop();
            _capturer.StopCapture();
            _capturer.DisposeAll();
            _encoder.DisposeEncoder();
            _inputReceiver?.Dispose();
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