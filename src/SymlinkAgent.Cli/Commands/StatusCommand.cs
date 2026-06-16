using SymlinkAgent.Cli;
using SymlinkAgent.Core;

namespace SymlinkAgent.Commands;

/// <summary>
/// status:薄壳 —— 调 TargetEngine.Status,再渲染逐条状态。
/// 用法:SymlinkAgent status &lt;目标目录&gt;
/// </summary>
public sealed class StatusCommand : ICommand
{
    public string Name => "status";
    public string Summary => "检查目标链接状态";

    public int Execute(CommandContext ctx)
    {
        string? project = ctx.Args.Positional(0);
        if (string.IsNullOrWhiteSpace(project))
        {
            ctx.Error("用法:SymlinkAgent status <目标目录>");
            return 1;
        }

        try
        {
            StatusOutcome r = TargetEngine.Open().Status(project);
            if (r.Entries.Count == 0)
            {
                ctx.Warn("该目标没有任何链接记录。");
                return 0;
            }

            ctx.Info($"目标:{r.TargetRoot}");
            ctx.Info($"来源:{r.SourceRoot}");

            int abnormal = 0;
            foreach (EntryStatus e in r.Entries)
            {
                switch (e.Status)
                {
                    case LinkStatus.Ok: ctx.Ok($"{e.Target}"); break;
                    case LinkStatus.Missing: ctx.Error($"{e.Target}  缺失"); abnormal++; break;
                    case LinkStatus.NotALink: ctx.Warn($"{e.Target}  失效(已非链接)"); abnormal++; break;
                    case LinkStatus.WrongTarget: ctx.Warn($"{e.Target}  指向变了(期望 {e.ExpectedSource})"); abnormal++; break;
                }
            }

            ctx.Info(abnormal == 0 ? "全部正常。" : $"发现 {abnormal} 处异常。");
            return abnormal == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }
    }
}
