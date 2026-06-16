using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SymlinkAgent.Core;
using SymlinkAgent.Model;

namespace SymlinkAgent.Gui;

/// <summary>DataGrid 行:一条链接的当前状态(扩展版,含类型和源路径).</summary>
public sealed class EntryRow
{
    public string Target { get; init; } = "";
    public string Name { get; init; } = "";
    public string TypeDisplay { get; init; } = "";
    public LinkType LinkType { get; init; }
    public string ExpectedSource { get; init; } = "";
    public string StatusText { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Black;
    public Brush RowBackground { get; init; } = Brushes.Transparent;
    public int StatusSeverity { get; init; }

    /// <summary>是否正常链接(状态 Ok,严重度 0).用于右键"应用此条"的启用判定.</summary>
    public bool IsOk => StatusSeverity == 0;
}

/// <summary>
/// 主视图模型:GUI 的"壳" —— 操作都转调 Core 的 TargetEngine,持久化经 DataStore(单一 INI).
/// 设计主线:源(提供什么)→ 目标(现状如何)→ 操作.实时搜索同时过滤左右两侧;
/// 选中行驱动单条/多条操作;刷新时机统一(切源/改目标即自动检查);危险操作有确认;错误可见.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>日志保留的最大行数(环形裁剪,防止长会话无限增长).</summary>
    private const int MaxLogLines = 500;

    // —— 集合 ——

    /// <summary>历史使用过的源(下拉).</summary>
    public ObservableCollection<string> RecentSources { get; } = new();
    /// <summary>当前源下应用过的目标(下拉).</summary>
    public ObservableCollection<string> TargetsForSource { get; } = new();
    /// <summary>当前源顶层将链接的条目(左侧面板,结构化数据).</summary>
    public ObservableCollection<SourceEntryRow> SourceEntries { get; } = new();
    /// <summary>链接状态表(右侧 DataGrid).</summary>
    public ObservableCollection<EntryRow> Entries { get; } = new();

    /// <summary>DataGrid 的集合视图(过滤/排序).</summary>
    private readonly ICollectionView _entriesView;
    /// <summary>左侧源条目的集合视图(同一搜索文本过滤).</summary>
    private readonly ICollectionView _sourceEntriesView;

    /// <summary>条目 LinkType 映射(用于 join status 结果).</summary>
    private readonly Dictionary<string, LinkType> _sourceLinkTypes = new(StringComparer.OrdinalIgnoreCase);

    private bool _loading;

    // —— 源选择 ——

    private string? _selectedSource;
    /// <summary>选中的源(下拉);切换即激活该源.</summary>
    public string? SelectedSource
    {
        get => _selectedSource;
        set { if (Set(ref _selectedSource, value) && !_loading && !string.IsNullOrWhiteSpace(value)) SwitchSource(value!); }
    }

    // —— 目标路径(改动即去抖自动检查) ——

    private string _projectPath = "";
    public string ProjectPath
    {
        get => _projectPath;
        set { if (Set(ref _projectPath, value)) OnProjectPathChanged(); }
    }

    /// <summary>目标路径变更去抖定时器(停顿后自动检查).</summary>
    private DispatcherTimer? _statusDebounce;

    // —— 强制模式(不粘滞:操作后自动复位) ——

    private bool _force;
    public bool Force { get => _force; set => Set(ref _force, value); }

    // —— 日志(可折叠) ——

    private readonly List<string> _logLines = new();

    private string _log = "";
    public string Log { get => _log; private set => Set(ref _log, value); }

    private bool _logExpanded;
    /// <summary>日志面板是否展开.</summary>
    public bool LogExpanded { get => _logExpanded; set => Set(ref _logExpanded, value); }

    // —— 搜索过滤(同时过滤左右两侧) ——

