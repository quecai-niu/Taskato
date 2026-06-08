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
            // values[2] = CompletionDurationMinutes (int? 可空，可选)
            if (values[0] is DateTime createdAt)
            {
                string GetDatePrefix(DateTime dt)
                {
                    if (dt.Date == DateTime.Today) return "";
                    if (dt.Date == DateTime.Today.AddDays(-1)) return "昨天 ";
                    return $"{dt:M月d日} ";
                }

                var result = $"{GetDatePrefix(createdAt)}{createdAt:HH:mm} 创建";

                if (values[1] is DateTime completedAt)
                {
                    result += $" · {GetDatePrefix(completedAt)}{completedAt:HH:mm} 完成";
                }

                if (values.Length >= 3 && values[2] is int durationMinutes && durationMinutes > 0)
                {
                    result += $" · 耗时 {FormatDuration(durationMinutes)}";
                }

                return result;
            }

            return string.Empty;
        }

        private static string FormatDuration(int durationMinutes)
        {
            var hours = durationMinutes / 60;
            var minutes = durationMinutes % 60;

            if (hours > 0 && minutes > 0) return $"{hours}小时{minutes}分钟";
            if (hours > 0) return $"{hours}小时";
            return $"{minutes}分钟";
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
    /// <summary>
    /// 布尔值取反 → Visibility 转换器（true = Collapsed, false = Visible）
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool b && b;
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v != Visibility.Visible;
        }
    }

    /// <summary>
    /// 优先级整数转标签文本（3→"[紧急]", 2→"[高]", 1→"[中]", 0→""）
    /// </summary>
    public class PriorityToLabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int p)
            {
                return p switch
                {
                    3 => "[紧急]",
                    2 => "[高]",
                    1 => "[中]",
                    _ => ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
