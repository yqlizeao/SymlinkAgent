using SymlinkAgent.Cli;
using SymlinkAgent.Core;

namespace SymlinkAgent.Commands;

/// <summary>
/// apply:薄壳 —— 调 TargetEngine.Apply(把源顶层条目全部链接进目标目录)。
/// 用法:SymlinkAgent apply &lt;目标目录&gt; [--force]
/// </summary>
public sealed class ApplyCommand : ICommand
{
    public string Name => "apply";
    public string Summary => "把源目录内容链接到目标目录";

    public int Execute(CommandContext ctx)
    {
        string? project = ctx.Args.Positional(0);
        bool force = ctx.Args.HasFlag("force");

        if (string.IsNullOrWhiteSpace(project))
        {
            ctx.Error("用法:SymlinkAgent apply <目标目录> [--force]");
            return 1;
        }

        try
        {
            ApplyOutcome r = TargetEngine.Open().Apply(project, force);
            if (r.Entries.Count == 0)
            {
                ctx.Warn("源顶层没有可链接的条目。");
                return 0;
            }
            foreach (EntryOutcome e in r.Entries)
            {
                switch (e.Action)
                {
                    case EntryAction.Created: ctx.Ok($"{e.Target} → {e.Message}"); break;
                    case EntryAction.Skipped: ctx.Skip($"{e.Target}({e.Message})"); break;
                    case EntryAction.ConflictLink:
                    case EntryAction.ConflictReal: ctx.Error($"冲突:{e.Target} {e.Message}"); break;
                    case EntryAction.Failed: ctx.Error($"{e.Target} 创建失败:{e.Message}"); break;
                }
            }
            ctx.Info($"完成:新建 {r.Created},跳过 {r.Skipped},失败 {r.Failed}。");
            return r.Failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }
    }
}
