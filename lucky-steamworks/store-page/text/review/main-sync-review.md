# 正式版商店文案同步核对

日期：2026-10-15。状态：正式版完整导入文件已生成并完成本地字段核对，芬兰语、荷兰语长描述已通过主人提供的实际页面截图抽查；其余语言及简介待页面核对。

## 输入与页面身份

- 主人提供的原件：`D:\Downloads\storepage_602645_all_Main.json`。
- 原样归档：[storepage_602645_all_Main.json](../downloads/storepage_602645_all_Main.json)。`_Main` 是主人添加的文件名标记，不参与 Steam 页面身份判断。
- 原件内 `itemid` 为字符串 `"602645"`，对应正式版 Lucky Dog Rise（AppID `2583700`）。Demo 的商店编辑记录为 `1315198`，AppID 为 `5220880`。
- 正式版原件含 30 个语言键，其中 13 种语言已有长描述及简介，17 种为空；语言键和排列顺序与 Demo 完整累积稿相同。
- 正式版原件 SHA-256：`8b6bccdc817ee5e48e0f82395a1582ddefe53c04725101e221079713fac10324`。工作区归档与下载目录原件逐字节相同，未改写主人下载的文件。

## 共用文字与同步范围

共用文案来源：[第三批 Demo 完整累积稿](../upload/storepage_1315198_batch-03_review.json)，已随 Git 提交 `b4eec36` 保存。两页继续维护同一套文字，不新增正式版专属介绍或另一套翻译。

正式版已有 13 种语言的简介与最初 Demo 导出一致。已有长描述去除 BBCode 标签并忽略排版空白后，文字也与最初 Demo 导出一致；两者存在标签嵌套和空白差异，没有发现需要另行取舍的新增正文内容。

本次以正式版原件为底稿，只将 30 种语言的 `app[content][about]` 和 `app[content][short_description]` 同步为共用稿的值。与正式版原件比较，实际变化为 47 个字段：

- 17 种原本为空的语言补齐长短描述，共 34 个字段。
- 已有 13 种语言的长描述统一为共用稿的 BBCode 和编码。其中繁体中文还同步此前已审定的末尾关键词段落；其余已有正文文字保持一致。
- 已有 13 份简介不变。

没有从 Demo 拷贝整份应用数据再仅替换 ID。正式版原件的身份、语言键、字段集合、系统需求及全部其他字段保持原值。

## 交付与本地验证

正式版待上传文件：[storepage_602645_all_Main_review.json](../upload/storepage_602645_all_Main_review.json)。导入目标是正式版商店编辑记录 `602645`。

- JSON 可解析，保留字符串 `itemid = "602645"`，30 种语言均有长短描述。
- 全部 60 个文字字段与 Demo 完整累积稿逐值相同，没有重新翻译、二次 HTML 编码或另行改写已审定文案。
- 与正式版原件比较，只有上述两个允许同步的文字字段发生变化；所有其他字段逐值相同，语言键顺序不变。
- 每种语言的长描述均保留一次 `adoptionbox` 与一次 `store-dialogue-v009` 资源组引用。
- 正式版原件、Demo 原始下载及三个批次的累积 JSON 均未改写。

## 实际页面验证边界

AI 没有执行 Steam 后台上传、保存或发布。正式版导入文件保留正式版身份，主人使用该文件测试正式版页面。

### 芬兰语

2026-10-15 主人提供了两张正式版芬兰语截图，栏目标题为 `Tietoa pelistä`。可见长描述与本地芬兰语 BBCode 稿一致，本次抽查通过：

- 开头两句居中，第二句较短；Slogan 居中，邀请句与纸箱小狗插图靠右，关键词段落位于本地化媒体之后。
- `Sinun tapaamisesi on tämän koiran suurin onni.` 保留“遇见玩家是小狗最大的幸运”的方向。
- `Arki rispaantuu kiireessä. Pieni koira ompelee sen ehjäksi` 保留“日常磨损—小狗缝补完整”的回应关系。
- `Mahtuisikohan tämäkin pieni koira ruudullesi? Ihan pieni nurkka riittäisi.` 保留“也给这只小狗一点位置，只要一个小角落”的试探与小小请求。
- 尾段正确呈现 Bongo Catin、Doge-meemien、shiba inu、hauvan、karvaturri、virtuaalinen koira、Onni 和 työpöytälemmikki；选词依据仍见第二批审阅，没有在正式版重新翻译或添加关键词。

### 荷兰语

2026-10-15 主人提供了两张正式版荷兰语截图，栏目标题为 `Over dit spel`。可见长描述与本地荷兰语 BBCode 稿一致，本次抽查通过：

- 开头和 Slogan 居中，邀请句及纸箱插图靠右，媒体后的关键词尾段正常显示。
- 开头 `krijg je er een maatje bij` 明确“再添一位伙伴”；`Jou ontmoeten is het grootste geluk van dit hondje.` 明确幸运属于遇见玩家的小狗。
- `Drukke dagen rafelen. Een hondje naait ze weer heel` 保留“忙碌的日子磨损—小狗重新缝好”的意象。
- `Zou je op je scherm ook een klein plekje voor hem hebben? Een hoekje is al genoeg.` 保留“也给它一点位置，一个小角落就够了”的委婉请求。
- 尾段正确呈现 Bongo Cat、Shiba Inu-snoetje、Doge、Cheems、viervoeter、virtuele hond、Fikkie 和 hondje op je bureaublad。`een eigen kleine Fikkie` 作为亲近狗名联想自然串入，选词依据仍见第二批审阅。

### 尚未覆盖

本次截图未覆盖简介，其余 28 种语言也未完成正式版页面逐项检查。JSON 只保存正文媒体资源组的引用，不包含媒体文件及其语言映射；截图可见芬兰语和荷兰语媒体画面，但不能证明完整动画／视频播放或其他语言的资源映射。Demo 已完成的截图抽查不能替代正式版验证。
