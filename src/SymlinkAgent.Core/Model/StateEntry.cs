using System.Text.Json.Serialization;

namespace SymlinkAgent.Model;

/// <summary>单条链接记录(项目内一个被本工具创建的链接)。</summary>
public sealed class StateEntry
{
    /// <summary>目标相对路径(相对项目根,等于条目名)。</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>目标绝对路径(由项目根 + Target 推得)。</summary>
    public string AbsoluteTarget { get; set; } = string.Empty;

    /// <summary>期望指向的源绝对路径。核查时与 ResolveLinkTarget 比对。</summary>
    public string ExpectedSource { get; set; } = string.Empty;

    /// <summary>链接类型字符串(symlink-file / symlink-dir)。</summary>
    public string Type { get; set; } = LinkTypeParser.FileToken;

    [JsonIgnore]
    public LinkType LinkType => LinkTypeParser.Parse(Type);
}
