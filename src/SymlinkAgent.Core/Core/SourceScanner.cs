using SymlinkAgent.Model;

namespace SymlinkAgent.Core;

/// <summary>
/// 一条待创建的链接规格,由扫描源目录得出(约定优于配置,无需 manifest)。
/// </summary>
/// <param name="Name">条目名(源顶层项名,也是目标内的目标名)。</param>
/// <param name="Source">源绝对路径(源目录内)。</param>
/// <param name="Target">目标相对路径(相对目标根,等于 Name)。</param>
/// <param name="LinkType">文件 → SymlinkFile;目录 → SymlinkDir。</param>
/// <param name="SvnIgnore">是否加入 SVN 忽略(默认 true)。</param>
public sealed record LinkSpec(string Name, string Source, string Target, LinkType LinkType, bool SvnIgnore);

/// <summary>
/// 源扫描器:把"源目录根目录的顶层文件/目录"直接当作要链接的条目
/// (源根 = 单个 profile,约定优于配置)。源目录完全用文件夹管理,本工具只读不改。
/// 责任:从源文件夹结构推导链接条目。
/// </summary>
public sealed class SourceScanner
{
    private readonly string _sourceRoot;

    /// <summary>顶层扫描时忽略的名字:版本控制元数据、工具数据、系统垃圾文件。</summary>
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svn", ".git", ".hg", "data", "desktop.ini", "Thumbs.db", ".DS_Store",
    };

    public SourceScanner(string sourceRoot) => _sourceRoot = sourceRoot;

    /// <summary>扫描源顶层,返回按名排序的链接规格(忽略元数据/垃圾项)。</summary>
    public IReadOnlyList<LinkSpec> Scan()
    {
        if (!Directory.Exists(_sourceRoot)) return Array.Empty<LinkSpec>();

        var list = new List<LinkSpec>();
        foreach (string path in Directory.EnumerateFileSystemEntries(_sourceRoot))
        {
            string name = Path.GetFileName(path);
            if (Ignore.Contains(name)) continue;

            LinkType type = Directory.Exists(path) ? LinkType.SymlinkDir : LinkType.SymlinkFile;
            list.Add(new LinkSpec(name, Path.GetFullPath(path), name, type, SvnIgnore: true));
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
