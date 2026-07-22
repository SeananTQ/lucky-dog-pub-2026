---
last_editor: Codex
last_edit: 2026-07-22
status: revised
---

# Steam Playtest 上传指南（主人版）

## 这套工具解决什么

这套工具把现有受保护 Playtest 打包流程接到 SteamPipe。上传内容仍然来自 `Build-WindowsPackage.ps1` 生成并验证过的 staging 目录，不会绕过 PCK 加密、C# 混淆、存档签名和 Debug 隔离。

脚本分为三种动作：

- `Generate`：只生成并检查 SteamPipe VDF，不启动 SteamCMD，也不上传。
- `Preview`：让 SteamCMD执行预览构建，不上传内容。
- `Upload`：真实上传。没有额外指定分支时，只创建 Steam 后台 Build，不自动设为任何分支的 Live Build。

因此，默认命令不会影响玩家可见分支。

## 第一次使用前配置

在 Steamworks 后台打开 Lucky Dog Rise Playtest（AppID `4972240`）的 SteamPipe Depot 页面，复制 Windows Depot ID。

将下面的示例文件复制一份：

```text
lucky-dog-rise\Build\SteamPipeConfig.example.psd1
```

复制后的文件名必须是：

```text
lucky-dog-rise\Build\SteamPipeConfig.psd1
```

填写内容：

```powershell
@{
    AppId = 4972240
    DepotId = 这里填写后台的 Windows Depot ID
    SteamAccount = '这里填写有上传权限的 Steamworks 账号名'
}
```

`SteamPipeConfig.psd1` 已被 Git 忽略。文件只保存账号名，不保存密码和 Steam Guard 验证码。

脚本会固定检查 Playtest AppID 必须是 `4972240`。不要将主游戏 AppID `2583700` 填进该文件。

## 先做无上传检查

在工作区根目录执行：

```powershell
.\lucky-dog-rise\Build\Publish-SteamPlaytest.ps1 -Action Generate
```

该命令会：

1. 重新生成受保护 Playtest 包。
2. 再次检查 Steamworks 运行库、Debug 文件和 `steam_appid.txt`。
3. 生成 App 与 Depot 的 VDF。
4. 停止在本地，不启动 SteamCMD。

VDF 和 SteamPipe 缓存位于：

```text
.local-build\steampipe\playtest\
```

这些本地中间文件不会提交 Git。

如果刚刚已经成功打过当前代码的 Playtest 包，可以使用：

```powershell
.\lucky-dog-rise\Build\Publish-SteamPlaytest.ps1 -Action Generate -SkipPackageBuild
```

`-SkipPackageBuild` 只适合确认 staging 确实对应当前代码时使用。正式上传前建议省略它。

## 执行 SteamPipe 预览

确认 Generate 通过后执行：

```powershell
.\lucky-dog-rise\Build\Publish-SteamPlaytest.ps1 -Action Preview
```

SteamCMD 可能要求输入密码和 Steam Guard 验证码。验证信息只应在 SteamCMD 交互窗口中输入，不应写入脚本、配置文件、聊天记录或 Git。

Preview 用于验证 Steamworks 权限和 SteamPipe 配置，不会上传游戏内容。

## 真实上传但暂不设为 Live

执行：

```powershell
.\lucky-dog-rise\Build\Publish-SteamPlaytest.ps1 -Action Upload
```

上传成功后，Steamworks 后台会出现新的 Build，但脚本不会自动把它设为默认分支或测试分支的 Live Build。主人可以先在后台检查 Build、Depot 和描述，再手动指派给密码保护分支。

这是第一次上传时推荐的方式。

## 明确指派到已有测试分支

只有目标分支已经在 Steamworks 后台创建并确认名称后，才使用：

```powershell
.\lucky-dog-rise\Build\Publish-SteamPlaytest.ps1 -Action Upload -SetLiveBranch internal
```

将 `internal` 替换成后台的真实分支名。该参数会让上传成功的 Build 直接成为目标分支的 Live Build，因此不应在不确定分支名时尝试。

脚本不负责创建分支密码。密码保护仍在 Steamworks 后台设置。

## 上传后的 Steam 客户端验收

首次 Build 应至少检查：

- Steam 客户端能安装并启动 Lucky Dog Rise Playtest。
- 游戏显示 Playtest 渠道和正确版本号。
- 发布目录没有 `steam_appid.txt`。
- Steam Overlay 可以打开。
- Steam 登录身份和 AppID 正确。
- 桌宠透明窗口、全局输入 hook、扑克模式和音频正常。
- 本地已满足且后台已配置的成就可以同步到 Steam。
- 退出并重新启动后，存档与成就状态保留。

第二个 Build 应修改一个容易辨认且不破坏存档的内容，再上传到相同测试分支，用于验证 Steam 自动下载差异更新和旧存档兼容。

## 安全边界

- 脚本固定拒绝非 `4972240` 的 AppID。
- Depot ID 未填写时拒绝生成。
- 构建中存在 `steam_appid.txt`、PDB、源码或本地配置时拒绝生成。
- `Generate` 和 `Preview` 不会上传正式内容。
- `Upload` 默认不自动设为 Live。
- 只有显式提供 `-SetLiveBranch` 才自动改变分支指向。
- Steam 密码和 Steam Guard 验证码不落盘。

Steamworks 后台仍应只向必要账号授予上传权限，并保留 Steam Guard。
