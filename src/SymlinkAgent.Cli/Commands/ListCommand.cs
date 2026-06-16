using SymlinkAgent.Cli;
using SymlinkAgent.Core;
using SymlinkAgent.Model;

namespace SymlinkAgent.Commands;

/// <summary>
/// list:薄壳 —— 列出源顶层将链接的条目,以及已应用目标。
/// 用法:SymlinkAgent list
/// </summary>
public sealed class ListCommand : ICommand
{
    public string Name => "list";
    public string Summary => "列出源内容与已应用目标";

    public int Execute(CommandContext ctx)
    {
        try
        {
            TargetEngine engine = TargetEngine.Open();
            ctx.Info($"源:{engine.SourceRoot}");

            ctx.Info("\n将链接的条目(源顶层):");
            var entries = engine.SourceEntries();
            if (entries.Count == 0)
                ctx.Skip("  (无,请把需要链接的文件/目录直接放进源根目录)");
            else
                foreach (var e in entries)
                    ctx.Info($"  - {e.Name}  [{(e.LinkType == LinkType.SymlinkDir ? "目录" : "文件")}]");

            ctx.Info("\n已应用目标:");
            var projects = engine.ListTargets();
            if (projects.Count == 0)
                ctx.Skip("  (无)");
            else
                foreach (var s in projects)
                    ctx.Info($"  - {s.TargetRoot}  ({s.Entries.Count} 链接)");

            return 0;
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }
    }
}
