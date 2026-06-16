using SymlinkAgent.Model;

namespace SymlinkAgent.Core;

/// <summary>
/// 链接服务:对 .NET 原生符号链接 API 的最小封装。
/// 只暴露四个方法(最小协议):Create / Remove / ResolveTarget / IsLink。
/// 责任:链接的底层创建/删除/解析,不掺杂业务逻辑。
/// </summary>
public sealed class LinkService
{
    /// <summary>
    /// 创建符号链接。linkPath 为待创建的链接路径,targetPath 为其指向的真实源。
    /// 文件与目录分别调用对应的原生 API。
    /// </summary>
    public void Create(LinkType type, string linkPath, string targetPath)
    {
        // 确保父目录存在(目标根一般已存在,.claude 子项链接时父目录需就绪)
        string? parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        switch (type)
        {
            case LinkType.SymlinkFile:
                File.CreateSymbolicLink(linkPath, targetPath);
                break;
            case LinkType.SymlinkDir:
                Directory.CreateSymbolicLink(linkPath, targetPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    /// <summary>
    /// 删除链接本身。关键安全点:目录链接用非递归删除,只摘掉链接、绝不递归删除源内容。
    /// </summary>
    public void Remove(LinkType type, string linkPath)
    {
        switch (type)
        {
            case LinkType.SymlinkFile:
                File.Delete(linkPath);
                break;
            case LinkType.SymlinkDir:
                // recursive: false —— 仅删除目录符号链接本身,不触碰其指向的真实目录
                Directory.Delete(linkPath, recursive: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    /// <summary>
    /// 解析链接最终指向的绝对路径;若 path 不是链接或不存在,返回 null。
    /// 用于 status/doctor/remove 的指向核查。
    /// </summary>
    public string? ResolveTarget(string path)
    {
        FileSystemInfo? info = GetInfo(path);
        if (info is null) return null;
        FileSystemInfo? final = info.ResolveLinkTarget(returnFinalTarget: true);
        return final?.FullName;
    }

    /// <summary>判断 path 当前是否为一个符号链接。</summary>
    public bool IsLink(string path)
    {
        FileSystemInfo? info = GetInfo(path);
        return info?.LinkTarget is not null;
    }

    /// <summary>按"目录优先"取 FileSystemInfo;路径不存在(含悬空链接的两种形态)时尽力返回。</summary>
    private static FileSystemInfo? GetInfo(string path)
    {
        // 目录链接即使指向已失效,Directory.Exists 可能为 false,但 DirectoryInfo.LinkTarget 仍可读
        var di = new DirectoryInfo(path);
        if (di.Exists || di.LinkTarget is not null) return di;
        var fi = new FileInfo(path);
        if (fi.Exists || fi.LinkTarget is not null) return fi;
        return null;
    }
}
