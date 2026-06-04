---
name: editor-signature-rules
description: 编辑设计文档时自动加载。为设计文档维护 YAML front matter 签名（last_editor、last_edit、status）。
---

# 文档签名规则

## 说明

仅项目设计文档需要包含 YAML front matter（至少包含最后编辑者与时间）。  
Skill、MCP 配置文件等其他文档不需要。

## 格式

```yaml
---
last_editor: 主人
last_edit: 2026-04-29
---
```

## 规则

1. 谁最后编辑文档，将 `last_editor` 更新为自己的标识
2. `last_edit` 填写编辑日期（YYYY-MM-DD）
3. 仅记录最后一次编辑，不保留历史

## 标识映射

- `主人` — 秋哥（项目决策者）
- `Cline` — AI 助手（Qwen）
- `Claude` — AI 助手（DeepSeek）
- `OpenClaw` — AI 助手（Qwen）
- `TRAE` - AI 助手 (Qwen)

## 文档状态（优先级从低到高）

- `draft` — 头脑风暴/讨论中形成的记录，有较大变更空间，冲突时可修改
- `revised` — 主人（你）认为修改得差不多了，可以先去审阅其他关联文档
- `approved` — 模块内所有关联文档均已处理完毕，可交给编程AI开发
- `final` — 不可修改的公理

**公理规则**：冲突时以状态更高的文档为准（`final` > `approved` > `revised` > `draft`）。同状态时，以 `last_edit` 更新的为准。

