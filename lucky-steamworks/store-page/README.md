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
- `text/about/`：各语言“关于此游戏”的可编辑文案。新稿保存为 `.bbcode.txt`，只包含可直接粘贴到 Steam 的 BBCode；既有 `.md` 稿按批次核对，不默认代表当前定稿。
- `text/about/archive/`：旧文字草稿；现有无语言后缀的 `关于此游戏.md` 保留于此。
- `text/short/`：各语言简介 / 概述。第二批起使用不带标题的纯文本 `.txt`；第一批保留现有 `.md`，复制时不包含 Markdown 标题。
- `text/upload/`：整理和翻译后供 Steamworks 导入的 JSON，与下载原件分开保存。
- `text/review/`：各批次的审阅说明、关键句回译、用语依据和本地排版参考。

原始下载的 JSON 中，13 种语言已有长描述和短描述，17 种语言为空。除下面记录的第一批处理范围外，既有 Markdown 仍待按下载原文核对。

JSON 中对应的文字字段是 `app[content][about]` 和 `app[content][short_description]`；文件还带有系统需求等其他字段。

## 第一批审阅

2026-10-14 已整理意大利语、俄语、泰语的完整文案，以及繁体中文的末尾关键词段落。2026-10-15 主人确认本批可以通过并进入第二批；导入与实际页面检查范围见审阅说明。

- 审阅入口：[第一批说明](text/review/batch-01-review.md)。
- 待上传文件：[storepage_1315198_batch-01_review.json](text/upload/storepage_1315198_batch-01_review.json)。
- 原始下载文件保持不变；待上传版本有 16 种完整文案，另 14 种仍为空。

## 第二批审阅

2026-10-15 已补齐荷兰语、丹麦语、挪威语、瑞典语、芬兰语、波兰语、乌克兰语的长描述与简介。主人已提供挪威语实际页面截图，长描述排版与关键语义抽查正常；其余语言及简介的实际排版尚未逐项确认，检查范围见审阅说明。

- 审阅入口：[第二批说明、选词依据与中文回译](text/review/batch-02-review.md)。
- 待上传累积稿：[storepage_1315198_batch-02_review.json](text/upload/storepage_1315198_batch-02_review.json)，包含已确认的第一批内容，只需导入这一份即可。
- 第二批版本有 23 种完整文案，另 7 种仍为空；原始下载和第一批 JSON 保持不变。
- 第三批范围：保加利亚语、匈牙利语、印度尼西亚语、土耳其语、希腊语、罗马尼亚语、马来语。

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
