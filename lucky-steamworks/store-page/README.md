# Steam 商店页描述与插图

本目录维护两类材料：

- 商店页文字：“关于此游戏”（长描述）和“简介 / 概述”（短描述）。
- 描述正文实际引用的插图、动图、短视频及其制作源文件。

AI 的长期工作规则见 [AGENTS.md](AGENTS.md)。商店页顶部的宣传视频、截图、胶囊图、库资产，以及审核、构建、Steam 库存等文档在目录外维护。

Demo 与正式版商店页共用本目录的一套长描述和短描述。

## 手动下载放置位置

在 Steamworks 的“本地化”选项卡选择：

- 格式：`JSON`。
- 语言：`所有语言`。
- 点击“下载本地化”。

下载文件放入 `text/downloads/`，保留 Steamworks 给出的原文件名。

当前已放入的文件来自 **Lucky Dog Rise Demo（AppID 5220880）**：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026\lucky-steamworks\store-page\text\downloads\storepage_1315198_all.json
```

其中 `1315198` 是商店页编辑记录 ID，不是游戏 AppID。两页共用文字，但导出文件各自带有所属页面的身份信息。

如需保留多次下载，可在 `text/downloads/` 下建立日期子目录，再放入保持原名的文件。

## 文字目录

- `text/downloads/`：Steamworks 手动下载的原始 JSON；原样保留，作为该次核对的网页内容依据。
- `text/about/`：各语言“关于此游戏”的可读、可编辑 Markdown 文档。
- `text/about/archive/`：旧文字草稿；现有无语言后缀的 `关于此游戏.md` 保留于此。
- `text/short/`：各语言简介 / 概述的可读、可编辑 Markdown 文档。
- `text/upload/`：整理和翻译后供 Steamworks 导入的 JSON，与下载原件分开保存。

目前 `text/about/` 的 12 种语言文件仍为整理目录前的原文，尚未与下载文件同步。最新下载的 JSON 中，13 种语言已有长描述和短描述，17 种语言为空。

JSON 中对应的文字字段是 `app[content][about]` 和 `app[content][short_description]`；文件还带有系统需求等其他字段。

## 插图目录

- `illustrations/source/`：用于描述插图的制作源文件，例如 PSD。
- `illustrations/upload/`：供 Steamworks 描述正文引用的图片、动图或短视频成品。

当前正文使用的资源组：

- `adoptionbox`：纸箱中的小狗插图。
- `store-dialogue-v009`：正文展开后可见的动画 / 视频；主人已在 Steam 后台上传 30 种语言版本。

这两个资源组的引用保存在正文 JSON 中，媒体文件和完整的语言映射不在该 JSON 内。目前本地插图目录为空。

## 原目录其他材料的去向

原文件均保留，仅调整位置：

- Steam Inventory 阅读笔记和 ItemDef 字段设计：`../steam-item-def/`。
- Playtest 构建保护和首次送审决策：`../playtest/`。
- 竞品列表：`../marketing-data/竞品列表.md`。
- 旧商店页清单、材料准备包、提交总结和宣传视频录制脚本：`../archive/store-page/`。

历史文档中的旧路径和当时的提交状态保留原样。
