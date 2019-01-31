namespace LostTech.Windows
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using LostTech.Windows.Win32;
    using Microsoft.Win32;

    public sealed class Win32ScreenProvider: IScreenProvider, IDisposable
    {
        readonly ObservableCollection<Win32Screen> screens = new ObservableCollection<Win32Screen>();

        public ReadOnlyObservableCollection<Win32Screen> Screens { get; }

        public Win32ScreenProvider()
        {
            this.Screens = new ReadOnlyObservableCollection<Win32Screen>(this.screens);
            SystemEvents.DisplaySettingsChanged += this.SystemEventsOnDisplaySettingsChanged;
            this.UpdateScreens();
        }

        void SystemEventsOnDisplaySettingsChanged(object sender, EventArgs e) => this.UpdateScreens();

        internal static IEnumerable<DisplayDevice> GetDisplayDevices()
        {
            var device = new DisplayDevice {Size = Marshal.SizeOf<DisplayDevice>()};
            for (int i = 0;
                DisplayDevice.EnumDisplayDevices(null, i, ref device, DisplayDevice.EnumDisplayDevicesFlags.None);
                i++)
                yield return device;
        }

        void UpdateScreens([CallerMemberName] string calledFrom = null)
        {
            Debug.WriteLine("UpdateScreens: " + calledFrom);
            var knownScreens = new List<string>();
            foreach(var device in GetDisplayDevices()) {
                knownScreens.Add(device.Name);
                var screen = this.screens.FirstOrDefault(s => s.DeviceName == device.Name);
                if (screen == null) {
                    screen = new Win32Screen(device);
                    this.screens.Add(screen);
                }
                //Debug.WriteLine(
                //    $"name: {device.Name}; str: {device.String}; flags: {device.StateFlags}; ID: {device.ID}; key: {device.Key}");
            }

            for (int i = 0; i < this.screens.Count;) {
                var screen = this.screens[i];
                if (!knownScreens.Contains(screen.DeviceName))
                    this.screens.RemoveAt(i);
                else
                    i++;
            }
        }

        public void Dispose() {
            SystemEvents.DisplaySettingsChanged -= this.SystemEventsOnDisplaySettingsChanged;
        }
    }
}
