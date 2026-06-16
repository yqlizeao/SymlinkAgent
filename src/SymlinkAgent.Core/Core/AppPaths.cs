namespace SymlinkAgent.Core;

/// <summary>
/// 便携路径解析:所有持久化数据都放在 EXE 所在目录下的 data/ 子目录,
/// 不写 %APPDATA%、不写注册表 —— 整个工具文件夹可整体拷走(绿色便携版)。
/// 单一职责:解析"工具自身目录"及其下的数据路径。
/// </summary>
public static class AppPaths
{
    /// <summary>工具自身所在目录(单文件 EXE 运行时即 EXE 所在目录)。</summary>
    public static string ToolDir =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>数据根目录:&lt;工具目录&gt;\data。</summary>
    public static string DataDir => Path.Combine(ToolDir, "data");

    /// <summary>单一数据文件(配置 + 历史 + 各项目链接记录,INI 格式)。</summary>
    public static string DataFile => Path.Combine(DataDir, "symlinkagent.ini");
}
