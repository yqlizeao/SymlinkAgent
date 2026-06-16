using SymlinkAgent.Model;

namespace SymlinkAgent.Core;

// —— 结构化结果(无任何 Console/UI 依赖,供 CLI 与 GUI 共同渲染)——

/// <summary>apply 单条结果动作。</summary>
public enum EntryAction { Created, Skipped, ConflictLink, ConflictReal, Failed }

/// <summary>apply 单条结果。Message 为可读详情(源路径 / 跳过原因 / 错误)。</summary>
public sealed record EntryOutcome(string Target, EntryAction Action, string Message);

/// <summary>apply 汇总结果。</summary>
public sealed record ApplyOutcome(IReadOnlyList<EntryOutcome> Entries, int Created, int Skipped, int Failed);

/// <summary>remove 单条结果动作。</summary>
public enum RemoveAction { Removed, RecordCleared, KeptWrongTarget, KeptReal }

/// <summary>remove 单条结果。</summary>
public sealed record RemoveItem(string Target, RemoveAction Action);

/// <summary>remove 汇总结果。</summary>
public sealed record RemoveOutcome(IReadOnlyList<RemoveItem> Items, int Removed, int Kept);

/// <summary>status 单条结果。</summary>
public sealed record EntryStatus(string Target, LinkStatus Status, string ExpectedSource);

/// <summary>status 汇总结果。</summary>
public sealed record StatusOutcome(string TargetRoot, string SourceRoot, IReadOnlyList<EntryStatus> Entries);

/// <summary>
/// 目标引擎:把"目标应用 / 一键移除 / 目标检查"的编排集中于此,返回结构化结果。
/// CLI 与 GUI 都是它的薄壳 —— 满足需求责任制(每项需求只有一处实现)。
/// 链接条目由 SourceScanner 从源文件夹结构自动推导(无 manifest、无 profile)。
/// 记录持久化经 DataStore(单一 INI 文件)。所有安全规则都在这里。
/// </summary>
public sealed class TargetEngine
{
    private readonly Workspace _ws;

    public TargetEngine(Workspace ws) => _ws = ws;

    /// <summary>按数据文件打开引擎;未设置源目录或源目录不可达时抛异常。</summary>
    public static TargetEngine Open() => new(Workspace.Open());

    public string SourceRoot => _ws.SourceRoot;

    /// <summary>列出源顶层将被链接的条目(供预览/列表)。</summary>
    public IReadOnlyList<LinkSpec> SourceEntries() => _ws.Source.Scan();

    /// <summary>列出已应用过的目标记录。</summary>
    public IReadOnlyList<TargetRecord> ListTargets() => _ws.Data.Targets;

    /// <summary>列出在当前源目录下应用过的目标目录(供 GUI 下拉)。</summary>
    public IReadOnlyList<string> TargetsForCurrentSource() => _ws.Data.TargetRootsForSource(_ws.SourceRoot);

    // —————————————————————————— apply ——————————————————————————

    /// <summary>
    /// 把源顶层条目链接到目标目录;幂等、冲突保护。
    /// names 为 null 时处理全部条目;非 null 时只处理名字在集合内的条目(GUI 选中行驱动)。
    /// </summary>
    public ApplyOutcome Apply(string targetRoot, bool force, IReadOnlySet<string>? names = null)
    {
        targetRoot = Path.GetFullPath(targetRoot);
        if (!Directory.Exists(targetRoot))
            throw new DirectoryNotFoundException($"目标目录不存在:{targetRoot}");

        IReadOnlyList<LinkSpec> specs = _ws.Source.Scan();

        TargetRecord rec = _ws.Data.GetTarget(targetRoot) ?? new TargetRecord { TargetRoot = targetRoot };
        rec.SourceRoot = _ws.SourceRoot;

        var outcomes = new List<EntryOutcome>();
        int created = 0, skipped = 0, failed = 0;

        foreach (LinkSpec spec in specs)
        {
            if (names is not null && !names.Contains(spec.Target)) continue; // 仅处理选中条目
            string source = spec.Source;
            string target = Path.GetFullPath(Path.Combine(targetRoot, spec.Target));

            bool isLink = _ws.Links.IsLink(target);
            bool exists = isLink || File.Exists(target) || Directory.Exists(target);

            // 幂等:已是指向预期源的链接 → 跳过
            if (isLink && Verifier.SamePath(_ws.Links.ResolveTarget(target) ?? "", source))
            {
                UpsertEntry(rec, spec, target, source);
                outcomes.Add(new(spec.Target, EntryAction.Skipped, "已应用"));
                skipped++;
                continue;
            }

            if (exists)
            {
                if (isLink)
                {
                    if (!force)
                    {
                        outcomes.Add(new(spec.Target, EntryAction.ConflictLink, "已是其它链接;加 force 可覆盖"));
                        failed++;
                        continue;
                    }
                    _ws.Links.Remove(spec.LinkType, target); // 仅覆盖旧链接(安全)
                }
                else
                {
                    outcomes.Add(new(spec.Target, EntryAction.ConflictReal, "是真实文件/目录,拒绝覆盖"));
                    failed++;
                    continue;
                }
            }

            try
            {
                _ws.Links.Create(spec.LinkType, target, source);
                if (spec.SvnIgnore)
                    _ws.Svn.AddIgnore(Path.GetDirectoryName(target)!, Path.GetFileName(target));
                UpsertEntry(rec, spec, target, source);
                outcomes.Add(new(spec.Target, EntryAction.Created, source));
                created++;
            }
            catch (Exception ex)
            {
                outcomes.Add(new(spec.Target, EntryAction.Failed, ex.Message));
                failed++;
            }
        }

        _ws.Data.UpsertTarget(rec); // 持久化(含保存)
        return new ApplyOutcome(outcomes, created, skipped, failed);
    }

