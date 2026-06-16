using SymlinkAgent.Model;

namespace SymlinkAgent.Core;

/// <summary>单条链接的核查结论。</summary>
public enum LinkStatus
{
    /// <summary>正常:是链接且指向预期源。</summary>
    Ok,
    /// <summary>缺失:目标路径不存在。</summary>
    Missing,
    /// <summary>失效:目标存在但已不是链接(被替换成真实文件/目录)。</summary>
    NotALink,
    /// <summary>指向变了:是链接但指向的源与记录不符。</summary>
    WrongTarget,
}

/// <summary>
/// 核查器:state-first(读记录)+ scan-second(核对目标目录真实情况)。
/// 被 status / doctor / remove 共用,是"安全撤销"的判定核心。
/// 责任:目标检查。
/// </summary>
public sealed class Verifier
{
    private readonly LinkService _links;

    public Verifier(LinkService links) => _links = links;

    /// <summary>核查一条 state 记录,返回其当前真实状态。</summary>
    public LinkStatus Verify(StateEntry entry)
    {
        string target = entry.AbsoluteTarget;

        bool isLink = _links.IsLink(target);
        bool exists = isLink || File.Exists(target) || Directory.Exists(target);

        if (!exists) return LinkStatus.Missing;
        if (!isLink) return LinkStatus.NotALink;

        string? actual = _links.ResolveTarget(target);
        if (actual is null || !SamePath(actual, entry.ExpectedSource))
            return LinkStatus.WrongTarget;

        return LinkStatus.Ok;
    }

    /// <summary>大小写不敏感、忽略尾部分隔符的路径相等比较。</summary>
    public static bool SamePath(string a, string b)
    {
        static string Norm(string p) => Path.GetFullPath(p)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        return Norm(a) == Norm(b);
    }
}
