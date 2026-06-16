# CLAUDE.md

SymlinkAgent —— Windows 通用符号链接管理器。源目录就是文件夹,**源顶层文件/目录自动成为链接条目**(约定优于配置);软链进各目标目录,目标目录只出现链接。**拆三工程:Core 引擎 + CLI 壳 + WPF GUI 壳。**

## 编码原则

- **最小协议**:对外一个文件夹约定(源顶层即条目)+ 一个 INI 数据文件;`LinkService` 只暴露 `Create / Remove / ResolveTarget / IsLink`
- **单一职责**:一个关注点一个类
- **调用指令化**:操作是离散命令对象,无跨调用隐藏状态
- **需求责任制**:每项需求映射到唯一责任模块(见下表),业务逻辑只在 `TargetEngine`,CLI/GUI 不得重复
- **中文注释**:类级 + 关键方法级必要中文注释

## 工程结构

```
SymlinkAgent.sln
src/
├─ SymlinkAgent.Core/   Model/ + Core/(引擎,无 UI 依赖)
├─ SymlinkAgent.Cli/    Cli/ + Commands/ + Program.cs → SymlinkAgent.exe
└─ SymlinkAgent.Gui/    MainWindow + ViewModels/ → SymlinkAgentGui.exe
```

CLI 命令实现 `ICommand`(在 `Program.cs` 注册表登记一行);GUI 用 `RelayCommand` 绑定 `MainViewModel`。两者都是 `TargetEngine` 薄壳。

## GUI 交互约定(改 GUI 前必读,勿回退)

- **两块列表均为 `DataGrid`**,共用 `MainWindow.xaml` 资源里的 `GridHeaderStyle` / `GridRowStyle` / `GridCellStyle` —— 改观感只改这一处,左右自动同步;勿退回 ListView,也勿给单边写内联样式
- **选中驱动操作**:底部按钮按"有多选→选中,否则→全部"执行(`ScopedNames`);右键单条用独立的 `ApplyEntry/RemoveEntry`(右表)与 `ApplySourceEntry/RemoveSourceEntry`(左表),**勿**让右键复用底部按钮的 `ApplyCommand/RemoveCommand`(否则禁用态会牵连底部按钮)
- **右键启用/禁用**:`应用此条` 仅未应用时可用,`移除此条` 仅已应用/有记录时可用;选中项变化时调 `CommandManager.InvalidateRequerySuggested()`
- **刷新时机统一**:切源 / 改目标(去抖)即自动 `检查`;`检查` 按钮 = 手动刷新(配 F5)。`TargetEngine.Apply/Remove` 带可选 `names` 子集供 GUI 单条/多条
- **安全**:`移除` 与勾选 `强制` 的操作弹确认;`强制` 不粘滞(用后置回 false)
- **反馈**:错误走 `AppendError`(写日志 + 自动展开日志 + 状态栏红字);右表空时显示 `EmptyHint` 占位;状态栏两句概览(源侧总计/已启用/未启用 + 目标侧链接健康度)

## 架构(需求 → 责任模块,均在 Core)

| 能力 | 模块 |
|---|---|
| 数据持久化(配置+源历史+链接记录) | `DataStore`(单一 `data/symlinkagent.ini`,原子写入;`AppPaths` 解析路径) |
| 源内容推导 | `SourceScanner`(扫描顶层→链接条目,忽略 .svn/.git/data 等) |
| 目标应用/移除/检查 | `TargetEngine`(结构化结果:ApplyOutcome/RemoveOutcome/StatusOutcome) |
| 链接底层 | `LinkService`(.NET 原生 symbolic link) |
| 核查 | `Verifier`(state-first + scan-second) |
| SVN 隔离 | `SvnIgnoreService` |

## 关键约束

- 文件与目录**统一用 .NET 原生 symbolic link**,不用 junction、不 shell 调用 `mklink`
- 全程管理员(EXE 内嵌 `requireAdministrator`),无权限分支
- 本地版本控制 **SVN**:用 `svn:ignore` 隔离链接
- 源目录必须本地真实盘(管理员进程看不到映射网络盘)
- **绿色便携**:所有数据存 EXE 旁 `data/symlinkagent.ini`,CLI/GUI 同目录即共享
- **WPF 注意**:GUI 工程不可启用 `InvariantGlobalization`;`DataStore`/`IniFile` 对损坏数据返回空不抛错
- **UI 绑定**:可编辑 ComboBox 的 ItemsSource 更新用**差量同步**(不 `Clear()`),避免 WPF 冲掉 Text 绑定

## 安全规则(见 docs/spec.md)

- **创建**:目标已存在即冲突;`--force` 只覆盖旧链接,真实文件/目录永不覆盖
- **删除**:只删记录项;删前核查仍是预期链接才删;目录链接非递归删除;真实文件/目录永不删除

## 构建

```bash
dotnet build SymlinkAgent.sln -c Debug
dotnet publish src/SymlinkAgent.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
dotnet publish src/SymlinkAgent.Gui -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```
