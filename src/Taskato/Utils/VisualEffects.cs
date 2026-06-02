using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Taskato.Utils
{
    /// <summary>
    /// Coordinates temporary visual-effect reduction while a window is scrolling,
    /// resizing, or being dragged.
    /// </summary>
    public static class VisualEffects
    {
        private const int RestoreDelayMs = 420;
        private const int WmSizing = 0x0214;
        private const int WmMoving = 0x0216;
        private const int WmEnterSizeMove = 0x0231;
        private const int WmExitSizeMove = 0x0232;

        public static readonly DependencyProperty IsReducingEffectsProperty =
            DependencyProperty.RegisterAttached(
                "IsReducingEffects",
                typeof(bool),
                typeof(VisualEffects),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        private static readonly DependencyProperty RestoreTimerProperty =
            DependencyProperty.RegisterAttached(
                "RestoreTimer",
                typeof(DispatcherTimer),
                typeof(VisualEffects),
                new PropertyMetadata(null));

        private static readonly DependencyProperty IsInitializedProperty =
            DependencyProperty.RegisterAttached(
                "IsInitialized",
                typeof(bool),
                typeof(VisualEffects),
                new PropertyMetadata(false));

        private static readonly DependencyProperty IsNativeMoveSizeActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsNativeMoveSizeActive",
                typeof(bool),
                typeof(VisualEffects),
                new PropertyMetadata(false));

        private static readonly DependencyProperty HwndHookProperty =
            DependencyProperty.RegisterAttached(
                "HwndHook",
                typeof(HwndSourceHook),
                typeof(VisualEffects),
                new PropertyMetadata(null));

        private static readonly DependencyProperty HwndSourceProperty =
            DependencyProperty.RegisterAttached(
                "HwndSource",
                typeof(HwndSource),
                typeof(VisualEffects),
                new PropertyMetadata(null));

        public static bool GetIsReducingEffects(DependencyObject obj) =>
            (bool)obj.GetValue(IsReducingEffectsProperty);

        public static void SetIsReducingEffects(DependencyObject obj, bool value) =>
            obj.SetValue(IsReducingEffectsProperty, value);

        public static void Initialize(Window window)
        {
            if ((bool)window.GetValue(IsInitializedProperty))
                return;

            var restoreTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RestoreDelayMs)
            };

            restoreTimer.Tick += (_, _) =>
            {
                if (GetIsNativeMoveSizeActive(window))
                {
                    restoreTimer.Stop();
                    restoreTimer.Start();
                    return;
                }

                restoreTimer.Stop();
                SetIsReducingEffects(window, false);
            };

            window.SetValue(RestoreTimerProperty, restoreTimer);
            window.SetValue(IsInitializedProperty, true);

            window.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, e) =>
                {
                    if (e.VerticalChange == 0 && e.HorizontalChange == 0)
                        return;

                    BeginTemporaryReduction(window);
                    ScheduleRestore(window);
                }),
                true);

            window.SizeChanged += (_, _) =>
            {
                if (!window.IsLoaded || window.WindowState == WindowState.Minimized)
                    return;

                BeginTemporaryReduction(window);
                ScheduleRestore(window);
            };

            window.SourceInitialized += (_, _) => AttachNativeMoveSizeHook(window);
            window.Closed += (_, _) => DetachNativeMoveSizeHook(window);

            AttachNativeMoveSizeHook(window);
        }

        public static void RunWithTemporaryReduction(Window window, Action action)
        {
            BeginTemporaryReduction(window);
            FlushRender(window);

            try
            {
                action();
            }
            finally
            {
                ScheduleRestore(window);
            }
        }

        public static void BeginTemporaryReduction(Window window)
        {
            GetRestoreTimer(window)?.Stop();
            SetIsReducingEffects(window, true);
        }

        public static void ScheduleRestore(Window window)
        {
            var restoreTimer = GetRestoreTimer(window);
            if (restoreTimer == null)
            {
                SetIsReducingEffects(window, false);
                return;
            }

            restoreTimer.Stop();
            restoreTimer.Start();
        }

        private static DispatcherTimer? GetRestoreTimer(Window window) =>
            window.GetValue(RestoreTimerProperty) as DispatcherTimer;

        private static bool GetIsNativeMoveSizeActive(Window window) =>
            (bool)window.GetValue(IsNativeMoveSizeActiveProperty);

        private static void SetIsNativeMoveSizeActive(Window window, bool value) =>
            window.SetValue(IsNativeMoveSizeActiveProperty, value);

        private static void AttachNativeMoveSizeHook(Window window)
        {
            if (window.GetValue(HwndHookProperty) is HwndSourceHook)
                return;

            if (PresentationSource.FromVisual(window) is not HwndSource source)
                return;

            HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                switch (msg)
                {
                    case WmEnterSizeMove:
                        SetIsNativeMoveSizeActive(window, true);
                        BeginTemporaryReduction(window);
                        break;

                    case WmMoving:
                    case WmSizing:
                        BeginTemporaryReduction(window);
                        if (!GetIsNativeMoveSizeActive(window))
                        {
                            ScheduleRestore(window);
                        }
                        break;

                    case WmExitSizeMove:
                        SetIsNativeMoveSizeActive(window, false);
                        ScheduleRestore(window);
                        break;
                }

                return IntPtr.Zero;
            };

            source.AddHook(hook);
            window.SetValue(HwndHookProperty, hook);
            window.SetValue(HwndSourceProperty, source);
        }

        private static void DetachNativeMoveSizeHook(Window window)
        {
            if (window.GetValue(HwndSourceProperty) is HwndSource source &&
                window.GetValue(HwndHookProperty) is HwndSourceHook hook)
            {
                source.RemoveHook(hook);
            }

            window.ClearValue(HwndSourceProperty);
            window.ClearValue(HwndHookProperty);
            window.ClearValue(IsNativeMoveSizeActiveProperty);
        }

        private static void FlushRender(Window window)
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(
                DispatcherPriority.Render,
                new Action(() => { }));
        }
    }
}
