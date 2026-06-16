using SymlinkAgent.Cli;
using SymlinkAgent.Core;

namespace SymlinkAgent.Commands;

/// <summary>
/// remove:薄壳 —— 调 TargetEngine.Remove,再渲染结果。
/// 用法:SymlinkAgent remove &lt;目标目录&gt; [--force]
/// </summary>
public sealed class RemoveCommand : ICommand
{
    public string Name => "remove";
    public string Summary => "移除目标中本工具创建的链接";

    public int Execute(CommandContext ctx)
    {
        string? project = ctx.Args.Positional(0);
        bool force = ctx.Args.HasFlag("force");

        if (string.IsNullOrWhiteSpace(project))
        {
            ctx.Error("用法:SymlinkAgent remove <目标目录> [--force]");
            return 1;
        }

        try
        {
            RemoveOutcome r = TargetEngine.Open().Remove(project, force);
            if (r.Items.Count == 0)
            {
                ctx.Warn("该目标没有可移除的链接记录。");
                return 0;
            }

            foreach (RemoveItem it in r.Items)
            {
                switch (it.Action)
                {
                    case RemoveAction.Removed: ctx.Ok($"{it.Target}(已移除)"); break;
                    case RemoveAction.RecordCleared: ctx.Skip($"{it.Target}(已不存在,清理记录)"); break;
                    case RemoveAction.KeptWrongTarget: ctx.Warn($"{it.Target}(指向已变,跳过;--force 可强删该链接)"); break;
                    case RemoveAction.KeptReal: ctx.Warn($"{it.Target}(已是真实文件/目录,拒绝删除)"); break;
                }
            }
            ctx.Info($"完成:移除 {r.Removed},保留 {r.Kept}。");
            return 0;
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }
    }
}
