using System.Globalization;
using System.Windows.Data;

namespace SymlinkAgent.Gui;

/// <summary>
/// 枚举值 ↔ 布尔:value 等于 ConverterParameter(枚举名)时为 true。
/// 用于把一组 RadioButton 绑定到同一个枚举属性(分段单选),消除每按钮重复样式。
/// ConvertBack 仅在 true 时回写该枚举值(RadioButton 取消选中不回写)。
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is not null
           && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is not null
           ? Enum.Parse(targetType, parameter.ToString()!)
           : Binding.DoNothing;
}
