---
last_editor: Codex
last_edit: 2026-07-11
status: revised
---

# Playtest 打包指南（主人版）

## 这份指南解决什么

这份指南用于主人在当前电脑上生成可以发给外部测试者的 Windows Playtest ZIP。

主人不需要在 Godot 导出窗口中手动填写加密密钥，也不需要每次打包都重新编译 Godot。脚本会自动完成导出、加密、混淆、检查和压缩。

## 当前已经准备好的东西

当前电脑已经具备完整打包环境：

- Godot 4.6.3 源码已经下载。
- Windows x86_64 .NET 自定义打包模板已经编译。
- PCK 加密密钥和存档签名密钥已经生成。
- Godot Mono 编辑器路径已经配置。
- C# 混淆工具已经固定版本。

因此，主人现在**不需要**再下载 Godot 源码，也**不需要**重新编译打包模板。

这些本机文件都放在 `.local-build/` 中，并且不会提交到 Git：

- `.local-build/godot-4.6.3/`：Godot 源码。
- `.local-build/templates/windows_release_x86_64.exe`：自定义 Windows 模板。
- `.local-build/secrets.psd1`：两把密钥，必须保密和备份。

## 平时怎样打 Playtest 包

在资源管理器打开：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026
```

在该文件夹空白处打开 PowerShell，然后执行：

```powershell
.\lucky-dog-rise\Build\Build-WindowsPackage.ps1 -Channel Playtest
```

等待命令完成。看到以下提示表示成功：

```text
Package ready: ...LuckyDogPub-0.3.1-playtest-win-x64.zip
```

最终 ZIP 位于：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026\GameBuild\
```

只需要把 `LuckyDogPub-<版本>-playtest-win-x64.zip` 发给测试者。

不要发送以下内容：

- `.local-build` 文件夹。
- `secrets.psd1`。
- `staging` 文件夹。
- Godot 源码。
- 混淆映射文件。

## 打包脚本做了什么

主人执行一条命令后，脚本会自动：

1. 读取版本号和 Git 短提交号。
2. 使用 Playtest 渠道标记导出 Release 编译。
3. 使用自定义 Godot 模板生成加密 PCK。
4. 混淆游戏 C# 程序集。
5. 删除 PDB、console wrapper 和不应发布的文件。
6. 检查 Debug 入口和敏感文件是否残留。
7. 隐藏启动游戏十秒，检查 PCK、.NET、音频资源和 SFX cue。
8. 所有检查通过后生成 ZIP。

某一步失败时不会生成新的合格 ZIP。主人应保留 PowerShell 中的错误文字，并交给后续开发人员处理，不要为了出包手动关闭加密或混淆。

## dirty 是什么意思

设置页可能显示：

```text
0.3.1 Playtest (1f39c51-dirty)
```

`dirty` 表示打包时工作区中存在尚未提交的修改。这对开发阶段的 Playtest 是允许的，方便快速测试。

但不同的 `dirty` 包可能显示相同 Git 提交号，因此重要外测前最好先提交一次代码，再重新打包。正式 Release 包完全不允许 dirty worktree。

## 是否还要自己编译 Godot 模板

当前电脑不需要，因为模板已经编译完成。

只有以下情况需要重新编译：

- 换了一台电脑。
- `.local-build` 被删除或丢失。
- Godot 从 4.6.3 升级到其它版本。
- PCK 加密密钥更换。
- 自定义模板损坏。

重新准备环境时，后续开发人员需要先确认 Python、SCons、Visual Studio C++、.NET 8、Git 和 Godot 4.6.3 Mono 编辑器可用。

如果是迁移到新电脑，应先从安全备份恢复原来的 `.local-build/secrets.psd1`，再执行：

```powershell
.\lucky-dog-rise\Build\Build-CustomTemplate.ps1
```

只有确认不再沿用任何旧 Playtest/Release 存档和模板，并且确实要建立全新密钥体系时，才执行：

```powershell
.\lucky-dog-rise\Build\Initialize-BuildSecrets.ps1
```

当前电脑已经有密钥，不要重复执行，也不要删除旧密钥后随意重建。

编译 Godot 模板可能耗时较长，但它不是日常打包步骤。模板和密钥没有变化时，可以反复使用同一个模板生成新 Playtest 包。

## 密钥必须怎样处理

主人需要将以下文件备份到仓库以外的安全位置：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026\.local-build\secrets.psd1
```

不要打开后截图，不要发到聊天群，不要提交 Git，不要和测试包放在一起。

丢失密钥会带来两个问题：

- 无法继续使用旧的 PCK 加密模板体系。
- 新版本无法验证使用旧 HMAC 密钥签名的 Playtest 或 Release 存档。

## 正式 Release 怎样打

Release 不是当前日常测试命令。准备正式候选版本前，需要开发人员先完成：

1. 更新游戏版本号。
2. 完成 Playtest 人工验收。
3. 提交所有计划发布的改动。
4. 确认 `git status` 没有修改。

然后执行：

```powershell
.\lucky-dog-rise\Build\Build-WindowsPackage.ps1 -Channel Release
```

如果工作区不干净，脚本会拒绝 Release 构建。这是保护措施，不应绕过。

## 每次发包前主人检查什么

至少检查：

- 设置页显示正确版本和 `Playtest`。
- Debug 页入口不存在。
- 小狗钻出动画结束后，Counter 和真实桌宠正常出现。
- 可以切换桌宠和扑克模式。
- BGM、发牌、翻牌、筹码和敲桌音效正常。
- 主动和被动新手提示正常。
- 可以完成一局扑克。
- 存档重启和重置存档正常。
- 盲盒提示、开盒和领奖流程正常。

正式发给外部测试者前，还需要至少在另一台 Windows 电脑测试一次。当前程序尚未进行 Windows 代码签名，另一台电脑出现 SmartScreen 提示属于已知情况。

## 当前还没完成什么

- 尚未完成另一台 Windows 电脑验收。
- 尚未完成盲盒完整回归。
- 尚未验证主存档和 backup 同时被篡改的处理。
- 尚未生成并验收正式 Release 包。
- 尚未配置 Windows 代码签名。
- 尚未接入 Steam Playtest 分支或 depot 上传。
- `Puppy's Nap Time.mp3` 是否作为正式 BGM 仍需确认授权和最终播放策略。
