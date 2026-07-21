---
last_editor: Codex
last_edit: 2026-07-21
status: draft
---

# Steam Playtest 首次送审范围决策

## 背景

- 主游戏 AppID：`2583700`。
- Steam Playtest AppID：`4972240`。
- Playtest 商店资产已经获准发行，下一阶段需要准备生成版本审核。
- Steam Playtest 的首次发行流程包含简化商店页审核与生成版本审核，不应只提交一个用于验证 SteamPipe 上传的普通游戏包。
- Playtest 与主游戏是相互独立的 AppID。Steam 后台的成就、统计和其它应用级配置需要分别配置，Playtest 数据不会自然继承到主游戏。

官方参考：

- [Steam 游戏测试](https://partner.steamgames.com/doc/features/playtest)
- [Steamworks SDK](https://partner.steamgames.com/doc/sdk)
- [Steamworks API 概览](https://partner.steamgames.com/doc/sdk/api)
- [上传至 Steam](https://partner.steamgames.com/doc/sdk/uploading)
- [生成版本与测试分支](https://partner.steamgames.com/doc/store/application/builds)

## 本次决策

第一次送审版本定义为“Steam 基础闭环版”，在提交审核前完成最小 Steam API 接入，并复用已经完成的游戏内成就和统计系统。

首次送审版本计划包含：

- Steam API 初始化、回调处理和关闭。
- Steam 不可用时安全降级，不阻塞 Godot 编辑器和普通本地开发。
- 识别当前 Steam 用户与当前运行的 AppID。
- 验证 Steam Overlay。
- 将游戏内成就同步到 Steam 成就。
- 将适合的平台统计同步到 Steam Stats。
- Debug 环境保留成就快速测试手段；正式 Playtest 中，随机获得道具、发筹码和固定牌局等 Debug 操作不得触发成就或正式统计。
- 保留现有 Playtest 渠道的 PCK 加密、C# 混淆、存档 HMAC、Debug 隔离和渠道隔离。
- 完成桌宠模式、切换扑克模式、完成一局牌局并触发至少一个真实成就的基础闭环验收。

相关构建防护与验收门槛继续以 [Playtest 构建隔离与基础防护](playtest-build-protection.md) 为准。

## 暂缓范围

以下功能不作为第一次 Playtest 生成版本审核的前置条件：

- Steam Inventory 与正式装扮库存。
- 盲盒的 Steam 发奖和库存同步。
- Steam Cloud。
- Rich Presence。
- Steam Input 或完整手柄支持。
- 自动化 SteamPipe 发布工具和 CI/CD 上传。

这些功能的复杂度和失败面较大，应在 Steam 基础闭环稳定后分别设计、实现和测试。

## 日常开发原则

- Steam 平台能力通过统一服务接口接入，游戏逻辑不直接散落调用第三方绑定。
- 本地开发使用空实现或降级实现；Steam 初始化失败时只影响 Steam 专属能力，不应阻止游戏启动。
- 开发环境可以使用 `steam_appid.txt` 辅助直接启动测试，但该文件不得进入 Steam Depot。
- Playtest 与正式游戏的 AppID、Steam 后台配置和构建配置必须明确隔离，不在业务代码中到处硬编码 AppID。

## 审核通过后的更新测试

首次生成版本审核通过后，立即验证 Steam 安装与更新链路：

1. 从 Steam 客户端安装并运行首次审核版本。
2. 记录游戏版本、存档、装备、成就和统计状态。
3. 上传带有明确版本变化的第二个 Playtest 构建。
4. 验证 Steam 增量更新和启动项。
5. 验证更新后存档、装备、成就和统计数据不丢失。
6. 在必要时验证切换测试分支或回滚旧构建。

## 下一步

1. 确认 Godot 4.6.3 Mono 项目采用的 Steamworks 第三方绑定及其打包方式。
2. 设计最小 Steam 平台服务接口、空实现和 Steam 实现。
3. 确认 Playtest 侧首批测试成就和统计 API Name。
4. 实现并完成本地降级、Steam 启动、Overlay、成就和统计测试。
5. 配置 Playtest 启动项、Depot、程序包和 SteamPipe 上传脚本。
6. 生成首个具有实际测试价值的 Playtest 审核包并提交审核。
