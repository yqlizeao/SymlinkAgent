using System.Globalization;
using System.Windows.Data;

namespace SymlinkAgent.Gui;

/// <summary>
/// 长路径中间省略:保留盘符头 + 尾部(区分条目的关键部分),超长时中间用 … 折叠。
/// 解决"同源多条目源路径被尾部省略号截得一模一样"的问题(完整路径仍在 ToolTip).
/// ConverterParameter 为预算字符数(默认 30).
/// </summary>
public sealed class PathCompactConverter : IValueConverter
{
    public static readonly PathCompactConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return "";
        int budget = 30;
        if (parameter is string p && int.TryParse(p, out int b) && b > 8) budget = b;
        if (s.Length <= budget) return s;

        const int head = 6;                  // 保留盘符等开头
        int tail = budget - head - 1;        // 余量给尾部(减 1 给 …)
        return string.Concat(s.AsSpan(0, head), "…", s.AsSpan(s.Length - tail));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