    private string _searchText = "";
    /// <summary>搜索过滤文本(实时过滤左侧源条目 + 右侧 DataGrid).</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) { _entriesView.Refresh(); _sourceEntriesView.Refresh(); } }
    }

    // —— 类型过滤 ——

    private TypeFilter _activeTypeFilter = TypeFilter.All;
    /// <summary>当前激活的类型过滤.</summary>
    public TypeFilter ActiveTypeFilter
    {
        get => _activeTypeFilter;
        set { if (Set(ref _activeTypeFilter, value)) _entriesView.Refresh(); }
    }

    // —— 左侧面板选中项 ——

    private SourceEntryRow? _selectedSourceEntry;
    /// <summary>左侧面板选中的源条目(点击定位到右侧).</summary>
    public SourceEntryRow? SelectedSourceEntry
    {
        get => _selectedSourceEntry;
        set { if (Set(ref _selectedSourceEntry, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    // —— 右侧 DataGrid 选中项 ——

    private EntryRow? _selectedEntry;
    /// <summary>当前选中的 DataGrid 行(由 XAML SelectedItem 绑定).</summary>
    public EntryRow? SelectedEntry
    {
        get => _selectedEntry;
        set { if (Set(ref _selectedEntry, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>多选行的条目名集合(由 code-behind 在 SelectionChanged 时推入).</summary>
    public HashSet<string> SelectedEntryNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>code-behind 在多选变化后调用:刷新按钮文案.</summary>
    public void NotifySelectionChanged() => RefreshActionTexts();

    // —— 操作按钮文案(随选中数/总数变化) ——

    public string ApplyButtonText => ScopedText("应用");
    public string RemoveButtonText => ScopedText("移除");

    private string ScopedText(string verb)
        => SelectedEntryNames.Count > 0
           ? $"{verb} (选中 {SelectedEntryNames.Count})"
           : $"{verb} (全部 {Entries.Count})";

    private void RefreshActionTexts()
    {
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(RemoveButtonText));
    }

    // —— 空状态占位提示 ——

    private string _emptyHint = "";
    /// <summary>右侧 DataGrid 为空时的占位引导文本(非空即显示).</summary>
    public string EmptyHint { get => _emptyHint; private set => Set(ref _emptyHint, value); }

    private string _windowTitle = "SymlinkAgent";
    /// <summary>窗口标题:带当前源文件夹名,一眼知道在操作哪个源.</summary>
    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }

    // —— 状态栏 ——

    private string _statusLeftText = "";
    public string StatusLeftText { get => _statusLeftText; set => Set(ref _statusLeftText, value); }

    private Brush _statusLeftBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    /// <summary>状态栏左侧文字颜色(错误时转红).</summary>
    public Brush StatusLeftBrush { get => _statusLeftBrush; set => Set(ref _statusLeftBrush, value); }

    private string _statusTargetText = "";
    /// <summary>状态栏目标侧:链接健康度概览.</summary>
    public string StatusTargetText { get => _statusTargetText; set => Set(ref _statusTargetText, value); }

    // 最近一次检查的状态计数(供状态栏目标侧概览)
    private int _cntTotal, _cntOk, _cntMissing, _cntNotLink, _cntWrong;
    private bool _statusComputed; // 当前目标是否已核查过

    private static readonly Brush NormalStatusBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush ErrorStatusBrush = Brushes.Firebrick;

    /// <summary>临时消息定时器(操作完成后显示摘要 5 秒).</summary>
    private DispatcherTimer? _transientTimer;

    // —— 命令 ——

    public RelayCommand SetSourceCommand { get; }
    public RelayCommand BrowseTargetCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand StatusCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    // 过滤命令
    public RelayCommand FilterAllCommand { get; }
    public RelayCommand FilterDirCommand { get; }
    public RelayCommand FilterFileCommand { get; }

    // 右键菜单命令
    public RelayCommand CopyTargetPathCommand { get; }
    public RelayCommand CopySourcePathCommand { get; }
    public RelayCommand CopyEntryNameCommand { get; }
    public RelayCommand CopyAllStatusCommand { get; }
    public RelayCommand OpenInExplorerCommand { get; }
    public RelayCommand CopySourceEntryNameCommand { get; }
    public RelayCommand CopySourceEntrySourceCommand { get; }
    public RelayCommand OpenSourceEntryInExplorerCommand { get; }

    // 单条应用 / 移除(右键;按"已应用"状态启用/禁用)
    public RelayCommand ApplySourceEntryCommand { get; }
    public RelayCommand RemoveSourceEntryCommand { get; }
    public RelayCommand ApplyEntryCommand { get; }
    public RelayCommand RemoveEntryCommand { get; }

    public MainViewModel()
    {
        SetSourceCommand = new RelayCommand(SetSource);
        BrowseTargetCommand = new RelayCommand(BrowseTarget);
        ApplyCommand = new RelayCommand(Apply);
        StatusCommand = new RelayCommand(ShowStatus);
        RemoveCommand = new RelayCommand(Remove);
        RefreshCommand = new RelayCommand(ShowStatus);
        ClearLogCommand = new RelayCommand(ClearLog);

        FilterAllCommand = new RelayCommand(() => ActiveTypeFilter = TypeFilter.All);
        FilterDirCommand = new RelayCommand(() => ActiveTypeFilter = TypeFilter.Dir);
        FilterFileCommand = new RelayCommand(() => ActiveTypeFilter = TypeFilter.File);

        CopyTargetPathCommand = new RelayCommand(CopyTargetPath);
        CopySourcePathCommand = new RelayCommand(CopySourcePath);
        CopyEntryNameCommand = new RelayCommand(CopyEntryName);
        CopyAllStatusCommand = new RelayCommand(CopyAllStatus);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer);
        CopySourceEntryNameCommand = new RelayCommand(CopySourceEntryName);
        CopySourceEntrySourceCommand = new RelayCommand(CopySourceEntrySource);
        OpenSourceEntryInExplorerCommand = new RelayCommand(OpenSourceEntryInExplorer);

        // 单条应用/移除:按"是否已应用"启用或禁用,且需先选定目标目录
        ApplySourceEntryCommand = new RelayCommand(ApplySourceEntry,
            () => SelectedSourceEntry is not null && !SelectedSourceEntry.IsApplied && HasTarget);
        RemoveSourceEntryCommand = new RelayCommand(RemoveSourceEntry,
            () => SelectedSourceEntry is not null && SelectedSourceEntry.IsApplied && HasTarget);
        ApplyEntryCommand = new RelayCommand(ApplyEntry,
            () => SelectedEntry is not null && !SelectedEntry.IsOk && HasTarget);
        RemoveEntryCommand = new RelayCommand(RemoveEntry,
            () => SelectedEntry is not null && HasTarget);

        // 集合视图(同一实例,过滤稳定):右侧 = 搜索 + 类型;左侧 = 搜索
        _entriesView = CollectionViewSource.GetDefaultView(Entries);
        _entriesView.Filter = EntriesFilter;
        _sourceEntriesView = CollectionViewSource.GetDefaultView(SourceEntries);
        _sourceEntriesView.Filter = SourceEntriesFilter;

        DataStore data = DataStore.Load();
        foreach (string h in data.RecentSources) RecentSources.Add(h);

        if (!string.IsNullOrWhiteSpace(data.CurrentSource))
        {
            _loading = true;
            if (!RecentSources.Any(h => SamePath(h, data.CurrentSource!))) RecentSources.Insert(0, data.CurrentSource!);
            _selectedSource = RecentSources.First(h => SamePath(h, data.CurrentSource!));
            OnPropertyChanged(nameof(SelectedSource));
            _loading = false;
            RefreshForSource();
        }
        else
        {
            AppendLog("尚未设置源目录。请点击「设置源...」，选一个放着需要链接的文件/目录的文件夹。");
            StatusLeftText = "未设置源";
        }
        UpdateEmptyHint();
    }

    // —— 切换 / 设置源 ——

    private void SwitchSource(string path)
    {
        try
        {
            DataStore.ValidateSourceRoot(path);
            DataStore data = DataStore.Load();
            data.SetCurrentSource(path);
            string full = Path.GetFullPath(path);

            _loading = true;
            if (!RecentSources.Any(h => SamePath(h, full))) RecentSources.Insert(0, full);
            _selectedSource = RecentSources.First(h => SamePath(h, full));
            OnPropertyChanged(nameof(SelectedSource));
            _loading = false;

            AppendLog($"已切换源:{full}");
            RefreshForSource();
        }
        catch (Exception ex)
        {
            _loading = false;
            AppendError(ex.Message);
        }
    }

    private void SetSource()
    {
        var dlg = new OpenFolderDialog { Title = "选择源目录" };
        if (dlg.ShowDialog() == true) SwitchSource(dlg.FolderName);
    }

    private void BrowseTarget()
    {
        var dlg = new OpenFolderDialog { Title = "选择目标目录" };
        if (dlg.ShowDialog() == true)
            ProjectPath = dlg.FolderName; // 触发去抖自动检查
    }

    // —— 目标路径变更:去抖自动检查 ——

    private void OnProjectPathChanged()
    {
        _statusDebounce?.Stop();
        if (string.IsNullOrWhiteSpace(_projectPath))
        {
            ClearStatus();
            return;
        }
        _statusDebounce ??= CreateDebounce();
        _statusDebounce.Start();
    }

    private DispatcherTimer CreateDebounce()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { t.Stop(); ShowStatus(); };
        return t;
    }

    /// <summary>由 code-behind(下拉选择/Enter)调用:跳过去抖立即检查.</summary>
    public void CheckNow()
    {
        _statusDebounce?.Stop();
        ShowStatus();
    }

    // —— 刷新当前源的条目预览 + 目标下拉 ——

    private void RefreshForSource()
    {
        if (!TryEngine(out TargetEngine? engine)) return;

        SourceEntries.Clear();
        _sourceLinkTypes.Clear();
        var specs = engine!.SourceEntries();
        foreach (var s in specs)
        {
            _sourceLinkTypes[s.Name] = s.LinkType;
            SourceEntries.Add(new SourceEntryRow
            {
                Name = s.Name,
                TypeDisplay = s.LinkType == LinkType.SymlinkDir ? "目录" : "文件",
                LinkType = s.LinkType,
                SourcePath = s.Source,
            });
        }
        if (specs.Count == 0)
            AppendLog("提示:源顶层没有可链接的条目。请把需要链接的文件/目录直接放进源根目录。");

        WindowTitle = $"SymlinkAgent — {Path.GetFileName(engine.SourceRoot.TrimEnd('\\', '/'))}";
        RefreshTargets(engine);

        // 切源:清掉上一个源的陈旧右表;若已有目标则立即重查(统一刷新时机)
        Entries.Clear();
        _statusComputed = false;
        foreach (var se in SourceEntries) se.IsApplied = false;
        if (!string.IsNullOrWhiteSpace(ProjectPath)) ShowStatus();
        else { UpdateEmptyHint(); RefreshActionTexts(); ComposeStatus(); }
    }

    // —— 核心操作 ——

    private void Apply() => ApplyNames(ScopedNames());

    /// <summary>对指定条目名集合执行应用(names 为 null = 全部).右键单条与底部按钮共用.</summary>
    private void ApplyNames(HashSet<string>? names)
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) { AppendLog("请先选择目标目录。"); return; }
        if (names is not null && names.Count == 0) return;
        if (!TryEngine(out TargetEngine? engine)) return;

        // 强制覆盖旧链接属危险动作 → 二次确认
        if (Force && !Confirm($"强制应用将覆盖目标中的旧链接({ScopeWord(names)} {ScopeCount(names)} 条)。\n真实文件/目录永不覆盖。确定继续?"))
            return;
        try
        {
            ApplyOutcome r = engine!.Apply(ProjectPath, Force, names);
            if (r.Entries.Count == 0) AppendLog("没有可链接的条目。");
            foreach (var e in r.Entries) AppendLog(Describe(e));
            if (r.Entries.Count > 0)
            {
                string msg = $"应用完成:新建 {r.Created},跳过 {r.Skipped},失败 {r.Failed}";
                AppendLog(msg);
                ShowTransientStatus(msg, isError: false);
            }
            if (Force) Force = false; // 不粘滞
            ShowStatus();
            RefreshTargets(engine);
        }
        catch (Exception ex) { AppendError(ex.Message); }
    }

    private void ShowStatus()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) { ClearStatus(); return; }
        if (!TryEngine(out TargetEngine? engine)) return;
        try
        {
            StatusOutcome r = engine!.Status(ProjectPath);
            Entries.Clear();
            foreach (var e in r.Entries) Entries.Add(ToRow(e));
            _entriesView.Refresh();

            // 左侧「已应用」标记:正常链接的名字集合
            var applied = new HashSet<string>(
                r.Entries.Where(e => e.Status == LinkStatus.Ok).Select(e => e.Target),
                StringComparer.OrdinalIgnoreCase);
            foreach (var se in SourceEntries) se.IsApplied = applied.Contains(se.Name);

            UpdateStatusCounts(r.Entries);
            UpdateEmptyHint();
            RefreshActionTexts();

            if (r.Entries.Count == 0)
                AppendLog("该目标没有任何链接记录。");
        }
        catch (Exception ex) { AppendError(ex.Message); }
    }

    private void Remove() => RemoveNames(ScopedNames());

    /// <summary>对指定条目名集合执行移除(names 为 null = 全部).右键单条与底部按钮共用.</summary>
    private void RemoveNames(HashSet<string>? names)
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) { AppendLog("请先选择目标目录。"); return; }
        if (names is not null && names.Count == 0) return;
        if (!TryEngine(out TargetEngine? engine)) return;

        int affected = ScopeCount(names);
        if (affected == 0) { AppendLog("没有可移除的链接记录。"); return; }
        if (!Confirm($"确定移除{ScopeWord(names)} {affected} 条链接记录?\n只删本工具创建的链接,真实文件/目录永不删除。"))
            return;
        try
        {
            RemoveOutcome r = engine!.Remove(ProjectPath, Force, names);
            if (r.Items.Count == 0) AppendLog("没有可移除的链接记录。");
            foreach (var it in r.Items) AppendLog(Describe(it));
            if (r.Items.Count > 0)
            {
                string msg = $"移除完成:移除 {r.Removed},保留 {r.Kept}";
                AppendLog(msg);
                ShowTransientStatus(msg, isError: false);
            }
            if (Force) Force = false; // 不粘滞
            ShowStatus();
            RefreshTargets(engine);
        }
        catch (Exception ex) { AppendError(ex.Message); }
    }

    // —— 单条应用 / 移除(右键菜单;左右两侧共用 ApplyNames/RemoveNames) ——

    /// <summary>是否已选定目标目录(单条操作的前提).</summary>
    private bool HasTarget => !string.IsNullOrWhiteSpace(ProjectPath);

    private static HashSet<string> One(string name) => new(new[] { name }, StringComparer.OrdinalIgnoreCase);

    private void ApplySourceEntry() { if (SelectedSourceEntry is { } s) ApplyNames(One(s.Name)); }
    private void RemoveSourceEntry() { if (SelectedSourceEntry is { } s) RemoveNames(One(s.Name)); }
    private void ApplyEntry() { if (SelectedEntry is { } e) ApplyNames(One(e.Name)); }
    private void RemoveEntry() { if (SelectedEntry is { } e) RemoveNames(One(e.Name)); }

    /// <summary>清空右表与左侧标记(目标为空时).</summary>
    private void ClearStatus()
    {
        Entries.Clear();
        foreach (var se in SourceEntries) se.IsApplied = false;
        _statusComputed = false;
        UpdateEmptyHint();
        RefreshActionTexts();
        ComposeStatus();
    }

    // —— 选中范围(null = 全部) ——

    /// <summary>当前操作范围的条目名集合:有多选则取选中,否则 null(=全部).</summary>
    private HashSet<string>? ScopedNames()
        => SelectedEntryNames.Count > 0 ? new HashSet<string>(SelectedEntryNames, StringComparer.OrdinalIgnoreCase) : null;

    private string ScopeWord(HashSet<string>? names) => names is null ? "全部" : "选中的";
    private int ScopeCount(HashSet<string>? names) => names?.Count ?? Entries.Count;

    private static bool Confirm(string message)
        => MessageBox.Show(message, "确认操作", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    // —— 过滤逻辑 ——

    /// <summary>右侧 DataGrid 过滤谓词:搜索文本 + 类型过滤.</summary>
    private bool EntriesFilter(object item)
    {
        if (item is not EntryRow row) return false;
        if (!string.IsNullOrEmpty(_searchText) &&
            !row.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_activeTypeFilter != TypeFilter.All)
        {
            LinkType expected = _activeTypeFilter == TypeFilter.Dir ? LinkType.SymlinkDir : LinkType.SymlinkFile;
            if (row.LinkType != expected) return false;
        }
        return true;
    }

    /// <summary>左侧源条目过滤谓词:仅搜索文本(与右侧联动).</summary>
    private bool SourceEntriesFilter(object item)
        => item is SourceEntryRow row &&
           (string.IsNullOrEmpty(_searchText) || row.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

    // —— 状态栏 ——

    private void UpdateStatusCounts(IReadOnlyList<EntryStatus> entries)
    {
        _cntTotal = entries.Count;
        _cntOk = _cntMissing = _cntNotLink = _cntWrong = 0;
        foreach (var e in entries)
        {
            switch (e.Status)
            {
                case LinkStatus.Ok: _cntOk++; break;
                case LinkStatus.Missing: _cntMissing++; break;
                case LinkStatus.NotALink: _cntNotLink++; break;
                case LinkStatus.WrongTarget: _cntWrong++; break;
            }
        }
        _statusComputed = true;
        ComposeStatus();
    }

    /// <summary>组合状态栏两侧概览:源侧(总计/已启用/未启用)+ 目标侧(链接健康度).</summary>
    private void ComposeStatus()
    {
        int total = SourceEntries.Count;
        string src;
        if (total == 0)
            src = "源目录暂无可链接条目";
        else if (string.IsNullOrWhiteSpace(ProjectPath))
            src = $"源目录总计 {total} 条";
        else
        {
            int enabled = SourceEntries.Count(se => se.IsApplied);
            src = $"源目录总计 {total} 条，已启用 {enabled} 条，未启用 {total - enabled} 条";
        }
        StatusLeftBrush = NormalStatusBrush;
        StatusLeftText = src;
        StatusTargetText = ComposeTargetSummary();
    }

    private string ComposeTargetSummary()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) return "未选择目标目录";
        if (!_statusComputed) return "";
        if (_cntTotal == 0) return "目标目录暂无链接记录";
        if (_cntOk == _cntTotal) return "目标目录全部链接正常";

        var parts = new List<string>();
        if (_cntOk > 0) parts.Add($"{_cntOk} 正常");
        if (_cntMissing > 0) parts.Add($"{_cntMissing} 缺失");
        if (_cntNotLink > 0) parts.Add($"{_cntNotLink} 失效");
        if (_cntWrong > 0) parts.Add($"{_cntWrong} 指向变了");
        return "目标目录:" + string.Join(" / ", parts);
    }

    /// <summary>临时显示操作结果/错误摘要 5 秒,然后恢复计数.</summary>
    private void ShowTransientStatus(string message, bool isError)
    {
        StatusLeftBrush = isError ? ErrorStatusBrush : NormalStatusBrush;
        StatusLeftText = message;
        _transientTimer?.Stop();
        _transientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _transientTimer = null;
            ComposeStatus(); // 恢复概览
        };
        _transientTimer.Start();
    }

    private void UpdateEmptyHint()
    {
        EmptyHint = Entries.Count > 0
            ? ""
            : string.IsNullOrWhiteSpace(ProjectPath)
                ? "输入或选择目标目录,查看链接现状"
                : "该目标暂无链接记录 —— 点「应用」创建链接";
    }

    // —— 右键菜单命令 ——

    private void CopyTargetPath()
    {
        if (SelectedEntry is not null)
            try { Clipboard.SetText(SelectedEntry.Target); } catch { }
    }

    private void CopySourcePath()
    {
        if (SelectedEntry is not null)
            try { Clipboard.SetText(SelectedEntry.ExpectedSource); } catch { }
    }

    private void CopyEntryName()
    {
        if (SelectedEntry is not null)
            try { Clipboard.SetText(SelectedEntry.Name); } catch { }
    }

    private void CopyAllStatus()
    {
        var lines = new List<string> { "名称\t类型\t源路径\t状态" };
        foreach (EntryRow row in _entriesView)
            lines.Add($"{row.Name}\t{row.TypeDisplay}\t{row.ExpectedSource}\t{row.StatusText}");
        try { Clipboard.SetText(string.Join("\n", lines)); } catch { }
    }

    private void OpenInExplorer()
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(ProjectPath)) return;
        string targetPath = Path.GetFullPath(Path.Combine(ProjectPath, SelectedEntry.Target));
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer", $"/select,\"{targetPath}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { AppendError("无法打开资源管理器:" + ex.Message); }
        }
    }

    private void CopySourceEntryName()
    {
        if (SelectedSourceEntry is not null)
            try { Clipboard.SetText(SelectedSourceEntry.Name); } catch { }
    }

    private void CopySourceEntrySource()
    {
        if (SelectedSourceEntry is not null)
            try { Clipboard.SetText(SelectedSourceEntry.SourcePath); } catch { }
    }

    /// <summary>在资源管理器中定位源条目的源路径.</summary>
    private void OpenSourceEntryInExplorer()
    {
        if (SelectedSourceEntry is null) return;
        string p = SelectedSourceEntry.SourcePath;
        if (File.Exists(p) || Directory.Exists(p))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer", $"/select,\"{p}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { AppendError("无法打开资源管理器:" + ex.Message); }
        }
    }

    // —— 辅助 ——

    private bool TryEngine(out TargetEngine? engine)
    {
        try { engine = TargetEngine.Open(); return true; }
        catch (Exception ex) { engine = null; AppendError(ex.Message); return false; }
    }

    private void RefreshTargets(TargetEngine engine)
    {
        var fresh = engine.TargetsForCurrentSource().ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 差量同步:只删不在 fresh 里的、只加不在旧列表里的 —— 不 Clear(),避免 WPF 重置 ComboBox 的 Text 绑定
        for (int i = TargetsForSource.Count - 1; i >= 0; i--)
            if (!fresh.Contains(TargetsForSource[i])) TargetsForSource.RemoveAt(i);
        foreach (var p in fresh)
            if (!TargetsForSource.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                TargetsForSource.Add(p);
    }

    private static bool SamePath(string a, string b)
    {
        try { return DataStore.NormalizeTargetRoot(a) == DataStore.NormalizeTargetRoot(b); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private EntryRow ToRow(EntryStatus e)
    {
        (string text, Brush brush, Brush bg, int severity) = e.Status switch
        {
            LinkStatus.Ok => ("正常", Brushes.Green, Brushes.Transparent, 0),
            LinkStatus.Missing => ("缺失", Brushes.Red, new SolidColorBrush(Color.FromArgb(20, 255, 0, 0)), 3),
            LinkStatus.NotALink => ("失效(已非链接)", Brushes.DarkOrange, new SolidColorBrush(Color.FromArgb(20, 255, 165, 0)), 2),
            LinkStatus.WrongTarget => ("指向变了", Brushes.DarkOrange, new SolidColorBrush(Color.FromArgb(20, 255, 165, 0)), 1),
            _ => ("?", Brushes.Black, Brushes.Transparent, 99),
        };

        _sourceLinkTypes.TryGetValue(e.Target, out var linkType);
        string typeDisplay = linkType == LinkType.SymlinkDir ? "目录" : "文件";

        return new EntryRow
        {
            Target = e.Target,
            Name = e.Target,
            TypeDisplay = typeDisplay,
            LinkType = linkType,
            ExpectedSource = e.ExpectedSource,
            StatusText = text,
            StatusBrush = brush,
            RowBackground = bg,
            StatusSeverity = severity,
        };
    }

    private static string Describe(EntryOutcome e) => e.Action switch
    {
        EntryAction.Created => $"[新建] {e.Target} → {e.Message}",
        EntryAction.Skipped => $"[跳过] {e.Target}({e.Message})",
        EntryAction.ConflictLink => $"[冲突] {e.Target} {e.Message}",
        EntryAction.ConflictReal => $"[冲突] {e.Target} {e.Message}",
        EntryAction.Failed => $"[失败] {e.Target}:{e.Message}",
        _ => e.Target,
    };

    private static string Describe(RemoveItem it) => it.Action switch
    {
        RemoveAction.Removed => $"[移除] {it.Target}",
        RemoveAction.RecordCleared => $"[清理] {it.Target}(已不存在)",
        RemoveAction.KeptWrongTarget => $"[保留] {it.Target}(指向已变;勾选[强制]可强删)",
        RemoveAction.KeptReal => $"[保留] {it.Target}(真实文件/目录,拒绝删除)",
        _ => it.Target,
    };

    // —— 日志 ——

    private void AppendLog(string line)
    {
        _logLines.Add(line);
        if (_logLines.Count > MaxLogLines)
            _logLines.RemoveRange(0, _logLines.Count - MaxLogLines); // 环形裁剪
        Log = string.Join("\n", _logLines);
    }

    /// <summary>记录错误:写日志 + 展开日志面板 + 状态栏红字提示(确保可见).</summary>
    private void AppendError(string message)
    {
        AppendLog("[错误] " + message);
        LogExpanded = true;
        ShowTransientStatus("⚠ " + message, isError: true);
    }

    private void ClearLog()
    {
        _logLines.Clear();
        Log = "";
    }
}
