using Microsoft.Win32;

namespace Taskato.Services
{
    /// <summary>
    /// 开机自启服务 — 通过 Windows 注册表实现开机自动启动
    /// 
    /// 原理：在注册表 HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run 下
    /// 添加一个键值对，键名是应用名称，值是 exe 的完整路径。
    /// Windows 在用户登录时会自动运行该路径下的所有程序。
    /// 
    /// 使用 HKCU（当前用户）而非 HKLM（所有用户）的原因：
    /// - HKCU 不需要管理员权限
    /// - 只对当前用户生效，更安全
    /// </summary>
    public static class AutoStartService
    {
        /// <summary>
        /// 注册表中的键名（用应用名称作标识）
        /// </summary>
        private const string AppName = "Taskato";

        /// <summary>
        /// 注册表路径 — Windows 开机自启的标准位置
        /// </summary>
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// 设置开机自启状态
        /// </summary>
        /// <param name="enable">true = 开启自启, false = 关闭自启</param>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                // 打开注册表（可写模式）
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                if (key == null) return;

                if (enable)
                {
                    // 获取当前 exe 的完整路径
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // 写入注册表：键名=Taskato, 值=exe路径
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    // 从注册表中删除键值，取消开机自启
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                // 注册表操作失败（通常是权限问题），静默处理
                System.Diagnostics.Debug.WriteLine($"设置开机自启失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 查询当前是否已设置开机自启
        /// </summary>
        /// <returns>true = 已启用, false = 未启用</returns>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
                var value = key?.GetValue(AppName);
                return value != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
