# THUnion 使用与实现说明

THUnion 位于“下载”之后、“设置”之前，沿用 PCL 的顶栏、左侧分组、卡片、按钮、加载动画与主题色。此修改不变更现有账户、投影管理及下载实现。

## 2.99.T.1 修复

2.99.T.0 的 `.exe.config` 把 WinRT 的 `System.Numerics.Vectors` 请求重定向到 4.1.4.0，但交付目录没有该 DLL，导致打开内嵌网页时报程序集缺失。原全局异常处理又误判为系统框架过旧并打开 .NET Framework 下载页面；重装框架无法修复此缺包问题。

2.99.T.1 将 `System.Numerics.Vectors`（NuGet 4.5.0，程序集 4.1.4.0）、WebView2 Core/WPF 和 HtmlAgilityPack 嵌入 EXE，由启动器的程序集解析器直接从内存加载。创建网页控件前检查依赖，异常时留在页面显示修复说明。运行时异常不再自动打开 .NET 安装器页面；仅启动检测确实低于 .NET Framework 4.8 时才引导安装。

此前检查程序未使用启动器自身的 `.exe.config`，漏检了该问题。上一轮修复曾用旧包及相同配置复现问题，并交付附带 DLL 的版本。本次进一步恢复单 EXE 分发：检查在只有启动器 EXE 的独立目录运行，不携带 DLL、`runtimes` 或 `.exe.config`，检查程序本身也位于另一目录并且不提供依赖解析兜底。

THUnion 子页面切换沿用下载页的右侧内容动画，保留已挂载的左侧侧栏。点击侧栏、操作按钮和代码导航共用该路径；快速中断切换后返回在线页也会恢复轮询。

## 三个本地版本

| 目录 | 原版游戏下载 | 主题修改 | THUnion |
| --- | --- | --- | --- |
| `artifacts/thunion-2.99.T.1-singlefile/all-disabled` | 关闭 | 关闭 | 隐藏并禁止进入 |
| `artifacts/thunion-2.99.T.1-singlefile/theme-disabled` | 启用 | 关闭 | 启用 |
| `artifacts/thunion-2.99.T.1-singlefile/all-enabled` | 启用 | 启用 | 启用 |

按需求确认，“下载关闭”沿用现有 `RestrictMcDownload` 开关：保留 Mod、资源包等资源下载，不隐藏整个“下载”主选项卡。

每个目录运行 `PCL-Union-2.99.T.1.exe`。可单独复制 EXE 到其他目录运行，无需同目录 DLL、`runtimes` 或 `.exe.config`。`build-manifest.json` 记录三个开关、单文件标记与 EXE 的 SHA-256；说明和许可文件不是运行依赖。旧输出目录保留作历史记录，新包位于带 `-singlefile` 的目录。

EXE 内含 x86/x64/ARM64 的原生 `WebView2Loader.dll`。打开内嵌网页时，按当前进程架构自动释放到 `%LOCALAPPDATA%/PCL-Union/THUnion/native/{SHA256}/{架构}`，校验哈希并在损坏时重新释放。使用跨进程互斥锁和临时文件替换，避免多个实例同时写入；无需在 EXE 所在目录释放 DLL。原生加载器需要磁盘文件，因此“单文件”指分发和移动时只有 EXE，运行时仍会生成用户缓存。

内嵌页面需要 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。本机已实际加载验证；其他电脑缺少 Runtime 时会显示错误及重试入口，仍可使用“在浏览器中打开”。本次不捆绑完整浏览器安装包。

## 页面

“三周目”分组：在线列表、实时地图、工业设施导引。

“其他”分组：Wiki首页、服务器列表、二周目、一周目、部门规章制度。

