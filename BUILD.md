# 本地构建

需要 Visual Studio MSBuild、VB/WPF、.NET Framework 4.8 targeting pack、C# 13，以及 PowerShell 7。先执行 `git submodule update --init --recursive`。

在仓库根目录创建 `.local/client-id.txt`，只填写构建使用的 Microsoft 应用 Client ID（GUID）。该文件与目录已由 `.gitignore` 排除，不提交 Git，不复制进源码包。不要填写账号密码、访问令牌或应用密钥。构建时自动读取并嵌入资源，生成的 EXE 无需外部配置该值。Client ID 是应用标识，编译进 EXE 后可以被提取，并非秘密凭据。

要继承官方令牌，构建使用的应用标识应与原令牌兼容。本仓库不提供具体 Client ID；缺少配置会明确停止构建。

```powershell
& .\scripts\Build-PCL-Union.ps1 -OutputDirectory .\artifacts\standard
& .\scripts\Build-PCL-Union.ps1 -OutputDirectory .\artifacts\enabled -EnableDownload -EnableTheme
```

两项开关独立，均为编译期配置，不在界面中提供绕过开关：

| 功能 | 默认值 | 全局常量 | MSBuild 启用参数 |
| --- | --- | --- | --- |
| Minecraft 下载 | 关闭 | `RestrictMcDownload = True` | `/p:EnableDownload=true` |
| 主题修改 | 关闭 | `EnableThemeCustomization = False` | `/p:EnableTheme=true` |

对应条件编译符号是 `PCL_ENABLE_DOWNLOAD` 和 `PCL_ENABLE_THEME`。普通 MSBuild 构建也自动读取 `.local/client-id.txt`。不要把本地构建输出或身份配置加入 Git。公开发布仅使用默认两项关闭的构建，遵循上游 LICENCE。

显示版本与 Release 标签为 `2.99.T.0`；.NET 的数字程序集/文件版本为 `2.99.0.1`，InformationalVersion 为 `2.99.T.0`。

验证：

```powershell
& .\scripts\Verify-SchematicBuild.ps1 -Executable .\artifacts\standard\PCL-Union-2.99.T.0.exe -Variant standard
& .\scripts\Verify-SchematicBuild.ps1 -Executable .\artifacts\enabled\PCL-Union-2.99.T.0.exe -Variant enabled
```

`Check-OfficialAccountReadOnly.ps1` 可在本机只读验证官方配置解密及 DPAPI 重加密，仅输出数量和布尔结果，不输出账号、设备标识或令牌，也不调用联网登录。
