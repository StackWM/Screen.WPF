﻿namespace LostTech.Windows
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Drawing;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Threading;

    using LostTech.Windows.Win32;
    using Microsoft.Win32;
    using PInvoke;
    using static System.FormattableString;
    using FormsScreen = System.Windows.Forms.Screen;

    public sealed class Win32Screen: INotifyPropertyChanged
    {
        DisplayDevice displayDevice;
        IntPtr hMonitor;
        RectangleF workingArea;

        void SetPosition() {
            var topLeft = this.WorkingArea.TopLeft().Scale(1 / (float)this.ToDeviceScale);
            if (topLeft.X > 0) topLeft.X = (int)topLeft.X + 0.5f;
            if (topLeft.Y > 0) topLeft.Y = (int)topLeft.Y + 0.5f;
            this.detectorWindow.Left = topLeft.X;
            this.detectorWindow.Top = topLeft.Y;
        }

        bool dirty;
        readonly Window detectorWindow;
        readonly PresentationSource presentationSource;
        readonly DispatcherTimer workingAreaUpdater = new() {
            Interval = TimeSpan.FromSeconds(3),
        };

        internal Win32Screen(DisplayDevice displayDevice)
        {
            this.Device = displayDevice;
            Debug.WriteLine($"new screen: {this}");
            this.detectorWindow = new Window {
                Left = this.WorkingArea.Left,
                Top = this.WorkingArea.Top,
                ShowInTaskbar = false,
                Title = this.DeviceName,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = 1,
                Height = 1,
            };
            this.detectorWindow.Show();
            this.detectorWindow.DpiChanged += delegate { this.BeginUpdateWorkingArea(); };
            try {
                this.presentationSource = PresentationSource.FromVisual(this.detectorWindow);
                this.SetPosition();
            }
            finally {
                this.detectorWindow.Hide();
            }
            WtsApi.WTSRegisterSessionNotification(this.HwndSource.Handle, NotifySessionFlags.ThisSessionOnly);
            this.HwndSource.AddHook(this.OnWindowMessage);
            this.workingArea = this.GetWorkingArea();
            this.BeginUpdateWorkingArea();
            SystemEvents.DisplaySettingsChanged += this.OnDisplaySettingsChanged;
            this.workingAreaUpdater.Tick += this.WorkingAreaUpdater_Tick;
        }

        void EnsureUpToDate() {
            if (!this.dirty)
                return;

            this.SetPosition();
            this.detectorWindow.Show();
            this.detectorWindow.Hide();
            this.dirty = false;
        }

        IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch ((User32.WindowMessage)msg) {
            case User32.WindowMessage.WM_DPICHANGED:
            case User32.WindowMessage.WM_SETTINGCHANGE:
            case User32.WindowMessage.WM_DISPLAYCHANGE:
                this.ResetDeviceInfo();
                break;
            default:
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e) => this.ResetDeviceInfo();

        void ResetDeviceInfo() {
            var oldDeviceInfo = this.Device;
            this.Device = Win32ScreenProvider.GetDisplayDevices()
                .FirstOrDefault(device => device.Name == this.DeviceName);
            if (!this.Device.IsValid) {
                oldDeviceInfo.StateFlags &= ~(DisplayDeviceStateFlags.AttachedToDesktop |
                                              DisplayDeviceStateFlags.PrimaryDevice);
                this.Device = oldDeviceInfo;
            }

            if (oldDeviceInfo.IsActive != this.Device.IsActive)
                this.OnPropertyChanged(nameof(this.IsActive));
            if (oldDeviceInfo.IsPrimary != this.Device.IsPrimary)
                this.OnPropertyChanged(nameof(this.IsPrimary));

            if (this.Device.IsActive)
                this.ResetMonitorInfo();
        }

        unsafe void ResetMonitorInfo() {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, hdcMonitor, lprcMonitor, data) => {
                var info = User32.MONITORINFOEX.Create();
                if (!User32.GetMonitorInfoEx(monitor, &info)) {
                    var exception = new System.ComponentModel.Win32Exception();
                    if (exception.NativeErrorCode == (int)LostTech.Stack.WindowManagement.WinApi.WinApiErrorCode.ERROR_INVALID_MONITOR_HANDLE)
                        return true;
                    throw exception;
                }

                if (new string(info.DeviceName) == this.DeviceName) {
                    this.hMonitor = monitor;
                    if (this.presentationSource != null) {
                        this.detectorWindow.Dispatcher.BeginInvoke(() => this.BeginUpdateWorkingArea());
                    }
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        HwndSource HwndSource => (HwndSource)this.presentationSource;

        async void BeginUpdateWorkingArea() {
            for (int retry = 0; retry < 100; retry++) {
                var newWorkingArea = this.GetWorkingArea();
                if (newWorkingArea.IsEmpty) {
                    if (!this.Device.IsActive)
                        return;

                    await Task.Delay(50);
                    continue;
                }

                if (!this.workingArea.Equals(newWorkingArea)) {
                    this.workingArea = newWorkingArea;
                    this.OnPropertyChanged(nameof(this.WorkingArea));
                    Debug.WriteLine($"screen {this.ID} new working area: {this.workingArea.Width}x{this.workingArea.Height}");
                }

                if (this.ToDeviceScale != this.lastToDeviceScale) {
                    this.lastToDeviceScale = this.ToDeviceScale;
                    this.OnPropertyChanged(nameof(TransformToDevice));
                    this.OnPropertyChanged(nameof(TransformFromDevice));
                }

                this.dirty = true;
                return;
            }
            if (this.Device.IsActive)
                Debug.WriteLine($"failed to update working area for {this.ID}");
        }

        public Matrix TransformFromDevice
        {
            get {
                this.EnsureUpToDate();
                Debug.Assert(ReferenceEquals(this.presentationSource, PresentationSource.FromVisual(this.detectorWindow)));
                var scale = Matrix.Identity;
                double toDeviceScale = 1/this.ToDeviceScale;
                scale.Scale(toDeviceScale, toDeviceScale);
                return scale;
                // return this.presentationSource.CompositionTarget.TransformFromDevice;
            }
        }
        public Matrix TransformToDevice {
            get {
                this.EnsureUpToDate();
                var scale = Matrix.Identity;
                double toDeviceScale = this.ToDeviceScale;
                scale.Scale(toDeviceScale, toDeviceScale);
                return scale;
                //return this.presentationSource.CompositionTarget.TransformToDevice;
            }
        }

        double lastToDeviceScale = 1;
        double ToDeviceScale => this.WindowToDeviceScale(this.HwndSource);

        public double WindowToDeviceScale(HwndSource windowHandleSource) {
            double toDevice = windowHandleSource.CompositionTarget.TransformToDevice.M11;
            if (this.hMonitor == IntPtr.Zero || GetDpiForMonitorProxy(this.hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI,
                    out var dpi, out var _) != HResult.Code.S_OK) {
                return toDevice;
            }

            int windowDPI = GetDpiForWindow?.Invoke(windowHandleSource.Handle) ?? checked((int)dpi);
            return toDevice * dpi / windowDPI;
        }
        public bool IsActive => this.Device.IsActive;
        public string ID => this.DeviceName.Replace(@"\\.\DISPLAY", "");
        internal string DeviceName => this.Device.Name;
        public override string ToString() => Invariant($"{this.ID} ({(int)this.WorkingArea.Width}x{(int)this.WorkingArea.Height} @ {(int)this.WorkingArea.Left};{(int)this.WorkingArea.Top})");
        public bool IsPrimary => this.Device.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice);

        /// <summary>
        /// This is non-WPF area. One needs to use <see cref="TransformFromDevice"/> to get WPF compatible one.
        /// </summary>
        public RectangleF WorkingArea => this.GetWorkingArea();

        DisplayDevice Device {
            get { return this.displayDevice; }
            set {
                this.displayDevice = value;
                this.ResetMonitorInfo();
            }
        }

        RectangleF GetWorkingArea()
        {
            return FormsScreen.AllScreens
                       .FirstOrDefault(s => s.DeviceName == this.DeviceName)
                       ?.WorkingArea
                   ?? new RectangleF();
        }

        void WorkingAreaUpdater_Tick(object? sender, EventArgs e) {
            if (!this.Device.IsActive) return;
            this.ResetScreenCache();
            this.BeginUpdateWorkingArea();
        }

        void ResetScreenCache() {
            // use reflection to empty screens cache
            typeof(FormsScreen).GetField("screens", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, null);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void OnPropertyChanged(string propertyName)
            => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        [DllImport("User32.dll", SetLastError = true)]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MONITORENUMPROC lpfnEnum, IntPtr dwData);

        delegate HResult GetDpiForMonitorFunction(IntPtr hMonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

        static HResult GetDpiForMonitorFake(IntPtr hMonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY) {
            dpiX = dpiY = 96;
            return HResult.Code.E_NOTIMPL;
        }
        [DllImport("Shcore.dll")]
        static extern HResult GetDpiForMonitor(IntPtr hMonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);
        static readonly GetDpiForMonitorFunction GetDpiForMonitorProxy;

        public delegate bool MONITORENUMPROC(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        static readonly DpiForWindowFunction GetDpiForWindow;

        delegate int DpiForWindowFunction(IntPtr hwnd);

        static Win32Screen() {
            var user32 = Kernel32.LoadLibrary("user32.dll");
            GetDpiForWindow =
                Kernel32.GetProcAddress(user32, nameof(User32.GetDpiForWindow)) != IntPtr.Zero
                    ? new DpiForWindowFunction(User32.GetDpiForWindow)
                    : null;
            var shcore = Kernel32.LoadLibrary("Shcore.dll");
            GetDpiForMonitorProxy =
                !shcore.IsInvalid && Kernel32.GetProcAddress(shcore, nameof(GetDpiForMonitor)) != IntPtr.Zero
                    ? new GetDpiForMonitorFunction(GetDpiForMonitor)
                    : new GetDpiForMonitorFunction(GetDpiForMonitorFake);
        }
    }
}
