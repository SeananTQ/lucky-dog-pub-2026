---
last_editor: Codex
last_edit: 2026-07-26
status: revised
---

# Steam 正式版上传指南（主人版）

## 固定应用信息

正式版上传链路固定绑定：

- Lucky Dog Rise AppID：`2583700`
- Windows Content Depot ID：`2583701`
- 构建渠道：`Release`
- 本地 staging：`.local-build\staging\release\`
- SteamPipe 中间文件：`.local-build\steampipe\release\`

脚本会同时校验渠道、AppID 和 Depot ID。Playtest 的 AppID `4972240`、Depot ID `4972241` 无法通过正式版入口上传。

## Steamworks 后台准备

正式版 Windows Depot 应保持为“所有语言”。当前游戏把全部本地化内容放在同一个 Windows 包中，不使用语言专属 Depot。

正式版 Developer Comp 程序包需要包含 Depot `2583701`，否则开发者账号可能看到游戏却无法安装完整内容。

首次验收建议在 Steamworks 创建密码保护的 `internal` 分支。正式版 Build 先进入后台，再由主人将 Build 指派给 `internal` 分支。通过 Steam 客户端验收后，才能考虑指派给 `default` 并提交生成版本审核。

## 第一次使用前配置

复制：

```text
lucky-dog-rise\Build\SteamReleasePipeConfig.example.psd1
```

将副本命名为：

```text
lucky-dog-rise\Build\SteamReleasePipeConfig.psd1
```

保留固定 ID，只填写有正式版上传权限的 Steamworks 账号名：

```powershell
@{
    AppId = 2583700
    DepotId = 2583701
    SteamAccount = 'Steamworks账号名'
}
```

该本地配置已被 Git 忽略。密码和 Steam Guard 验证码不能写入文件，只能在 SteamCMD 交互提示中输入。

## 生成并检查正式版

Release 构建要求 Git 工作区完全干净。完成计划发布的提交后，在工作区根目录执行：

```powershell
.\lucky-dog-rise\Build\Publish-SteamRelease.ps1 -Action Generate
```

该命令会生成受保护的 Release 包，验证输出文件，并生成 SteamPipe VDF；不会启动 SteamCMD，也不会上传内容。

如果当前代码对应的 Release staging 已经成功生成，可以只重建 VDF：

```powershell
.\lucky-dog-rise\Build\Publish-SteamRelease.ps1 -Action Generate -SkipPackageBuild
```

`-SkipPackageBuild` 只能用于确认 staging 与当前提交完全对应的情况。

## 执行 SteamPipe Preview

```powershell
.\lucky-dog-rise\Build\Publish-SteamRelease.ps1 -Action Preview -SkipPackageBuild
```

Preview 会连接 Steamworks，检查账号权限、App、Depot 和文件映射，但不上传游戏内容。

## 上传但不改变任何分支

主人明确批准真实上传后执行：

```powershell
.\lucky-dog-rise\Build\Publish-SteamRelease.ps1 -Action Upload -SkipPackageBuild
```

该命令只在 Steamworks 后台创建新 Build，不会自动改变 `internal` 或 `default` 分支。

正式版首次上传推荐采用这种方式。上传成功后，由主人在 Steamworks 后台检查 Build ID、Depot 清单和内部注释，再手动指派给 `internal`。

## internal 分支验收

Steam 客户端切换到密码保护的 `internal` 分支后，至少检查：

- 能安装并从 Steam 客户端启动正式版 AppID `2583700`。
- Steam Overlay、玩家身份、成就和统计初始化正常。
- Debug 页签和开发入口不可见。
- 桌宠透明窗口、全局输入、扑克模式、音频和盲盒正常。
- 设置页显示 `Release` 渠道和正确版本、提交号。
- 新建存档、重启保留、重置存档均正常。
- Release 存档不读取 Playtest 存档。
- 退出后 Steam 正确恢复为“开始游戏”。

验收通过后，主人可以在 Steamworks 后台将同一 Build 指派给 `default`，再从发行进度页面提交游戏生成版本审核。

## 安全边界

- 正式版入口固定拒绝其它 AppID 和 Depot ID。
- Release 构建拒绝 dirty worktree。
- `steam_appid.txt`、PDB、源码、本地配置和混淆映射不得进入 Depot。
- `Generate` 和 `Preview` 不会上传内容。
- `Upload` 默认不改变任何分支。
- 脚本拒绝通过 `SetLiveBranch` 自动设置 `default`；默认分支必须由主人在 Steamworks 后台手动操作。
- 密码与 Steam Guard 验证码不会写入配置、日志或 Git。
