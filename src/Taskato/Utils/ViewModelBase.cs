using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Taskato.Utils
{
    /// <summary>
    /// ViewModel 基类 — 实现 INotifyPropertyChanged 接口
    /// 所有 ViewModel 继承此类即可获得属性变更通知能力，
    /// 这是 WPF MVVM 数据绑定的核心基础设施。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// 属性变更事件 — WPF 绑定引擎监听此事件来更新 UI
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// CallerMemberName 特性会自动填入调用方的属性名称，避免硬编码字符串
        /// </summary>
        /// <param name="propertyName">发生变更的属性名（自动填入）</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 通用的属性设置方法 — 仅当值真正改变时才触发通知，避免无效刷新
        /// 使用方式: SetProperty(ref _myField, value);
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">后备字段的引用</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名（自动填入）</param>
        /// <returns>值是否真正发生了变化</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // 如果新旧值相同，跳过更新，返回 false
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
