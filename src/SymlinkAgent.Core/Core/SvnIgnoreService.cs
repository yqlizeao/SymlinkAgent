using System.Diagnostics;

namespace SymlinkAgent.Core;

/// <summary>
/// SVN 忽略服务:把本工具创建的链接名写入所在目录的 svn:ignore 属性,
/// 使 SVN 工作副本不会把链接误当待 add 项(链接永不纳入版本管理)。
/// 非 SVN 工作副本、或未安装 svn 时,所有操作静默跳过。
/// 责任:版本控制隔离(SVN 专用)。
/// </summary>
public sealed class SvnIgnoreService
{
    /// <summary>当前环境是否可用 svn 命令(只探测一次)。</summary>
    private bool? _svnAvailable;

    /// <summary>判断目录是否处于 SVN 工作副本中。</summary>
    public bool IsWorkingCopy(string dir)
    {
        if (!SvnAvailable()) return false;
        var (code, _, _) = RunSvn("info", dir);
        return code == 0;
    }

    /// <summary>把 name 追加进 dir 的 svn:ignore(已存在则不重复)。失败仅返回 false,不抛。</summary>
    public bool AddIgnore(string dir, string name)
    {
        if (!IsWorkingCopy(dir)) return false;
        var set = GetIgnoreSet(dir);
        if (!set.Add(name)) return true; // 已存在,视为成功
        return SetIgnore(dir, set);
    }

    /// <summary>从 dir 的 svn:ignore 中移除 name。失败仅返回 false,不抛。</summary>
    public bool RemoveIgnore(string dir, string name)
    {
        if (!IsWorkingCopy(dir)) return false;
        var set = GetIgnoreSet(dir);
        if (!set.Remove(name)) return true; // 本就不存在
        return SetIgnore(dir, set);
    }

    // —— 内部实现 ——

    private HashSet<string> GetIgnoreSet(string dir)
    {
        var (code, stdout, _) = RunSvn("propget", "svn:ignore", dir);
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (code == 0)
        {
            foreach (var line in stdout.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
        }
        return set;
    }

    private bool SetIgnore(string dir, HashSet<string> set)
    {
        // 通过临时文件传多行属性值,避免命令行换行转义问题
        string value = string.Join('\n', set.OrderBy(s => s, StringComparer.Ordinal));
        string tmp = Path.Combine(Path.GetTempPath(), "symlinkagent-svnignore-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(tmp, value);
            var (code, _, _) = RunSvn("propset", "svn:ignore", "-F", tmp, dir);
            return code == 0;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private bool SvnAvailable()
    {
        if (_svnAvailable.HasValue) return _svnAvailable.Value;
        try
        {
            var (code, _, _) = RunSvn("--version", "--quiet");
            _svnAvailable = code == 0;
        }
        catch
        {
            _svnAvailable = false;
        }
        return _svnAvailable.Value;
    }

    private static (int code, string stdout, string stderr) RunSvn(params string[] args)
    {
        var psi = new ProcessStartInfo("svn")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 svn 进程。");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }
}
