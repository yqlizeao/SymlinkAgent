using System.Windows.Media;
using SymlinkAgent.Model;

namespace SymlinkAgent.Gui;

/// <summary>源条目行:代表源顶层的一个链接条目.IsApplied 随检查结果可变,故继承可观察基类.</summary>
public sealed class SourceEntryRow : ObservableObject
{
    /// <summary>条目名称(文件名/目录名).</summary>
    public string Name { get; init; } = "";

    /// <summary>"文件"或"目录"的显示文本.</summary>
    public string TypeDisplay { get; init; } = "";

    /// <summary>链接类型枚举.</summary>
    public LinkType LinkType { get; init; }

    /// <summary>源路径.</summary>
    public string SourcePath { get; init; } = "";

    /// <summary>行底色(源条目恒为透明;与 EntryRow 共用同一套 DataGrid 行样式).</summary>
    public Brush RowBackground => Brushes.Transparent;

    private bool _isApplied;
    /// <summary>该条目是否已在当前目标里应用(由最近一次检查结果推得).</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set { if (Set(ref _isApplied, value)) OnPropertyChanged(nameof(AppliedDisplay)); }
    }

    /// <summary>已应用显示:✓ / —.</summary>
    public string AppliedDisplay => _isApplied ? "✓" : "—";
}
