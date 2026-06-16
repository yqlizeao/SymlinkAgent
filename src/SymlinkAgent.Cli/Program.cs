using SymlinkAgent.Cli;
using SymlinkAgent.Commands;

namespace SymlinkAgent;

/// <summary>
/// 程序入口:解析 argv → 按动词分发到对应命令(调用指令化)。
/// 所有命令统一实现 ICommand,入口只负责"路由 + 兜底异常 + 返回退出码"。
/// </summary>
internal static class Program
{
    // 命令注册表:动词 → 命令对象。新增命令只需在此登记一行。
    private static readonly ICommand[] Commands =
    {
        new InitCommand(),
        new ApplyCommand(),
        new RemoveCommand(),
        new StatusCommand(),
        new ListCommand(),
        new DoctorCommand(),
    };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        ParsedArgs parsed = ParsedArgs.Parse(args);
        if (string.IsNullOrEmpty(parsed.Verb) || parsed.Verb is "help" or "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        ICommand? cmd = Commands.FirstOrDefault(c =>
            string.Equals(c.Name, parsed.Verb, StringComparison.OrdinalIgnoreCase));

        if (cmd is null)
        {
            Console.Error.WriteLine($"未知命令:{parsed.Verb}");
            PrintHelp();
            return 1;
        }

        try
        {
            return cmd.Execute(new CommandContext(parsed));
        }
        catch (Exception ex)
        {
            // 兜底:任何未被命令内部处理的异常都不应让进程崩出栈
            Console.Error.WriteLine($"[ERR]  {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SymlinkAgent -- 链接管理器(Windows)");
        Console.WriteLine();
        Console.WriteLine("用法:SymlinkAgent <命令> [参数...]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        foreach (var c in Commands)
            Console.WriteLine($"  {c.Name,-8} {c.Summary}");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  SymlinkAgent init   D:\\agent-hub");
        Console.WriteLine("  SymlinkAgent apply  E:\\repo\\demo");
        Console.WriteLine("  SymlinkAgent status E:\\repo\\demo");
        Console.WriteLine("  SymlinkAgent remove E:\\repo\\demo  [--force]");
    }
}
