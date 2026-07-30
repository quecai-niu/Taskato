using System.Windows;

namespace Taskato.Services
{
    /// <summary>集中定义窗口显示状态的持久化边界，避免最小化或托盘恢复覆盖用户的最大化选择。</summary>
    public static class WindowStatePersistence
    {
        public static bool CaptureIsMaximized(bool previousValue, WindowState currentState) =>
            currentState switch
            {
                WindowState.Maximized => true,
                WindowState.Normal => false,
                _ => previousValue
            };

        public static WindowState ResolveActivationState(
            WindowState currentState,
            bool wasMaximized) =>
            currentState == WindowState.Minimized
                ? wasMaximized ? WindowState.Maximized : WindowState.Normal
                : currentState;
    }
}
