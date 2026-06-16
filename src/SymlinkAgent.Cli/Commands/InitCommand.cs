using SymlinkAgent.Cli;
using SymlinkAgent.Core;

namespace SymlinkAgent.Commands;

/// <summary>
/// init:把指定文件夹登记为源目录,写入单一数据文件并加入历史。
/// 源用文件夹直接管理,本工具不在其中创建任何结构。
/// 用法:SymlinkAgent init &lt;源路径&gt;
/// </summary>
public sealed class InitCommand : ICommand
{
    public string Name => "init";
    public string Summary => "把文件夹登记为源目录";

    public int Execute(CommandContext ctx)
    {
        string? hub = ctx.Args.Positional(0) ?? ctx.Args.FlagValue("source");
        if (string.IsNullOrWhiteSpace(hub))
        {
            ctx.Error("用法:SymlinkAgent init <源路径>");
            return 1;
        }

        try
        {
            DataStore.ValidateSourceRoot(hub); // 校验本地真实盘、目录存在
            DataStore data = DataStore.Load();
            data.SetCurrentSource(hub);        // 设为当前 + 加入历史 + 保存

            ctx.Ok($"已登记源 = {Path.GetFullPath(hub)}");
            ctx.Info($"数据文件:{AppPaths.DataFile}");
            return 0;
        }
        catch (Exception ex)
        {
            ctx.Error(ex.Message);
            return 1;
        }
    }
}
