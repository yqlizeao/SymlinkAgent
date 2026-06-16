namespace SymlinkAgent.Model;

/// <summary>
/// 链接类型。本工具刻意只支持两种 —— 文件与目录都用 symbolic link,
/// 不用 junction(其免管理员的唯一优势在"全程管理员"模型下消失,
/// 且会被 SVN 当真实目录递归 add)。
/// </summary>
public enum LinkType
{
    /// <summary>文件符号链接(File.CreateSymbolicLink)。</summary>
    SymlinkFile,

    /// <summary>目录符号链接(Directory.CreateSymbolicLink)。</summary>
    SymlinkDir,
}

/// <summary>LinkType 与 manifest/state JSON 里字符串值的互转。</summary>
public static class LinkTypeParser
{
    public const string FileToken = "symlink-file";
    public const string DirToken = "symlink-dir";

    public static LinkType Parse(string? token) => token switch
    {
        FileToken => LinkType.SymlinkFile,
        DirToken => LinkType.SymlinkDir,
        _ => throw new FormatException($"未知链接类型:\"{token}\"(仅支持 {FileToken} / {DirToken})"),
    };

    public static string ToToken(LinkType type) => type switch
    {
        LinkType.SymlinkFile => FileToken,
        LinkType.SymlinkDir => DirToken,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
