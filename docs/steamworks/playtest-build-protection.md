---
last_editor: Codex
last_edit: 2026-07-11
status: revised
---

# Playtest 构建隔离与基础防护

## 目标与边界

Playtest 测试者可能来自 Godot 开发者群或其它半公开渠道，不按可信内部成员处理。本轮防护目标是：

- 默认 Godot PCK 解包工具不能在不知道密钥时列出并提取完整资源。
- 外部包不包含可达的 Debug 页、全道具数据源、赠送筹码和随机道具等开发入口。
- 普通文本修改 Playtest 或 Release 存档后不能直接加载作弊数据。
- 日常编辑器运行和 Debug 编译保持原有开发效率。

本方案不承诺防住专业逆向、内存修改、DLL 注入或从客户端二进制中提取密钥，也不引入重型反作弊。

## 构建渠道

项目只维护三个渠道：

- Dev：用于编辑器和日常开发，保留 Debug 能力，不要求 PCK 加密，不启用混淆。
- Playtest：用于半公开外部测试，编译移除 Debug 能力，启用 PCK 加密和 C# 混淆。
- Release：用于正式发布候选，采用 Playtest 防护，并额外要求 clean worktree。

`BuildInfo` 是渠道判断的唯一入口：

- `DEBUG` 编译固定为 Dev。
- Playtest 由导出 feature tag `lucky_playtest` 识别。
- Release 由导出 feature tag `lucky_release` 识别。
- 非 Debug 编译缺少渠道 tag、同时存在多个 tag 或缺少合法存档密钥时，主场景启动后立即报错退出。
- 设置页底部显示版本、渠道和 Git 短提交号；Playtest 允许 dirty worktree，并在提交号后追加 `dirty`。
- Release 构建要求 clean worktree。

实现入口：

- `lucky-dog-rise/Scripts/BuildInfo.cs`
- `lucky-dog-rise/Scripts/ModeManager.cs`
- `lucky-dog-rise/Scenes/SystemPanel.tscn`

## Debug 隔离

Debug 页信号、随机狗、随机场景、随机道具、赠送筹码、全道具数据源和相应处理方法均使用 `#if DEBUG` 编译隔离。

外部构建启动系统面板时会将 `DebugTab` 和 `DebugContent` 从场景树中释放，而不是只设置 `Visible=false`。非 Debug 编译还会强制使用 `LocalSave`，忽略配置文件中遗留的 `DebugAllItems` 值。

新增开发入口时，需要同时满足：

1. UI 信号、处理方法和数据源使用 `#if DEBUG`。
2. 不从普通玩家路径调用 Debug 逻辑。
3. `Verify-Build.ps1` 中的敏感符号检查按需扩充。

## 存档与设置隔离

渠道通过 Godot feature-tag 项目设置覆盖 `user://`：

- Dev：保留现有目录。
- Playtest：`%APPDATA%\LuckyDogRise\Playtest`。
- Release：`%APPDATA%\LuckyDogRise\Release`。

Playtest 不迁移到 Release。Dev 可读取旧的未签名存档，并在下次保存时升级；Playtest 和 Release 拒绝未签名存档。

`SaveProfile` 使用以下完整性字段：

- `IntegrityVersion = 1`
- `IntegrityTag = HMAC-SHA256(...)`

签名使用独立 256 位密钥。计算前移除 `IntegrityTag`，对稳定排序后的紧凑 JSON 计算 HMAC。验签使用固定时间比较，日志不打印密钥、原始签名或规范化内容。

加载顺序为：

1. 验证主存档。
2. 主存档无效时验证 backup。
3. 有效 backup 恢复为主存档。
4. 两者都无效时保留 `profile_0.invalid_signature.json` 并创建新档。

实现入口：

- `lucky-dog-rise/Scripts/Desktop/SaveIntegrity.cs`
- `lucky-dog-rise/Scripts/Desktop/SaveManager.cs`
- `lucky-dog-rise/project.godot`

## 密钥管理

构建使用两把互相独立的 256 位密钥：

- PCK AES 密钥：编译进自定义 Windows template，并用于导出资源包。
- 存档 HMAC 密钥：通过 MSBuild assembly metadata 编入游戏程序集。

本机密钥位于被 Git 忽略的 `.local-build/secrets.psd1`。构建脚本不得打印密钥。主人需要将该文件另行离线备份；丢失 PCK 密钥后无法继续使用旧模板，丢失 HMAC 密钥后新版本无法验证旧存档。

初始化命令：

```powershell
.\lucky-dog-rise\Build\Initialize-BuildSecrets.ps1
```

不得提交或发送以下文件：

- `.local-build/secrets.psd1`
- `.godot/export_credentials.cfg`
- `.local-build/maps/`
- 生成的 Obfuscar XML 和构建中间目录

## 自定义 Windows 模板

