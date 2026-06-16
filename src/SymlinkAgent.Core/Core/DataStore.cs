using SymlinkAgent.Model;

namespace SymlinkAgent.Core;

/// <summary>某目标的链接记录(来源源目录 + 已创建的条目)。</summary>
public sealed class TargetRecord
{
    public string TargetRoot { get; set; } = string.Empty;
    public string SourceRoot { get; set; } = string.Empty;
    public List<StateEntry> Entries { get; set; } = new();
}

/// <summary>
/// 数据仓库:把"当前源目录 + 历史源目录 + 各目标链接记录"全部存于**单个 INI 文件**
/// (&lt;工具目录&gt;/data/symlinkagent.ini),取代原先的 config.json 与 state/*.json。
/// 绿色便携、原子写入;损坏时尽量容错(读不出的部分忽略)。
/// 责任:配置/历史/状态的统一持久化与查询。
/// </summary>
public sealed class DataStore
{
    private const string SourcesSection = "sources";
    private const string TargetPrefix = "target:";

    // 旧格式兼容
    private const string LegacyHubsSection = "hubs";
    private const string LegacyProjectPrefix = "project:";

    /// <summary>当前激活的源目录。</summary>
    public string? CurrentSource { get; set; }

    /// <summary>历史使用过的源目录(最近在前)。</summary>
    public List<string> RecentSources { get; } = new();

    /// <summary>各目标链接记录。</summary>
    public List<TargetRecord> Targets { get; } = new();

    // —————————————————————————— 读 / 写 ——————————————————————————

    public static DataStore Load()
    {
        var store = new DataStore();
        string path = AppPaths.DataFile;
        if (!File.Exists(path)) return store;

        IniFile ini;
        try { ini = IniFile.Parse(File.ReadAllText(path)); }
        catch { return store; } // 损坏 → 视为空,不崩

        // 优先读新格式 [sources],回退到旧格式 [hubs]
        IniFile.Section? sources = ini.Get(SourcesSection) ?? ini.Get(LegacyHubsSection);
        if (sources is not null)
        {
            store.CurrentSource = sources.First("current");
            foreach (string h in sources.All("recent"))
                if (!string.IsNullOrWhiteSpace(h)) store.RecentSources.Add(h);
        }

        foreach (IniFile.Section sec in ini.Sections)
        {
            // 兼容新格式 [target:xxx] 和旧格式 [project:xxx]
            string targetRoot;
            if (sec.Name.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase))
                targetRoot = sec.Name[TargetPrefix.Length..];
            else if (sec.Name.StartsWith(LegacyProjectPrefix, StringComparison.OrdinalIgnoreCase))
                targetRoot = sec.Name[LegacyProjectPrefix.Length..];
            else
                continue;

            // 兼容新格式 source= 和旧格式 hub=
            string sourceRoot = sec.First("source") ?? sec.First("hub") ?? string.Empty;

            var rec = new TargetRecord
            {
                TargetRoot = targetRoot,
                SourceRoot = sourceRoot,
            };
            foreach (string link in sec.All("link"))
            {
                // 格式:target|type|expectedSource
                string[] p = link.Split('|');
                if (p.Length < 3) continue;
                rec.Entries.Add(new StateEntry
                {
                    Target = p[0],
                    Type = p[1],
                    ExpectedSource = p[2],
                    AbsoluteTarget = Path.GetFullPath(Path.Combine(targetRoot, p[0])),
                });
            }
            store.Targets.Add(rec);
        }
        return store;
    }

    /// <summary>原子写入(先写临时文件再替换),避免中途损坏。</summary>
    public void Save()
    {
        var ini = new IniFile();

        IniFile.Section sources = ini.GetOrAdd(SourcesSection);
        if (!string.IsNullOrWhiteSpace(CurrentSource)) sources.Add("current", CurrentSource!);
        foreach (string h in RecentSources) sources.Add("recent", h);

        foreach (TargetRecord rec in Targets)
        {
            IniFile.Section sec = ini.GetOrAdd(TargetPrefix + rec.TargetRoot);
            sec.Add("source", rec.SourceRoot);
            foreach (StateEntry e in rec.Entries)
                sec.Add("link", $"{e.Target}|{e.Type}|{e.ExpectedSource}");
        }

        Directory.CreateDirectory(AppPaths.DataDir);
        string path = AppPaths.DataFile;
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ini.ToString());
        File.Move(tmp, path, overwrite: true);
    }

    // —————————————————————————— 源目录 ——————————————————————————

    /// <summary>设为当前源目录并加入历史(最近在前),随即持久化。</summary>
    public void SetCurrentSource(string sourceRoot)
    {
        string full = Path.GetFullPath(sourceRoot);
        CurrentSource = full;
        RecentSources.RemoveAll(h => SamePath(h, full));
        RecentSources.Insert(0, full);
        Save();
    }

    // —————————————————————————— 目标 ——————————————————————————

    public TargetRecord? GetTarget(string targetRoot)
        => Targets.FirstOrDefault(p => SamePath(p.TargetRoot, targetRoot));

    public void UpsertTarget(TargetRecord rec)
    {
        Targets.RemoveAll(p => SamePath(p.TargetRoot, rec.TargetRoot));
        Targets.Add(rec);
        Save();
    }

    public void RemoveTarget(string targetRoot)
    {
        Targets.RemoveAll(p => SamePath(p.TargetRoot, targetRoot));
        Save();
    }

    /// <summary>列出在指定源目录下应用过的目标目录(供 GUI 下拉)。</summary>
    public IReadOnlyList<string> TargetRootsForSource(string sourceRoot)
        => Targets.Where(p => SamePath(p.SourceRoot, sourceRoot))
                   .Select(p => p.TargetRoot)
                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                   .ToList();

    // —————————————————————————— 辅助 ——————————————————————————

    /// <summary>规范化目标根路径:绝对、去尾分隔符、小写(Windows 大小写不敏感)。</summary>
    public static string NormalizeTargetRoot(string targetRoot)
        => Path.GetFullPath(targetRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

    private static bool SamePath(string a, string b)
    {
        try { return NormalizeTargetRoot(a) == NormalizeTargetRoot(b); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// 校验源路径是否为本地真实可达目录(管理员进程看不到映射网络盘)。
    /// </summary>
    public static void ValidateSourceRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new InvalidOperationException("源路径为空。");

        string full = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(
                $"源目录不存在或不可达:{full}\n" +
                "(注意:本程序以管理员身份运行,看不到映射的网络盘;请把源放在本地真实盘,如 C:\\agent-source)");

        string root = Path.GetPathRoot(full) ?? string.Empty;
        if (new DriveInfo(root).DriveType == DriveType.Network)
            throw new InvalidOperationException($"源目录位于网络盘({root}),管理员进程可能无法访问。请改用本地真实盘。");
    }
}
