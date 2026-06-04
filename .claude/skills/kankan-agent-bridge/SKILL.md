---
name: kankan-agent-bridge
description: 多 AI 协作的本地 Issue CLI — 下载即用，多 AI 协作的沟通桥梁
version: 1.0.0
---

# kankan — AI 协作工作流

你是 CLI 翻译层，把用户口语转成 kankan 命令并执行。

**每个新会话开始，先跑 `kankan init`**（幂等，已初始化则无影响）。

`kankan-cli.js` 与本 SKILL.md 在同一目录，调用时自行解析路径。

## 行为约定

用户以 **kankan** 开头的话语都是给你的指令，翻译为 CLI 命令执行。用户用词可能随意（如"记""写""留""看看""查"等），按意图映射，不要抠字面。

### 查看类

- `kankan 收件箱` / `kankan inbox` → `kankan-cli.js inbox`
- `kankan 列表` / `kankan 看看有哪些` → `kankan-cli.js list --oneline`
- `kankan kk-0001` / `kankan 看看 0001` → `kankan-cli.js show <ID>`

### 创建类

用户说 `kankan XXX YYY`（XXX 为类型或动作词，YYY 为描述）：

| 用户可能的词 | 映射 |
|-------------|------|
| 需求、feature、功能、想要、希望 | `create --type feature` |
| bug、问题、报错、故障、bug缺陷 | `create --type bug` |
| 任务、task、todo、待办、记、写、留、留言 | `create --type task` |

用户只说意图没给内容时，先追问再创建。

创建时**直接引用用户原话，一字不改**（包括标点符号）。kankan 的归一化引擎依赖标点拆分章节，改写会破坏拆分效果。

### 更新类

- `kankan 更新 kk-0001 状态 working 接手 张三` → `kankan-cli.js update kk-0001 --status working --owner 张三`
- `kankan 关掉 kk-0001` → `kankan-cli.js update kk-0001 --status closed`

### 其他

- 处理完 inbox 文件后询问用户是否移入 `kankan-data/inbox/.done/`
- 不直接修改 `kankan-data/` 下的文件，始终通过 CLI 操作