- **在线列表**：进入后立即显示操作卡片，数据区先显示原生加载动画。GET `http://www.lvss.xyz:14010/up/world/world/{当前UTC毫秒时间戳}`。每秒尝试刷新一次，未结束的请求不会重叠；离开页面立即取消请求和定时器，返回后恢复。请求超时为 12 秒。接口人数采用 `currentcount`，列表来自 `players`，两者不强行对齐。
- **玩家行**：显示头像、名称、维度、x/y/z、生命与护甲；`world`、`DIM-1`、`DIM1` 映射为主世界、下界、末地，其他维度保留原值。坐标显示至小数点后两位。头像从指定 32×32 地址取得，按名称缓存，最多并发四个；头像失败显示占位图，不阻塞玩家数据。
- **失败恢复**：首次加载失败显示错误并允许点击加载图标或 F5 重试。在线页会继续自动重试；后续刷新失败保留上一次成功数据，并明确显示旧数据的更新时间，恢复后自动替换。有效空列表显示人数 0 和表头。
- **工业设施导引、服务器列表**：每次进入从指定 Wiki 页面 GET HTML，使用 HtmlAgilityPack 解析正文 `wikitable`，将表格行、表头、换行、合并单元格及链接渲染为原生 WPF 表格。工业页面卡片标题按需求写为“三周目工业设置导引”。加载与错误体验同在线页；Wiki 表格不作每秒轮询，F5 可手动刷新。宽表提供横向、纵向滚动条。
- **实时地图**：内嵌加载 `http://map.thunion.club/`，由其页面正常加载 Dynmap 框架、地图瓦片及脚本。
- **Wiki首页、二周目、一周目、部门规章制度**：内嵌加载指定 URL，保留二周目、一周目的锚点。右上角“在浏览器中打开”固定悬浮，不随网页滚动；实时地图也提供此按钮。按钮打开当前浏览地址，页面尚未初始化时打开该子页面的目标地址。

网页使用 WebView2 CompositionControl，避免传统 WPF 浏览器控件遮住悬浮按钮。网页不暴露启动器宿主对象或消息接口；表格解析不执行远端 HTML/XAML。示例请求中的个人 Cookie 和缓存校验头未写入程序。网页数据目录位于 `%LOCALAPPDATA%/PCL-Union/THUnion/WebView2`，与默认浏览器及启动器账户存储分离。

## 构建与维护

新增全局常量 `ModSecret.EnableTHUnion`，由 `PCL_ENABLE_THUNION` 控制；MSBuild 参数 `/p:EnableTHUnion=true`。默认关闭，F12 功能隐藏设置不能绕过。既有 `PageType` 数字保持不变，新增 `THUnion = 10`；标题导航改按 Tag 定位，避免插入按钮影响原页面索引。

```powershell
& 'S:\Projects\Minecraft\PCL\PCL-Union\scripts\Build-THUnionVariants.ps1'
& 'S:\Projects\Minecraft\PCL\PCL-Union\scripts\Verify-THUnion.ps1'
```

单独构建可使用 `Build-PCL-Union.ps1 -EnableTHUnion`，与现有 `-EnableDownload`、`-EnableTheme` 独立组合。完整环境及本地 Client ID 配置见 [BUILD.md](BUILD.md)。

主要源码：

- `PCLCS/THUnion/UnionService.cs`：地址、GET、取消与超时、JSON/HTML 解析及数据模型。
- `Plain Craft Launcher 2/Pages/PageTHUnion/`：左侧导航、数据页面、内嵌网页。
- `FormMain.xaml(.vb)`、`ModMain.vb`、`ModSecret.vb`、`PageSetupUI.xaml.vb`：主导航与编译开关集成。
- `Modules/Base/EmbeddedDependencies.vb`：内嵌托管程序集解析及原生加载器缓存。
- `tests/THUnionChecks/`：隔离注册表的运行验证。在线与网页检查需要服务器可达；HTML 解析还使用仓库旁 `Wiki` 中的本地样本。测试只复制各交付 EXE，实例化启动器 Application 以注册真实依赖解析器，绕过正常账户初始化，不使用个人登录令牌。

新增依赖锁定为 Microsoft.Web.WebView2 1.0.4314.35、HtmlAgilityPack 1.13.1、System.Numerics.Vectors 4.5.0（程序集 4.1.4.0），许可副本在 `licenses/THUnion`，同时嵌入启用版 EXE，并附带可直接阅读的 `third-party-licenses`。

## 本次验证范围（2026-10-27）

三个版本均完成 Release 构建和运行时开关核验。两个启用版本实际验证了在线接口、头像、每秒更新、离开取消/返回恢复、八个侧栏入口、两个 Wiki 表格、五个内嵌网页（含两个锚点）、默认窗口列宽及宽表右侧列可达。检查还覆盖损坏 JSON、有效空列表、合并单元格、链接处理、模拟服务失败后的旧数据保留与重试恢复、快速切页，以及原“设置”主导航。

关闭版本验证了入口隐藏、直接路由拒绝、F12 无法重新显示。单文件检查还核对托管依赖从内存加载、原生加载器从用户缓存加载、测试缓存损坏后自动修复，以及侧栏在连续切换中不触发 Loaded/Unloaded。构建日志、检查日志、WPF 渲染图和 WebView2 页面截图保存在 `artifacts/thunion-2.99.T.1-singlefile`。网页与服务器在线状态属于验证时结果；本机验证为 x64，尚未进行 x86/ARM64、其他电脑、真实微软登录、游戏启动及长期持续运行验证。
