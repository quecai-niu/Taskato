using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Taskato.Services
{
    /// <summary>
    /// 让无边框主窗口按当前显示器的工作区最大化，避免覆盖位于任意边缘的 Windows 任务栏。
    /// </summary>
    public static class WindowWorkAreaService
    {
        private const int WmGetMinMaxInfo = 0x0024;
        private const uint MonitorDefaultToNearest = 0x00000002;

        public static void Attach(Window window)
        {
            void AttachHook(object? sender, EventArgs args)
            {
                window.SourceInitialized -= AttachHook;
                if (PresentationSource.FromVisual(window) is HwndSource source)
                    source.AddHook(WindowProcedure);
            }

            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook(WindowProcedure);
            else
                window.SourceInitialized += AttachHook;
        }

        private static IntPtr WindowProcedure(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
                return IntPtr.Zero;

            var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
                return IntPtr.Zero;

            var monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
                return IntPtr.Zero;

            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var workArea = monitorInfo.WorkArea;
            var monitorArea = monitorInfo.MonitorArea;

            // WM_GETMINMAXINFO 使用显示器像素坐标；相对偏移同时覆盖顶部或左侧任务栏场景。
            minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
            minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
            minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
            minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;
            minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize;

            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            handled = true;
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRectangle MonitorArea;
            public NativeRectangle WorkArea;
            public uint Flags;
        }
    }
}
