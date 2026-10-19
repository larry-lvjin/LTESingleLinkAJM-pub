using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LTESingleLinkAJM.ViewModels;

namespace LTESingleLinkAJM.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
            StateChanged += OnStateChanged;
        }

        // ── 无边框窗口 (WindowStyle=None) 最大化默认铺满整屏、盖住任务栏,
        // 底部内容会被挡。处理 WM_GETMINMAXINFO 把最大化尺寸限制在
        // 当前显示器的工作区 (任务栏之外), 多显示器/任务栏任意停靠边均正确。──
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    // 最大化位置/尺寸 = 工作区相对显示器原点 (物理像素)
                    mmi.ptMaxPosition.x = info.rcWork.left - info.rcMonitor.left;
                    mmi.ptMaxPosition.y = info.rcWork.top - info.rcMonitor.top;
                    mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
                    mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
        }

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.Dispose();
        }

        private void OnStateChanged(object? sender, System.EventArgs e)
        {
            var key = WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize";
            MaxIconPath.Data = (Geometry)FindResource(key);
            MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "还原 (Restore)" : "放大填满 (Maximize)";
        }

        private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb) tb.ScrollToEnd();
        }

        private void Min_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Max_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
