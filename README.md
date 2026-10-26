# Plain Craft Launcher (PCL) Union 第三方修改版

**PCL-Union · 2.99.T.0** 是第三方基于 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 独立修改的 fork，非官方发布，与上游官方产品区分。

原作者：[龙腾猫跃](https://github.com/LTCatt)；[赞助原作者](https://meloong.com/afd/a/LTCat)。保留原关于页首位署名、赞助入口、Minecraft 正版购买及赞助劝导。继续使用原 [LICENCE](LICENCE)，不重新授权上游代码。

## 本分支的变更

- 移除启动器公告、自动更新相关控件和更新替换入口；不包含官方二进制中的公告云控、强制更新执行链。公开源码原本未包含其完整私有实现，本分支没有将这些实现移植回来。
- 删除启动前任意命令、整合包预启动命令迁移、自定义主页启动本地程序及任意写设置的执行路径。HTTPS 登录使用系统证书校验。
- 增加与 Mod 管理同风格的投影管理：搜索、格式筛选、批量导入/导出、重命名、回收站删除、元数据详情。只有检测到启用的 Litematica、Forgematica、Schematica 时才显示入口。
- 兼容导入同一 Windows 用户的官方账户状态。官方配置只读，本分支账户正常可写；新登录、切换、刷新和退出不修改官方配置。新写入的凭据使用 Windows DPAPI 保护。
- 同步上游 2.99.0.1 对 Minecraft 下载入口的进一步隐藏。公开源码和 Release 默认关闭 Minecraft 下载与主题修改，主题固定为默认蓝色；Mod、资源包等入口按上游规则保留。

## 使用

从 [Releases](https://github.com/c0d805e15c550432/PCL-Union/releases) 下载 EXE 与同名 `.exe.config`，放在同一目录运行。

首次运行会在本分支没有已有账户时尝试一次官方账户导入，不覆盖已有账户，也不在退出账户后反复导入。读取失败时不改动官方数据。令牌若已过期、撤销，或 OAuth 应用标识不兼容，仍需重新登录。

详细说明：[投影管理与账户兼容](SCHEMATIC_MANAGER.md) · [本地构建](BUILD.md) · [验证与边界](VALIDATION.md)。

上游基线：[93ede5d / 2.99.0.1](https://github.com/Meloong-Git/PCL/commit/93ede5d9e638c99fcec853a9ba1eecd91ceee7f5)。仓库历史以本分支发布快照重新整理，GitHub fork 关系和上游来源说明保留。
