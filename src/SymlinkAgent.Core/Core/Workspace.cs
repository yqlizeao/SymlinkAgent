namespace SymlinkAgent.Core;

/// <summary>
/// 工作区装配器:按数据文件中的"当前源目录"组装好各 Core 服务,供命令/引擎取用。
/// 单一职责 = "依配置装配服务";各服务本身仍互相独立。
/// </summary>
public sealed class Workspace
{
    public string SourceRoot { get; }
    public LinkService Links { get; }
    public SourceScanner Source { get; }
    public DataStore Data { get; }
    public SvnIgnoreService Svn { get; }
    public Verifier Verifier { get; }

    private Workspace(DataStore data, string sourceRoot)
    {
        Data = data;
        SourceRoot = sourceRoot;
        Links = new LinkService();
        Source = new SourceScanner(sourceRoot);
        Svn = new SvnIgnoreService();
        Verifier = new Verifier(Links);
    }

    /// <summary>读取数据文件并装配工作区;未设置源目录或源目录不可达时抛异常。</summary>
    public static Workspace Open()
    {
        DataStore data = DataStore.Load();
        if (string.IsNullOrWhiteSpace(data.CurrentSource))
            throw new InvalidOperationException("尚未设置源目录。请先运行:SymlinkAgent init <源路径>(或在 GUI 点\"设置源\")。");

        DataStore.ValidateSourceRoot(data.CurrentSource!);
        return new Workspace(data, Path.GetFullPath(data.CurrentSource!));
    }
}
