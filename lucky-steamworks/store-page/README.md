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

最初放入的文件来自 **Lucky Dog Rise Demo（AppID 5220880）**：

```text
G:\Workspace\godot-project\lucky-dog-pub-2026\lucky-steamworks\store-page\text\downloads\storepage_1315198_all.json
```

其中 `1315198` 是商店页编辑记录 ID，不是游戏 AppID。两页共用文字，但导出文件各自带有所属页面的身份信息。

2026-10-15 补充正式版 **Lucky Dog Rise（AppID 2583700）** 的原始导出：[storepage_602645_all_Main.json](text/downloads/storepage_602645_all_Main.json)。保留主人添加的 `_Main` 文件名标记；实际正式版身份已通过 JSON 内的字符串 `itemid = "602645"` 核对，不靠文件名判断。

如需保留多次下载，可在 `text/downloads/` 下建立日期子目录，再放入保持原名的文件。

## 文字目录

- `text/downloads/`：Steamworks 手动下载的原始 JSON；原样保留，作为该次核对的网页内容依据。
- `text/about/`：各语言“关于此游戏”的可编辑文案。新稿保存为 `.bbcode.txt`，只包含可直接粘贴到 Steam 的 BBCode；既有 `.md` 稿按批次核对，不默认代表当前定稿。
- `text/about/archive/`：旧文字草稿；现有无语言后缀的 `关于此游戏.md` 保留于此。
- `text/short/`：各语言简介 / 概述，整理稿使用不带标题的纯文本 `.txt`；此前补译第一批的意大利语、俄语、泰语仍保留 `.md`，另列格式收尾，复制时不包含 Markdown 标题。
- `text/upload/`：整理和翻译后供 Steamworks 导入的 JSON，与下载原件分开保存。
- `text/review/`：各批次的审阅说明、关键句回译、用语依据和本地排版参考。

最初 Demo 下载的 JSON 中，13 种语言已有长描述和短描述，17 种语言为空；本次正式版原始导出也是同一填充范围。前面的三个补译批次已补齐 17 种空白语言；原有 13 种语言按[原文备份与尾段补充分批](text/review/existing-languages-plan.md)执行，不重新翻译正文或简介。三个整理批次均已完成本地文件，共 8 种新增尾段待审阅与页面抽查。

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

## 第三批与完整累积稿

2026-10-15 已补齐第三批 7 种语言的长描述与简介，当前完整累积 JSON 中 30 种语言均有长短描述。主人已提供马来语实际页面截图，长描述排版与关键语义抽查正常；其余语言及简介的实际排版尚未逐项确认，检查范围见审阅说明。

- 审阅入口：[第三批说明、选词依据与中文回译](text/review/batch-03-review.md)。
- **Demo 补译完成快照**：[storepage_1315198_batch-03_review.json](text/upload/storepage_1315198_batch-03_review.json)，包含原始已有内容及三个补译批次。此文件保持不变；追加原有语言尾段的最新完整导入稿见下文。
- 本文件保留 Demo 编辑记录 `1315198` 的身份。正式版共用文字，但必须以正式版自身导出身份生成导入文件。
- 原始下载和前两批 JSON 保持不变。30/30 表示字段填齐，不表示全部旧 Markdown 已核对，也不表示全部语言已完成页面预览或母语审校。

## 正式版共用文案导入稿

2026-10-15 已以正式版原始导出为底稿，同步上述 30 种语言的共用长描述与简介。

- **正式版首次同步快照**：[storepage_602645_all_Main_review.json](text/upload/storepage_602645_all_Main_review.json)。上传目标为正式版商店编辑记录 `602645`；此文件保持不变，最新完整导入稿见下文。
- 仅同步 `app[content][about]` 和 `app[content][short_description]`，全部 60 个文字字段与 Demo 完整累积稿相同；正式版身份、系统需求和其他字段保留正式版原件值。
- 核对记录：[正式版同步说明](text/review/main-sync-review.md)。下载原件按字节归档，下载目录外的主人文件也保持原样。
- 主人已提供正式版芬兰语、荷兰语实际页面截图，两种语言的长描述排版与关键语义抽查通过；其余语言及简介仍待页面核对。AI 没有执行 Steam 上传、保存或发布，静态截图不替代完整媒体播放验证。

