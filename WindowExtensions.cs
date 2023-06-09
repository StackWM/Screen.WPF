﻿namespace LostTech.Windows {
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Interop;
    using LostTech.Stack.WindowManagement;

    public static class WindowExtensions
    {
        public static async Task<bool> AdjustToClientArea(this Window window, Win32Screen screen) {
            if (window is null) throw new ArgumentNullException(nameof(window));
            if (screen is null) throw new ArgumentNullException(nameof(screen));

            Vector wpfScreenSize = screen.TransformFromDevice.Transform(screen.WorkingArea.Size.AsWPFVector());
            if (wpfScreenSize.X <= 1 || wpfScreenSize.Y <= 1)
                return false;
            // this is a hack to force layout recompute. InvalidateMeasure does not help
            window.Width = wpfScreenSize.X + 1;
            window.Width = wpfScreenSize.X;
            window.Height = wpfScreenSize.Y;
            await MoveToScreen(window, screen).ConfigureAwait(false);
            return true;
        }

        public static Task MoveToScreen(Window window, Win32Screen screen) {
            if (screen == null)
                throw new ArgumentNullException(nameof(screen));
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            var nativeWindow = window.GetNativeWindow();
            return nativeWindow.Move(screen.WorkingArea);
        }

        /// <summary>
        /// Sets location and size of the window to fit to <see cref="Window.Margin"/> on the specified screen.
        /// Negative margin behavior is undefined.
        /// </summary>
        public static Task FitToMargin(this Window window, Win32Screen screen) {
            if (window is null) throw new ArgumentNullException(nameof(window));
            if (screen is null) throw new ArgumentNullException(nameof(screen));

            var handleSource = (HwndSource)PresentationSource.FromVisual(window);
            var nativeWindow = window.GetNativeWindow();

            Vector wpfScreenSize = screen.TransformFromDevice.Transform(screen.WorkingArea.Size.AsWPFVector());
            double width = wpfScreenSize.X - window.Margin.Left - window.Margin.Right;
            double finalWidth = Math.Min(width, double.IsNaN(window.MaxWidth) ? width : window.MaxWidth);
            double height = wpfScreenSize.Y - window.Margin.Top - window.Margin.Bottom;
            double finalHeight = Math.Min(height, double.IsNaN(window.MaxHeight) ? height : window.MaxHeight);
            Vector sizeFix = new Vector(width - finalWidth, height - finalHeight) / 2;
            Vector marginOffset = new Vector(window.Margin.Left, window.Margin.Top);
            marginOffset = screen.TransformToDevice.Transform(marginOffset);
            sizeFix = screen.TransformToDevice.Transform(sizeFix);
            var size = screen.TransformToDevice.Transform(new Vector(finalWidth, finalHeight));

            return nativeWindow.Move(new RectangleF(
                (screen.WorkingArea.TopLeft().ToWPF() + marginOffset + sizeFix).ToDrawingPoint(),
                new SizeF((float)size.X, (float)size.Y)));
        }
    }
}
