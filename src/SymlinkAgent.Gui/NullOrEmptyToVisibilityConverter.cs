using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SymlinkAgent.Gui;

/// <summary>
/// 字符串为空/null → Visible,非空 → Collapsed(默认)。
/// ConverterParameter="invert" 时反转:非空 → Visible,空 → Collapsed。
/// 默认用于搜索栏 Placeholder;invert 用于空状态占位提示(有提示文本才显示)。
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public static readonly NullOrEmptyToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool empty = string.IsNullOrEmpty(value as string);
        bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? !empty : empty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