## 原有语言整理分组

详见[原有语言整理分批与文件盘点](text/review/existing-languages-plan.md)。本轮采用 3＋5＋5：

- 第一批：简体中文、英语、日语。
- 第二批：法语、德语、捷克语、韩语、越南语。
- 第三批：繁体中文、西班牙语（西班牙／拉丁美洲）、葡萄牙语（葡萄牙／巴西），共 5 种。

长描述从当前已确认 JSON 原样备份为 `.bbcode.txt`，简介备份为 `.txt`，对应旧 Markdown 原样归档。英语、中文（简体与繁体）、日语、捷克语保留既有尾段；其余 8 种已有语言仅研究并追加缺失的尾部关键词段，原正文与简介不改。意大利语、俄语、泰语的遗留 Markdown 单列为格式收尾。

### 原有语言第一批：JSON 原文备份

2026-10-15 已将简体中文、英语、日语的完整 BBCode 与简介从已确认 JSON 原样备份到本地，并将三份旧 Markdown 原样移入归档。此前临时进行的正文排版、英日邀请语、简介及关键词尾段改写已撤回，说明见[第一批备份记录](text/review/existing-01-review.md)。本批只做格式归档，无需重新上传。

| 页面 | 第一批备份来源 JSON | 编辑记录 ID |
| --- | --- | --- |
| Demo | [storepage_1315198_batch-03_review.json](text/upload/storepage_1315198_batch-03_review.json) | `1315198` |
| 正式版 | [storepage_602645_all_Main_review.json](text/upload/storepage_602645_all_Main_review.json) | `602645` |

两份文件均含 30 种语言，全部 60 个文字字段保持共用。本批没有修改任何 JSON；两份临时改写的 `existing-01` 上传稿已撤回删除。原始下载和此前 JSON 快照保持原样，没有执行后台上传、保存或发布。

### 原有语言第二批

2026-10-15 已将法语、德语、捷克语、韩语、越南语保存为完整 BBCode 和独立简介，并原样归档四份旧 Markdown。仅法、德、韩、越在原正文媒体之后追加关键词段；捷克语已有尾段保留。所有原正文及简介均不改写。

- 审阅入口：[第二批选词依据、中文回译与文件入口](text/review/existing-02-review.md)。新增尾段待主人审阅及 Steam 页面抽查。
- **Demo 第二批快照**：[storepage_1315198_existing-02_review.json](text/upload/storepage_1315198_existing-02_review.json)，编辑记录 `1315198`。
- **正式版第二批快照**：[storepage_602645_existing-02_Main_review.json](text/upload/storepage_602645_existing-02_Main_review.json)，编辑记录 `602645`。
- 两份均含完整 30 种语言，可分别导入对应页面，无需手动合并。相比各自起始快照，仅四个长描述字段追加尾段；其余语言、全部简介、各自身份及非文字字段保留。
- 原始下载和此前 JSON 快照保持原样。没有执行 Steam 上传、保存或发布；当前最新累积稿见第三批。

### 原有语言第三批与当前完整导入稿

2026-10-15 已整理繁中、西语两地区版、葡语两地区版的完整 BBCode 与独立简介，并原样归档五份旧 Markdown。繁中保留已确认尾段；仅四种地区版本在原正文媒体后各追加一个关键词段。原正文与全部简介不改写。

- 审阅入口：[第三批地区选词、中文回译与文件入口](text/review/existing-03-review.md)。新增尾段待主人审阅及 Steam 页面抽查。
- **Demo 当前完整导入稿**：[storepage_1315198_existing-03_review.json](text/upload/storepage_1315198_existing-03_review.json)，编辑记录 `1315198`。
- **正式版当前完整导入稿**：[storepage_602645_existing-03_Main_review.json](text/upload/storepage_602645_existing-03_Main_review.json)，编辑记录 `602645`。
- 两份均包含完整 30 种语言及第二、三批合计 8 种新增尾段，分别直接导入对应页面即可，无需手动合并。相比各自第二批，只追加本批四个长描述尾段；所有简介、其他语言、各自身份及非文字字段保持原值。
- 本轮原有 13 种语言已全部有 BBCode 和独立纯文本简介。原始下载及此前 JSON 快照保持原样，没有执行 Steam 上传、保存、发布或 Git 提交。

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