    // —————————————————————————— remove ——————————————————————————

    /// <summary>
    /// 按记录删除链接;删前核查、真实数据永不删。
    /// names 为 null 时处理全部记录;非 null 时只处理名字在集合内的记录(GUI 选中行驱动)。
    /// </summary>
    public RemoveOutcome Remove(string targetRoot, bool force, IReadOnlySet<string>? names = null)
    {
        targetRoot = Path.GetFullPath(targetRoot);
        TargetRecord? rec = _ws.Data.GetTarget(targetRoot);
        if (rec is null || rec.Entries.Count == 0)
            return new RemoveOutcome(Array.Empty<RemoveItem>(), 0, 0);

        var items = new List<RemoveItem>();
        int removed = 0, kept = 0;

        foreach (StateEntry entry in rec.Entries.ToList())
        {
            if (names is not null && !names.Contains(entry.Target)) continue; // 仅处理选中条目
            switch (_ws.Verifier.Verify(entry))
            {
                case LinkStatus.Ok:
                    DeleteLink(entry);
                    items.Add(new(entry.Target, RemoveAction.Removed));
                    rec.Entries.Remove(entry);
                    removed++;
                    break;

                case LinkStatus.Missing:
                    items.Add(new(entry.Target, RemoveAction.RecordCleared));
                    rec.Entries.Remove(entry);
                    break;

                case LinkStatus.WrongTarget:
                    if (force)
                    {
                        DeleteLink(entry);
                        items.Add(new(entry.Target, RemoveAction.Removed));
                        rec.Entries.Remove(entry);
                        removed++;
                    }
                    else
                    {
                        items.Add(new(entry.Target, RemoveAction.KeptWrongTarget));
                        kept++;
                    }
                    break;

                case LinkStatus.NotALink:
                    items.Add(new(entry.Target, RemoveAction.KeptReal));
                    kept++;
                    break;
            }
        }

        if (rec.Entries.Count == 0) _ws.Data.RemoveTarget(targetRoot);
        else _ws.Data.UpsertTarget(rec);

        return new RemoveOutcome(items, removed, kept);
    }

    // —————————————————————————— status ——————————————————————————

    /// <summary>核查目标链接状态;无记录时返回空 Entries。</summary>
    public StatusOutcome Status(string targetRoot)
    {
        targetRoot = Path.GetFullPath(targetRoot);
        TargetRecord? rec = _ws.Data.GetTarget(targetRoot);
        if (rec is null)
            return new StatusOutcome(targetRoot, "", Array.Empty<EntryStatus>());

        var entries = rec.Entries
            .Select(e => new EntryStatus(e.Target, _ws.Verifier.Verify(e), e.ExpectedSource))
            .ToList();

        return new StatusOutcome(rec.TargetRoot, rec.SourceRoot, entries);
    }

    // —————————————————————————— 内部 ——————————————————————————

    private void DeleteLink(StateEntry entry)
    {
        _ws.Links.Remove(entry.LinkType, entry.AbsoluteTarget);
        _ws.Svn.RemoveIgnore(Path.GetDirectoryName(entry.AbsoluteTarget)!, Path.GetFileName(entry.AbsoluteTarget));
    }

    private static void UpsertEntry(TargetRecord rec, LinkSpec spec, string target, string source)
    {
        rec.Entries.RemoveAll(e => Verifier.SamePath(e.AbsoluteTarget, target));
        rec.Entries.Add(new StateEntry
        {
            Target = spec.Target,
            AbsoluteTarget = target,
            ExpectedSource = source,
            Type = LinkTypeParser.ToToken(spec.LinkType),
        });
    }
}