加密 PCK 必须由带相同密钥编译的自定义 Godot template 读取，官方预编译模板不能读取本项目的加密包。当前模板固定为 Godot `4.6.3-stable`、Windows x86_64、.NET Release：

```text
target=template_release
module_mono_enabled=yes
production=yes
lto=none
debug_symbols=no
```

模板输出到 `.local-build/templates/windows_release_x86_64.exe`。脚本保存密钥指纹；密钥变化或旧模板没有指纹时，会先清理 SCons 产物再完整重编，避免误用无密钥模板。

### 当前本机状态

截至 2026-07-11，当前工作区已经完成：

- Godot `4.6.3-stable` 源码位于 `.local-build/godot-4.6.3/`。
- 带当前 PCK 密钥的 Windows x86_64 .NET Release 模板位于 `.local-build/templates/windows_release_x86_64.exe`。
- 模板密钥指纹位于 `.local-build/templates/pck-key.sha256`。
- `lucky-dog-rise/Build/LocalConfig.psd1` 已配置当前 Godot Mono 编辑器和源码路径。

因此，当前电脑后续生成 Playtest 包时不需要重新下载源码或重新编译模板。只有出现以下情况时才重新执行模板编译：

- 更换电脑或 `.local-build` 丢失。
- Godot 项目版本从 `4.6.3` 升级。
- PCK 加密密钥发生变化。
- 自定义模板损坏或被删除。

首次配置：

1. 复制 `lucky-dog-rise/Build/LocalConfig.example.psd1` 为被忽略的 `LocalConfig.psd1`。
2. 设置 Godot 4.6.3 Mono 编辑器路径和源码路径。
3. 执行：

```powershell
.\lucky-dog-rise\Build\Build-CustomTemplate.ps1
```

