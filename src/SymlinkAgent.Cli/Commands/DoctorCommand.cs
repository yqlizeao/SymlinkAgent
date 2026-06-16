using SymlinkAgent.Cli;
using SymlinkAgent.Core;
using SymlinkAgent.Model;

namespace SymlinkAgent.Commands;

/// <summary>
/// doctor:当记录缺失或目标被手动改动时,扫描目标目录并给出诊断/修复。
/// 用法:SymlinkAgent doctor &lt;目标目录&gt; [--fix]
/// 默认只读诊断;加 --fix 才落地修复(重建缺失记录、清理失效记录)。
/// </summary>
public sealed class DoctorCommand : ICommand
{
    public string Name => "doctor";
    public string Summary => "诊断并(可选)修复目标链接状态";

    public int Execute(CommandContext ctx)
    {
        string? project = ctx.Args.Positional(0);
        bool fix = ctx.Args.HasFlag("fix");
        if (string.IsNullOrWhiteSpace(project))
        {
            ctx.Error("用法:SymlinkAgent doctor <目标目录> [--fix]");
            return 1;
        }

        Workspace ws;
        string projectRoot;
        try
        {
            ws = Workspace.Open();
            projectRoot = Path.GetFullPath(project);
            if (!Directory.Exists(projectRoot))
            {
                ctx.Error($"目标目录不存在:{projectRoot}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }

        TargetRecord? rec = ws.Data.GetTarget(projectRoot);

        // 情况一:记录缺失 → 扫描目标顶层、找出指向源的链接,(可)重建记录
        if (rec is null)
        {
            ctx.Warn("未找到链接记录,扫描目标目录中指向源的链接……");
            var found = ScanSourceLinks(ws, projectRoot);
            if (found.Count == 0)
            {
                ctx.Info("未发现指向源的链接,无需重建。");
                return 0;
            }

            foreach (var e in found) ctx.Info($"  发现:{e.Target} → {e.ExpectedSource}");

            if (fix)
            {
                ws.Data.UpsertTarget(new TargetRecord
                {
                    TargetRoot = projectRoot,
                    SourceRoot = ws.SourceRoot,
                    Entries = found,
                });
                ctx.Ok($"已重建记录,共 {found.Count} 条。");
            }
            else
            {
                ctx.Info("加 --fix 可据此重建记录。");
            }
            return 0;
        }

        // 情况二:记录存在 → 逐条核查并报告;--fix 清理 Missing 记录
        int abnormal = 0;
        var toDrop = new List<StateEntry>();
        foreach (StateEntry entry in rec.Entries)
        {
            LinkStatus status = ws.Verifier.Verify(entry);
            if (status == LinkStatus.Ok) { ctx.Ok($"{entry.Target}"); continue; }

            abnormal++;
            switch (status)
            {
                case LinkStatus.Missing:
                    ctx.Error($"{entry.Target}  缺失" + (fix ? " → 清理记录" : " (--fix 清理记录)"));
                    if (fix) toDrop.Add(entry);
                    break;
                case LinkStatus.NotALink:
                    ctx.Warn($"{entry.Target}  已是真实文件/目录(不自动处理,请手动确认)");
                    break;
                case LinkStatus.WrongTarget:
                    ctx.Warn($"{entry.Target}  指向变了 → 期望 {entry.ExpectedSource}(可 remove --force 后重 apply)");
                    break;
            }
        }

        if (fix && toDrop.Count > 0)
        {
            foreach (var e in toDrop) rec.Entries.Remove(e);
            if (rec.Entries.Count == 0) ws.Data.RemoveTarget(projectRoot);
            else ws.Data.UpsertTarget(rec);
            ctx.Ok($"已清理 {toDrop.Count} 条失效记录。");
        }

        ctx.Info(abnormal == 0 ? "状态健康。" : $"发现 {abnormal} 处异常。");
        return abnormal == 0 ? 0 : 2;
    }

    /// <summary>扫描目标顶层,返回指向源内部的符号链接。</summary>
    private static List<StateEntry> ScanSourceLinks(Workspace ws, string projectRoot)
    {
        var result = new List<StateEntry>();
        foreach (string path in Directory.EnumerateFileSystemEntries(projectRoot))
        {
            if (!ws.Links.IsLink(path)) continue;
            string? resolved = ws.Links.ResolveTarget(path);
            if (resolved is null) continue;

            if (!resolved.StartsWith(ws.SourceRoot, StringComparison.OrdinalIgnoreCase)) continue;

            LinkType type = Directory.Exists(path) ? LinkType.SymlinkDir : LinkType.SymlinkFile;
            result.Add(new StateEntry
            {
                Target = Path.GetFileName(path),
                AbsoluteTarget = path,
                ExpectedSource = resolved,
                Type = LinkTypeParser.ToToken(type),
            });
        }
        return result;
    }
}
