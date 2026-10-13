# Steam 商店页描述与插图

本目录只管理两类材料：

- 商店页文字：“关于此游戏”（长描述）和“简介 / 概述”（短描述）。
- 描述正文实际引用的插图、动图及其制作源文件。

商店页顶部的宣传视频、截图、胶囊图、库资产，以及审核、构建、Steam 库存等文档不在本目录维护。

## 手动下载放置位置

在 Steamworks 的“本地化”选项卡选择：

- 格式：`JSON`。
- 语言：`所有语言`。
- 点击“下载本地化”。

下载文件放入 `text/downloads/`，保留 Steamworks 给出的原文件名。

当前打开的页面是 **Lucky Dog Rise Demo（AppID 5220880）**，对应的下载文件为：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026\lucky-steamworks\store-page\text\downloads\storepage_1315198_all.json
```

其中 `1315198` 是当前商店页编辑记录 ID，不是游戏 AppID。主游戏与 Demo 的导出需要分别识别来源，不能直接混合或互相覆盖。

如需保留多次下载，可在 `text/downloads/` 下建立日期子目录，再放入保持原名的文件。

## 文字目录

- `text/downloads/`：Steamworks 手动下载的原始 JSON；原样保留，作为该次核对的网页内容依据。
- `text/about/`：各语言“关于此游戏”的可读、可编辑 Markdown 文档。
- `text/about/archive/`：旧文字草稿；现有无语言后缀的 `关于此游戏.md` 保留于此。
- `text/short/`：各语言简介 / 概述的可读、可编辑 Markdown 文档。
- `text/upload/`：整理和翻译后供 Steamworks 导入的 JSON，与下载原件分开保存。

当前归入 `text/about/` 的 12 种语言文件保持原文，尚未与最新手动下载文件同步；不能将其视为网页最新版本。其余语言后续以下载内容核对和补齐。

后续整理范围只涉及 `app[content][about]` 和 `app[content][short_description]`。下载 JSON 中可能包含系统需求等其他字段，保留原件时不删改，也不因本目录整理而改写这些字段。

## 插图目录

- `illustrations/source/`：用于描述插图的制作源文件，例如 PSD。
- `illustrations/upload/`：供 Steamworks 描述正文引用的图片或动图成品。

成品名称应与 Steamworks 中引用的资源名对应；有语言版本时保留语言标识。正文中的 `{STEAM_APP_IMAGE}/extras/...` 引用和排版标签在同步时需要一并保留。

本次只建立插图目录，未复制、下载或重新生成插图。

## 原目录其他材料的去向

原文件均保留，仅调整位置：

- Steam Inventory 阅读笔记和 ItemDef 字段设计：`../steam-item-def/`。
- Playtest 构建保护和首次送审决策：`../playtest/`。
- 竞品列表：`../marketing-data/竞品列表.md`。
- 旧商店页清单、材料准备包、提交总结和宣传视频录制脚本：`../archive/store-page/`。

历史文档中的旧路径和当时的提交状态保留原样，不作为本目录当前文字版本的依据。
