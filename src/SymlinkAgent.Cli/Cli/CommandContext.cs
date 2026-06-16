namespace SymlinkAgent.Cli;

/// <summary>
/// 命令执行上下文:封装"解析后的参数 + 输出器"。
/// 全局配置(hub 路径)由各命令按需通过 GlobalConfig 加载,不在此隐式持有,
/// 以保证命令之间无跨命令隐藏状态(调用指令化)。
/// </summary>
public sealed class CommandContext
{
    public ParsedArgs Args { get; }

    public CommandContext(ParsedArgs args) => Args = args;

    // —— 输出器:统一带颜色的状态输出 ——
    public void Info(string msg) => Console.WriteLine(msg);

    public void Ok(string msg) => WriteColored(ConsoleColor.Green, "[OK]   ", msg);

    public void Warn(string msg) => WriteColored(ConsoleColor.Yellow, "[WARN] ", msg);

    public void Error(string msg) => WriteColored(ConsoleColor.Red, "[ERR]  ", msg);

    public void Skip(string msg) => WriteColored(ConsoleColor.DarkGray, "[SKIP] ", msg);

    private static void WriteColored(ConsoleColor color, string prefix, string msg)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(prefix);
        Console.ForegroundColor = prev;
        Console.WriteLine(msg);
    }
}
