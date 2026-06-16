using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SymlinkAgent.Gui;

/// <summary>
/// 主窗口:DataContext 绑定 + 事件处理。
/// 最小化 code-behind:仅处理键盘快捷键、选择联动(多选→VM)、滚动定位、日志滚底等无法纯绑定的交互。
/// </summary>
public partial class MainWindow : Window
{
    private bool _suppressSelectionChanged; // 防止初始化/集合差量同步时误触发

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // 初始化完成后才启用 SelectionChanged 处理
        Loaded += (_, _) => _suppressSelectionChanged = false;
        _suppressSelectionChanged = true;

        // 键盘快捷键
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>键盘快捷键:Ctrl+F 聚焦搜索;Escape 清空搜索;F5 检查;Delete 移除选中行。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            if (Vm is { } vm) vm.SearchText = "";
            SearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Vm?.CheckNow();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && StatusGrid.IsKeyboardFocusWithin && StatusGrid.SelectedItems.Count > 0)
        {
            Vm?.RemoveCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>目标框按 Enter:跳过去抖立即检查。</summary>
    private void TargetBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm?.CheckNow();
            e.Handled = true;
        }
    }

    /// <summary>从目标历史下拉中选中一项后:立即检查(跳过去抖)。文本输入则走 VM 去抖。</summary>
    private void TargetPathComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (sender is ComboBox cb && cb.SelectedItem is string path && !string.IsNullOrWhiteSpace(path) && Vm is { } vm)
        {
            vm.ProjectPath = path;
            vm.CheckNow();
        }
    }

    /// <summary>右侧 DataGrid 多选变化 → 把选中名字集合推给 VM(驱动单条/多条操作 + 按钮文案)。</summary>
    private void StatusGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || Vm is not { } vm) return;
        vm.SelectedEntryNames.Clear();
        foreach (var item in grid.SelectedItems)
            if (item is EntryRow row) vm.SelectedEntryNames.Add(row.Name);
        vm.NotifySelectionChanged();
    }

    /// <summary>左侧选中源条目 → 右侧 DataGrid 滚动到对应行(若存在)。</summary>
    private void SourceEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (sender is not DataGrid dg || dg.SelectedItem is not SourceEntryRow entry) return;

        foreach (var item in StatusGrid.Items)
        {
            if (item is EntryRow row && string.Equals(row.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                StatusGrid.ScrollIntoView(item);
                StatusGrid.SelectedItem = item;
                break;
            }
        }
    }

    /// <summary>左键点击 DataGrid 空白区(非行、非表头)→ 清除选中。</summary>
    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (HitTestAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is null &&
            HitTestAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(e.OriginalSource as DependencyObject) is null)
        {
            grid.UnselectAll();
        }
    }

    /// <summary>右键点击某行 → 先选中该行(若未在多选内),使右键菜单作用到它。</summary>
    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (HitTestAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is DataGridRow row && !row.IsSelected)
        {
            grid.UnselectAll();
            row.IsSelected = true;
            grid.SelectedItem = row.Item;
        }
    }

    /// <summary>从可视树向上找指定类型的祖先(含自身)。</summary>
    private static T? HitTestAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        return node as T;
    }

    /// <summary>日志追加后自动滚到底部。</summary>
    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }
}