Godot 参考：[Feature Tags](https://docs.godotengine.org/en/4.0/tutorials/export/feature_tags.html)、[PCK Encryption](https://docs.godotengine.org/en/4.6/contributing/development/compiling/compiling_with_script_encryption_key.html)、[Data Paths](https://docs.godotengine.org/en/4.6/tutorials/io/data_paths.html)。

## C# 混淆

仓库通过 `.config/dotnet-tools.json` 固定 `Obfuscar.GlobalTool 2.2.50`。只处理 `LuckyDogRise.dll`，不处理 GodotSharp、.NET Runtime 或第三方程序集。

保留规则位于 `lucky-dog-rise/Build/godot-obfuscation-preserve.txt`。构建会扫描 Godot 派生类；发现新增节点类未加入保留列表时直接失败。Godot 节点类的类型名和全部方法名均保留，避免私有信号回调、`CallDeferred` 和生成绑定被改名；Godot 生成入口 `GodotPlugins.Game.Main.InitializeFromGameProject` 也必须保留名称。

混淆映射保存在 `.local-build/maps/<版本>/<渠道>/`。混淆失败、入口被改名或混淆后运行异常都使构建失败，不回退到未混淆 DLL。

## 构建命令

Playtest：

```powershell
.\lucky-dog-rise\Build\Build-WindowsPackage.ps1 -Channel Playtest
```

Release：

```powershell
.\lucky-dog-rise\Build\Build-WindowsPackage.ps1 -Channel Release
```

流水线固定执行：生成本地预设、Release 导出、PCK 文件与目录加密、C# 混淆、删除 PDB/console wrapper、产物检查、隐藏启动冒烟测试、生成 ZIP。

包名：`LuckyDogPub-<version>-<channel>-win-x64.zip`。

产物不得包含松散的 PDB、C# 源码、PSD、Excel、Python、Markdown、密钥、混淆映射、旧 layer index 或 console wrapper。游戏运行所需资源位于加密的内嵌 PCK。`layer_index.json` 的本机 PSD 绝对路径已清理。

Windows Authenticode 签名暂未实施；检查报告明确标记 `unsigned`，其它电脑可能显示 SmartScreen 警告。

### 开发人员日常操作

日常开发继续使用 Godot 编辑器或 Debug 编译，不运行加密和混淆流程。准备阶段性外部测试包时，在仓库根目录执行：

```powershell
.\lucky-dog-rise\Build\Build-WindowsPackage.ps1 -Channel Playtest
```

成功后只发送 `GameBuild/LuckyDogPub-<version>-playtest-win-x64.zip`，不要发送 `.local-build`、源码目录或 staging 目录。

准备正式 Release 前需要：

1. 更新 `project.godot` 中的版本号。
2. 提交所有计划进入版本的改动。
3. 确认 `git status` 干净。
4. 执行 Release 构建命令。
5. 在另一台 Windows 电脑完成完整人工验收。

新增 Godot 节点类后，如果构建报告该类型未进入混淆保留列表，需要将类型名加入 `lucky-dog-rise/Build/godot-obfuscation-preserve.txt`，不能通过关闭混淆绕过。

新增通过字符串动态加载的资源时，必须同步更新 `New-ExportPresets.ps1` 的 `include_filter`，或改为静态资源引用。音频当前通过 `Audio/**/*.ogg`、`Audio/**/*.wav`、`Audio/**/*.mp3` 显式纳入导出。

## 自动检查

`Verify-Build.ps1` 检查禁止文件、游戏程序集、Debug 敏感符号和签名状态。`Test-ExportedRuntime.ps1` 会隐藏启动最终 EXE 十秒，并拦截以下问题：

- PCK 或项目数据无法加载。
- Godot .NET 初始化入口缺失。
- 未处理异常或脚本错误。
- 关键参数为空。
- SFX 目录或必需音频资源缺失。
- `Card_PokerHandDeal` 代表性 SFX cue 无法解析到变体文件。

只有检查通过后才会生成 ZIP。

## 2026-07-11 实际验证

已完成：

- Debug 与 Release 编译均为 0 错误、0 警告。
- `SystemPanel.tscn` 的版本标签 Export NodePath 已通过场景树检查。
- 自定义 Godot 4.6.3 .NET template 可读取加密内嵌 PCK。
- 混淆后的 Playtest 包通过隐藏启动测试，窗口输入 hook 正常安装。
- Playtest 存档写入 `%APPDATA%\LuckyDogRise\Playtest`，初始筹码为 1000，签名长度为 64。
- 将主档筹码改为 `987654321` 且保留旧签名后，主档被拒绝并从有效 backup 恢复为 1000。
- GDRE Tools `2.6.0-beta.4` 在不给密钥时报告 `Incorrect encryption key`，列出和提取的文件数均为 0。[GDRE Tools](https://github.com/GDRETools/gdsdecomp)
- Playtest 包内没有 PDB、松散源码、密钥或混淆映射。
- 主人已确认 Debug 页入口不可见，版本显示为 `0.3.1 Playtest (<Git短提交号>-dirty)`。
- 主人已确认启动钻出动画完成后，Counter 和真实桌宠正常显示。
- 主人已确认扑克主流程、主动新手提示、被动新手提示和重置存档功能正常。
- 已修复混淆器改名 Godot 私有信号回调导致 Intro 交接失效的问题；Godot 节点类的全部方法名现已保留。
- 已修复动态加载音频未进入导出包的问题；音频格式现由导出预设显式包含。
- 已修复加密 PCK 中无法通过 `DirAccess` 枚举 SFX 变体的问题；变体改由 `ResourceLoader.Exists` 探测 `_1` 至 `_64`。
- 主人已确认 `Puppy's Nap Time.mp3` BGM、扑克 SFX 和系统音效开关工作正常。

当前产物：`GameBuild/LuckyDogPub-0.3.1-playtest-win-x64.zip`。

## 尚未完成

- 尚未在另一台 Windows 电脑验证启动、音频、透明窗口、全局输入、存档目录和 SmartScreen 行为。
- 尚未完成盲盒完整人工回归：投放提示、消耗、开盒、中断恢复、领奖和自动领奖。
- 尚未验证主存档与 backup 同时被篡改时，是否按预期保留 `profile_0.invalid_signature.json` 并创建新档。
- 尚未生成并人工验收真正的 Release 渠道包；当前工作区有未提交改动，Release 构建会按设计拒绝执行。
- 尚未购买或配置 Windows Authenticode 代码签名证书。
- 尚未接入 Steam Playtest 分支或 depot 上传流程。
- 尚未决定 `Puppy's Nap Time.mp3` 是否作为正式 BGM，包括授权、循环点、音量和平滑停止策略。
- 当前防护仍不抵抗专业逆向、内存修改和从客户端提取密钥，这属于明确的非目标。

## 后续操作

开发人员接手后按以下顺序推进：

1. 将 `.local-build/secrets.psd1` 备份到仓库外的安全位置，且不得通过聊天、Issue 或提交记录传播密钥内容。
2. 完成盲盒和存档双篡改测试，并将结果更新到本文档。
3. 在另一台 Windows 电脑测试当前 Playtest ZIP，记录系统版本、SmartScreen、音频设备和透明窗口结果。
4. 确认 BGM 授权和最终播放策略；如替换文件，保持动态音频导出规则和启动资源检查同步更新。
5. 整理并提交当前实现，使 worktree 干净。
6. 更新游戏版本号，生成 Release 包并重复 Playtest 的完整人工验收。
7. 根据 Steam 发布安排接入 Playtest 分支或 depot 上传。
8. 临近公开发布时再决定是否购买代码签名证书；该事项不阻塞当前 Playtest。

上述人工验收是 Playtest 发包门槛；本次自动验证不等同于视觉和完整玩法验收。
