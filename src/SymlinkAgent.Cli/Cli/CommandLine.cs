namespace SymlinkAgent.Cli;

/// <summary>
/// 零依赖命令行解析(最小协议)。约定格式:
///   SymlinkAgent &lt;verb&gt; [位置参数...] [--flag] [--key value]
/// 规则:
///   - 第一个非 -- 开头的 token 是动词(verb)。
///   - 其余非 -- token 是位置参数。
///   - --name 后若紧跟一个非 -- token,则视为带值开关;否则为布尔开关。
/// </summary>
public sealed class ParsedArgs
{
    public string Verb { get; private set; } = string.Empty;
    public IReadOnlyList<string> Positionals => _positionals;
    private readonly List<string> _positionals = new();
    private readonly Dictionary<string, string?> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否带有该布尔/带值开关。</summary>
    public bool HasFlag(string name) => _flags.ContainsKey(name);

    /// <summary>取带值开关的值;不存在或为布尔开关时返回 null。</summary>
    public string? FlagValue(string name) => _flags.TryGetValue(name, out var v) ? v : null;

    /// <summary>取第 index 个位置参数(0 基);越界返回 null。</summary>
    public string? Positional(int index) => index < _positionals.Count ? _positionals[index] : null;

    public static ParsedArgs Parse(string[] args)
    {
        var result = new ParsedArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                string key = token[2..];
                // 后面紧跟一个非开关 token → 带值开关;否则布尔开关
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    result._flags[key] = args[++i];
                }
                else
                {
                    result._flags[key] = null;
                }
            }
            else if (result.Verb.Length == 0)
            {
                result.Verb = token;
            }
            else
            {
                result._positionals.Add(token);
            }
        }
        return result;
    }
}
