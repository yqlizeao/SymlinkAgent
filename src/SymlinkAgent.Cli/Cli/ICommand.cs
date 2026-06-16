namespace SymlinkAgent.Cli;

/// <summary>
/// 命令统一协议(最小协议)。每个动词对应一个实现了本接口的命令对象,
/// 只暴露一个方法:执行并返回进程退出码(0 成功,非 0 失败/有异常)。
/// </summary>
public interface ICommand
{
    /// <summary>动词名,例如 "apply"。</summary>
    string Name { get; }

    /// <summary>一行用途说明,供 help/list 输出。</summary>
    string Summary { get; }

    /// <summary>执行命令,返回进程退出码。</summary>
    int Execute(CommandContext ctx);
}
