using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Taskato.Converters
{
    /// <summary>
    /// 布尔值取反转换器 — 将 true 转为 false，false 转为 true
    /// 
    /// 使用场景示例：
    /// 当 IsCompleted = true 时，按钮应该显示为不可用
    /// 绑定: IsEnabled="{Binding IsCompleted, Converter={StaticResource InverseBoolConverter}}"
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : value;
        }
    }

    /// <summary>
    /// 布尔值 → Visibility 转换器（true = Visible, false = Collapsed）
    /// 
    /// WPF 内置了 BooleanToVisibilityConverter，但它把 false 转为 Hidden 而非 Collapsed。
    /// Collapsed 不占布局空间，Hidden 占空间但不显示。大多数场景我们要 Collapsed。
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;

            // 如果参数为 "Inverse"，取反逻辑
            if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    /// <summary>
    /// DateTime → 友好显示文本转换器
    /// 
    /// 示例输出：
    /// - "14:30 创建"
    /// - "14:30 创建 · 16:00 完成"
    /// </summary>
    public class TaskTimeDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return string.Empty;

            // values[0] = CreatedAt (DateTime)
            // values[1] = CompletedAt (DateTime? 可空)
            if (values[0] is DateTime createdAt)
            {
                var result = $"{createdAt:HH:mm} 创建";

                if (values[1] is DateTime completedAt)
                {
                    result += $" · {completedAt:HH:mm} 完成";
                }

                return result;
            }

            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 整数与布尔值双向转换器 — 用于 RadioButton 的组合选择
    /// 
    /// 逻辑：
    /// Convert: return (value == parameter)
    /// ConvertBack: if (value == true) return parameter
    /// </summary>
    public class IntToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return System.Windows.Data.Binding.DoNothing;
            return (bool)value ? int.Parse(parameter.ToString()!) : System.Windows.Data.Binding.DoNothing;
        }
    }
}
