# 第二批商店文案审阅

日期：2026-10-15。状态：第二批文案整理完成，挪威语长描述截图抽查通过，主人已要求提交 Git；其他视觉检查范围见文末。

第一批已经主人确认可以继续。本批补齐 7 种原始 JSON 中为空的语言：荷兰语、丹麦语、挪威语（Bokmål）、瑞典语、芬兰语、波兰语、乌克兰语。第三批保留保加利亚语、匈牙利语、印度尼西亚语、土耳其语、希腊语、罗马尼亚语、马来语，尚未开展。

## 文件与导入范围

长描述文件只有可直接粘贴的 Steam BBCode；简介文件只有纯文本，没有 Markdown 标题或围栏。下表文件与第二批 JSON 一致，文案说明和回译只保存在本审阅稿。

| 语言 | Steam 键 | 长描述 | 简介 | 简介字符数 |
| --- | --- | --- | --- | ---: |
| 荷兰语 Nederlands | `dutch` | [BBCode](../about/关于此游戏-Nederlands.bbcode.txt) | [纯文本](../short/简介-Nederlands.txt) | 213 |
| 丹麦语 Dansk | `danish` | [BBCode](../about/关于此游戏-Dansk.bbcode.txt) | [纯文本](../short/简介-Dansk.txt) | 246 |
| 挪威语 Norsk | `norwegian` | [BBCode](../about/关于此游戏-Norsk.bbcode.txt) | [纯文本](../short/简介-Norsk.txt) | 228 |
| 瑞典语 Svenska | `swedish` | [BBCode](../about/关于此游戏-Svenska.bbcode.txt) | [纯文本](../short/简介-Svenska.txt) | 220 |
| 芬兰语 Suomi | `finnish` | [BBCode](../about/关于此游戏-Suomi.bbcode.txt) | [纯文本](../short/简介-Suomi.txt) | 212 |
| 波兰语 Polski | `polish` | [BBCode](../about/关于此游戏-Polski.bbcode.txt) | [纯文本](../short/简介-Polski.txt) | 243 |
| 乌克兰语 Українська | `ukrainian` | [BBCode](../about/关于此游戏-Українська.bbcode.txt) | [纯文本](../short/简介-Українська.txt) | 230 |

待上传文件：[storepage_1315198_batch-02_review.json](../upload/storepage_1315198_batch-02_review.json)。它是累积稿，包含第一批已经确认的内容，不必先后导入两个文件。

- 应用身份沿用 Demo 原始导出：商店编辑记录 `itemid = "1315198"`，对应 AppID `5220880`。
- 与第一批 JSON 比较，只改变本批 7 种语言的长描述和简介，共 14 个字段。其余 23 种语言及所有系统需求字段保持原值。
- 共 30 个语言键：23 种有完整文案，剩余 7 种为空。
- 正式版与 Demo 共用文案，但正式版应以它自己的导出身份生成导入文件，不直接上传本文件。
- 原始下载和第一批 JSON 均未改动。AI 未执行后台上传、保存或发布；主人自行操作后提供了挪威语实际页面截图。

## 正文与简介的共同处理

正文以原始简中语义和已确认的第一批取舍为依据，保留五段说明：桌面陪伴与小狗的幸运、不打扰与全屏避让、键鼠积累点数、保留／换牌的扑克小游戏、用积累的点数开盒与收集装扮。没有给任一语言添加付费筹码、现金奖励或新玩法承诺。

开头始终先说电脑／屏幕，再说猫、鱼等已有伙伴，第二句更短，落在这次加入的小狗。各语言正文都用自己的明确主语／所属关系表达“遇见你是小狗最大的幸运”。正式游戏名使用 Lucky Dog Rise，角色／品牌使用 Lucky Dog，亲昵代称按上下文选择；末尾选中的文化狗名只作联想或比喻，不是给角色改名，也不暗示有命名功能。

七份简介都覆盖：桌面小狗陪伴，工作／休息／游戏，与小狗打扑克并观察表情，开盒收集装扮并营造两者的小窝。均附带点数来自键鼠输入的简短说明。没有为了介绍类型而强制使用“本作是一款……”开头。

每份长描述保留两个资源组各一处：`adoptionbox` 靠右；`store-dialogue-v009` 居中。开头和 Slogan 居中，邀请语靠右，关键词段落在正文媒体后。本批不新建 HTML 预览，也不复制维护一套混合 Markdown 的长描述。

## 选词依据的读法

以下先列出选词及其当地依据，再给出实际尾段的中文回译。词典支持词义和语域；狗名资料支持名字实际存在或熟悉度；当地媒体、犬种介绍和梗社区支持文化关联；Steam 页面支持平台名称和类型术语。它们都不能单独证明某词当前的搜索量或 Steam 排名提升。本批没有取得关键词搜索量，也没有把普通狗称呼统称为正在流行的热词。

七种语言的 [Bongo Cat 商店标题](https://store.steampowered.com/app/3419430/Bongo_Cat/)均保留 Bongo Cat，因此本批沿用主人允许的同类游戏引入。各地区页面中的宣传正文有些仍回退英语；只用当地确实显示的标题、标签和界面术语作参考，不把英语回退内容当成本地自然文案。

## 荷兰语

用 `je` 的亲近语气。开头的 `krijg je er een maatje bij` 明确“再添一个伙伴”，不取代原有桌宠。

最大的幸运：`Jou ontmoeten is het grootste geluk van dit hondje.` 回译：遇见你，是这只小狗最大的幸运。

Slogan：`Drukke dagen rafelen. Een hondje naait ze weer heel` 回译：忙碌的日子磨出了毛边，小狗把它们重新缝好。`rafelen / naait` 保留磨损与针线修补的回应，修补对象是日子。这是本次创作的隐喻，不作为现成荷兰俗语介绍。

邀请：`Zou je op je scherm ook een klein plekje voor hem hebben? Een hoekje is al genoeg.` 回译：你的屏幕上，能不能也给它一点点位置？一个小角落就够了。`Zou / ook / klein / genoeg` 一起保留试探、加入和不敢多占位置的感觉。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `Fikkie` | 荷兰语典型狗名；想到狗时可能想到的熟悉名字 | 已核实。[荷兰语言研究所对 Fikkie 的研究](https://www.ivdnt.org/wp-content/uploads/2020/11/Fikkie.pdf)讨论其典型狗名地位；[Royal Canin 狗名页面](https://www.royalcanin.nl/populaire-kattennamen-populaire-hondennamen/hondennamen?naam=Fikkie)也有实际记录 | 用 `een eigen kleine Fikkie` 比喻属于自己的亲近伙伴；不是把它说成当前狗名榜第一 |
| `Doge`、`Cheems`、`Shiba Inu` | 玩家从柴犬外形想到两只梗犬，或直接搜其名字／犬种 | 已核实。当地 [NOS 的 Doge 报道](https://nos.nl/artikel/2521696-wereldberoemde-memehond-doge-dood)和 [Cheems 报道](https://nos.nl/artikel/2487257-internethond-cheems-bekend-van-vele-memes-is-dood)使用这些名称并说明柴犬关联 | 从 `Shiba Inu-snoetje`（柴犬小脸）连接 Doge 与 Cheems；不说游戏角色就是它们 |
| `viervoeter` | 四脚伙伴，常用宠物称呼，狗语境中自然 | 已核实，NOS 上述报道使用该称呼；不是单独证明狗专属搜索热度 | `deze kleine viervoeter` 承接狗脸，避免歧义 |
| `virtuele hond`、`hondje op je bureaublad` | 虚拟狗／桌面小狗，描述性搜索组合 | 按本作事实组成的候选表达；词义明确，未取得完整短语搜索量 | 两种完整表达都写入尾段，连接任务栏、工作与装扮 |
| `Bongo Cat`、`bureaubladmaatjes` | 同类桌宠名、桌面伙伴的概括 | [荷兰语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=dutch)核对标题与任务栏术语，平台标签为 `Virtuele buddy`；`bureaubladmaatjes` 是本稿自然改写，不冒充官方标签 | 用于共用受众开头 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，又更想有一只狗在身边，Lucky Dog Rise 或许适合你。看到这样的柴犬小脸，会不会马上想起 Doge 和 Cheems？这位小小的四脚伙伴不会只待在表情包里，它会搬到你的任务栏旁。它是一只虚拟狗，一个属于你的小 Fikkie，陪你打字和游戏。挑顶帽子，收集一套好看的衣服，让桌面上的小狗有自己的模样。也许，它会成为你那些打开的窗口之间最忠实的伙伴。

取舍：采用典型狗名 Fikkie，而不把该名字来源中的“火”义或俚语句也搬进正文；这些旁支需要另绕语境，不能更好地连接本作陪伴感。没有照搬简中的大小变化句。

## 丹麦语

使用 `du`，小狗用 `han`。`Lykkepoint` 为本稿统一的幸运点数名称。

最大的幸运：`At møde dig er det heldigste, der er sket for den lille hund.` 回译：遇见你，是这只小狗身上发生过的最幸运的事。

Slogan：`Travle dage trævler op. En lille hund syr dem sammen igen` 回译：忙碌的日子逐渐脱线，小狗把它们重新缝合。`trævler op / syr ... sammen` 形成脱线与缝回的回应。

邀请：`Kunne der mon også blive et lille hjørne på din skærm til ham? Han behøver ikke meget plads.` 回译：你的屏幕上，能不能也腾出一个小角落给它呢？它并不需要多少位置。`Kunne / mon / også` 使请求带试探和加入感。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `vovsen`（词形来自 `vovse`） | 可爱狗狗的口语昵称 | 已核实。[Den Danske Ordbog](https://ordnet.dk/ddo/ordbog/vovse)说明其可指小而可爱的狗，带非正式语气 | 通过定指词形指代屏幕上的这只狗；不把普通口语称呼标成近期爆词 |
| `Fido` | 丹麦语里熟悉的经典狗名 | 已核实。[Agria 2026 年发布的 2025 年狗名分析](https://via.ritzau.dk/pressemeddelelse/14745033/som-barn-sa-hund-se-arets-mest-populaere-hundenavne?lang=da&publisherId=13561644)明确区分经典的 Fido 与现在较常见的人名式狗名；[Hunden.dk](https://hunden.dk/nyheder/danskernes-foretrukne-hundenavne/10110)也记录该文化背景 | `en trofast Fido`（一个忠实的 Fido）承载经典狗伙伴联想；不是声称它现在排名热门 |
| `Doge-memes`、`Shiba Inu` | Doge 狗梗及柴犬外形 | 已核实当地名称关系。[丹麦语犬种介绍](https://www.hundelex.dk/racer/shiba-inu)明确将 Doge 与该犬种联系；这是当地用法依据，不是热度排名 | 小脸唤起 Doge 梗联想，随即转到屏幕上实际相伴的狗 |
| `virtuel hund`、`firbenet ven` | 虚拟狗、四脚朋友；产品搜索与狗的亲昵称呼 | 依语义和产品事实创作的候选短语，无独立搜索量数据 | 写进帽子、装扮、屏幕角落的场景 |
| `Bongo Cat`、`skrivebordsvenner` | 同类桌宠名及桌面伙伴 | [丹麦语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=danish)的标题与 `Skrivebordsven` 标签可核对 | 以自然复数用于开头 |

尾段回译：简而言之，如果你喜欢 Bongo Cat 这样的桌面伙伴，又对狗狗特别心软，Lucky Dog Rise 或许适合你。小小的柴犬脸也许会让你想到 Doge 梗，不过这里的狗狗会待在屏幕上陪你。它是一只虚拟狗，有自己的小表情，也有可以收集的帽子和衣服。就像键盘旁有个忠实的 Fido，只是地板上不会留下湿漉漉的爪印。也许，你温暖的屏幕角落里正少这样一位四脚朋友。

取舍：研究中也看到当代常用狗名 Charlie、Luna，但它们同时广泛用于人名，与狗的专属文化联系较弱；尝试“屏幕上的 Charlie”后不如 Fido 直接，因此本批采用 Fido 的经典联想，没有堆入多个人名。未因其他语言使用 Cheems 就强行补齐同样组合。

## 挪威语

采用 Bokmål、`du` 和自然的 `PC-en`，不混入丹麦语拼写。`Flakspoeng` 指键鼠与扑克获得的游戏内幸运点数。

最大的幸运：`Å møte deg er det heldigste som har hendt denne lille hunden.` 回译：遇见你，是这只小狗经历过的最幸运的事。

Slogan：`Travle dager rakner. En liten hund syr dem sammen` 回译：忙碌的日子松了线，小狗把它们缝回一起。`rakner` 用散开／脱线表现日常磨损，缝合对象为这些日子。

邀请：`Kunne denne lille hunden også få et hjørne av skjermen din? Bare en liten krok.` 回译：这只小狗能不能也得到你屏幕上的一个角落？只要小小的一隅。`Kunne / også / bare` 保留委婉、加入和只要一点的感觉。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `vofsen`（来自 `vofs`） | 狗狗的口语／儿语爱称 | 已核实。[挪威学院词典 NAOB](https://naob.no/ordbok/vofs)明确释为狗，并说明口语和称呼用法 | 指定为“这只狗狗”，保持自然变格 |
| `Bamse` | 实际常见狗名；也能带来毛绒、亲近的联想 | 已核实狗名身份。[Agria 发布的 2025 年榜单](https://kommunikasjon.ntb.no/pressemelding/18758652/luna-og-milo-topper-listen-dette-er-norges-mest-populaere-hundenavn?lang=no&publisherId=17849106)列入公犬前 20；本稿不把狗名使用频率当搜索量 | `en liten Bamse` 借熟悉狗名表达亲近感；保留正式角色名 Lucky Dog |
| `Doge`、`shiba inu` | 柴犬脸唤起 Doge 文化记忆 | 已核实。[挪威语 E24 报道](https://e24.no/boers-og-finans/i/PpRPd5/dogecoin-hunden-kabosu-er-doed)与 [Gamereactor Norge](https://www.gamereactor.no/hunden-doge-er-dod-1527213/)均使用 Doge／Kabosu 称呼 | 从柴犬小脸转到会继续陪伴的桌面狗，不引入报道的加密货币话题 |
| `virtuell hund`、`firbeint venn` | 虚拟狗、四脚朋友 | 描述性候选短语，按本作事实与当地语法组织 | 以表情、装扮和不占沙发的玩笑串联 |
| `Bongo Cat`、`skrivebordsvenner` | 同类桌宠与桌面伙伴 | [挪威语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=norwegian)使用 `Skrivebordsvenn` 标签 | 复数自然用于开头 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更喜欢狗狗，Lucky Dog Rise 或许适合你。柴犬的小脸也许让你想到 Doge，但这只狗狗在你关掉图片之后还会继续陪着你。你工作或游戏时，它就在任务栏旁找个角落。一只虚拟狗，有小表情，也有能收集的帽子和衣服；一位不需要借走你沙发位置的四脚朋友。也许 Lucky Dog 会让你想起一只小 Bamse，只是这位伙伴住在屏幕上。

取舍：Luna、Milo 等名字也在榜单里，但 Bamse 对本段毛绒伙伴的联想更集中，因此没有把狗名榜全搬进来。未给角色添加现实抱抱或互动抚摸功能；沙发只是虚拟狗与真实狗的玩笑对照。

## 瑞典语

使用 `du`。`lyckoträff` 表达幸运的际遇，使关键句避免重复硬译 lucky 的形容词。

最大的幸运：`Att få träffa dig är hans livs största lyckoträff.` 回译：能够遇见你，是它一生中最幸运的际遇。`hans` 的所属关系明确指小狗。

Slogan：`Dagarna rusar och fransar sig. En liten hund syr ihop dem` 回译：日子奔忙着，磨出了毛边；小狗将它们缝回一起。奔忙、磨损、缝补三者均保留，没有把“忙碌”翻成玩家必须完成的任务。

邀请：`Skulle den här lilla hunden också kunna få en vrå på din skärm? En liten vrå räcker.` 回译：这只小狗能不能也在你的屏幕上分得一隅？一个小小的角落就够了。`Skulle ... kunna / också / räcker` 保留柔和请求和不多索取。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `vovve`、`jycken`（来自 `jycke`） | 狗狗、狗子的口语称呼 | 已核实。[瑞典语同义词词典](https://router.synonymer.se/sv-syn/vovve)列出两者；Svenska.se 直接页面本次读取失败，不把它作为已完整读到的依据 | 两种爱称分散在尾段，不堆成同义词清单 |
| `Ludde` | 瑞典常见狗名，与家里熟悉的小伙伴联系 | 已核实。[瑞典语言与民俗研究所](https://www.isof.se/namn/ovriga-namn/djurnamn/namn-pa-hundar-katter-och-kaniner)说明狗名文化并列出 Ludde；[Svedea 汇总的 2025 年名字列表](https://www.svedea.se/hundforsakring/hundagare/skaffa-hund/hundnamn)也包含该名 | `en egen liten Ludde` 是熟悉狗名的比喻，不是新角色名或命名功能 |
| `Doge`、`shiba inu` | 柴犬与狗梗的文化联想 | 已核实。[SVT 瑞典语报道](https://www.svt.se/kultur/hunden-bakom-memet-doge-dod-blev-18-ar)使用 Doge 名称；犬种联系另由 [Yle 对同一梗犬的说明](https://yle.fi/a/74-20090437)交叉核对，后者不是瑞典语热度依据 | 接到小狗的外形、表情和任务栏陪伴 |
| `virtuell hund`、`fyrbent kompis`、`hundmänniska` | 虚拟狗、四脚伙伴、狗派用户 | 按当地词义创作的候选表达，不标为搜索量排名词 | 三个完整表达均写入尾段，分别连接产品、陪伴与受众偏好 |
| `Bongo Cat`、`skrivbordskompisar` | 同类桌宠与桌面伙伴 | [瑞典语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=swedish)标题核对；本稿使用自然的 `skrivbordskompisar`，不声称逐字引用平台标签 | 用于共用开头 |

尾段回译：简而言之，如果你喜欢 Bongo Cat 这样的桌面伙伴，但更偏狗派，Lucky Dog Rise 或许适合你。Doge 让许多人对着柴犬会心一笑；在这里，这样一只小狗狗还能在任务栏旁陪你。它是一只虚拟狗、一位四脚伙伴，在你打字、工作或游戏时待在身边。或许过不久，它就像属于你的小 Ludde？这个狗子有许多表情，也有帽子可以试戴；收集的衣服会让它拥有属于你们的风格。

取舍：没有把所有同义词都收进来。词典中还列出带贬意的狗称呼，虽可用反差玩笑串联，但会增加解释负担，因此采用亲近的 vovve 和较口语的 jycke。

## 芬兰语

采用自然的书面亲近语气，以变格和所属后缀表达关系。`Onnipisteet` 为幸运点数的统一叫法，正文按语法使用其变格。

最大的幸运：`Sinun tapaamisesi on tämän koiran suurin onni.` 回译：与你相遇，是这只狗最大的幸运。`tämän koiran` 明确幸运属于小狗。

Slogan：`Arki rispaantuu kiireessä. Pieni koira ompelee sen ehjäksi` 回译：日常在忙碌中磨损，小狗将它缝补完整。`sen` 指向日常，`ehjäksi` 是重新完整，而非具体缝纫玩法。

邀请：`Mahtuisikohan tämäkin pieni koira ruudullesi? Ihan pieni nurkka riittäisi.` 回译：这只小狗也能挤进你的屏幕里吗？一个很小很小的角落就够了。条件式与 `-han`、`-kin`、`riittäisi` 让请求保留试探、也加入和只求一点位置的感觉。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `hauvan`（来自 `hauva`）、`karvaturri` | 狗狗、毛茸茸的伙伴；后者不限于狗，但此处指代明确 | 已核实。[芬兰犬业协会教学材料](https://www.kennelliitto.fi/files/koiramateriaalia-ylakouluille-opettajan-opas-0)将两词列入狗相关表达，支持词义而非热度 | 用自然变格接到陪伴和装扮，不硬保留词典原形 |
| `Onni` | 实际狗名，同时也是幸运／幸福的词，可直接关联 Lucky Dog | 已核实。[芬兰语言研究机构 Kotus 的狗名文章](https://kotus.fi/kolumni/laika-haukku-ja-muut-haukkujat/)以 `Onnia` 等变格记录该狗名；这份历史资料不证明其现在的狗名排名 | `oma pieni Onni`（自己的小幸运／小 Onni）利用双关，不将角色正式改名 |
| `Doge-meemien`、`shiba inu` | Doge 狗梗与柴犬外形 | 已核实。[Yle 芬兰语报道](https://yle.fi/a/74-20090437)同时使用 Doge、meemi 和犬种名称 | 用 Doge 梗犬的外形作桥，接到不只看图片的持续陪伴 |
| `virtuaalinen koira`、`työpöytälemmikki` | 虚拟狗、桌面宠物 | 基于产品事实的候选搜索组合，未取得完整短语搜索量 | 两种完整词语均在尾段出现，分别说明狗的形态与所在环境 |
| `Bongo Catin`、`työpöytäkavereista` | Bongo Cat 的属格；桌面伙伴的变格 | [芬兰语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=finnish)提供标题与 `Työpöytäkamu` 标签；本稿自由选用 `kaveri` 表达伙伴 | 使用本地语法变格，不为关键词原样匹配而破坏句子 |

尾段回译：如果你喜欢 Bongo Cat 这样的桌面伙伴，又更偏狗派，Lucky Dog Rise 也许适合你。Doge 梗里的柴犬是熟悉的面孔，但这只狗狗的陪伴不只在你翻图片时才有。你打字、工作或游戏时，虚拟狗会坐在任务栏附近。这个毛茸茸的伙伴会从你收集的帽子和衣服里获得自己的样子。也许，你自己的“小 Onni／小幸运”就在屏幕角落：一只为普通日子带来陪伴和好心情的桌宠。

取舍：Kotus 同时列出 Musti、Rekku、Turre 等传统名字；尝试后选 Onni，因为它与幸运意象能直接结合，其他名字再加入只会重复同一段作用。没有为了凑名单把它们全写入。

## 波兰语

使用 `ty` 的亲近语气，避免要求玩家使用男性／女性的过去式。`Punkty Szczęścia` 按自然语法变格。

最大的幸运：`Poznanie ciebie to jego największe szczęście.` 回译：认识你，是它最大的幸运。`jego` 承接小狗。

Slogan：`Codzienność strzępi się w biegu. Piesek zszywa ją po kawałku` 回译：日常在奔忙中磨破，小狗一点一点把它缝好。保留“在忙碌中磨损—小狗逐步修补”，而不是单说小狗可爱。

邀请：`Czy dla tego pieska też znalazłby się kącik na twoim ekranie? Wystarczy mu odrobina miejsca.` 回译：你的屏幕上，能不能也为这只小狗找到一个小角落？一点点位置就够它了。`też / znalazłby / odrobina` 保留加入、条件式请求和小小要求。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `pieseł`、`Doge`、`Cheems` | 波兰本地狗梗称呼与两只梗犬名字 | 已核实。[当地犬舍的犬种资料](https://sesamehouse.pl/rasy/shiba/)搜索正文直接将 pieseł、Doge、Cheems 与 Shiba Inu 相连；页面直接打开失败，仅采用已返回的公开搜索正文。[波兰梗站的实际分类](https://www.blasty.pl/tag/piesel)也使用这些词，核对本地网络用法 | 都实际写入，以玩家看到柴犬脸时的联想连接，不宣称角色就是原梗犬 |
| `Reksia`（来自 `Reksio`） | 波兰经典动画狗，玩家童年狗形象的文化记忆 | 已核实。[原创动画工作室官网](https://www.sfr.pl/en)介绍该角色；本稿不将其称为当前网络新梗 | 用 `pamiętasz ... Reksia`（记得 Reksio）引入童年记忆，再接到现在屏幕上的新伙伴；宾格 Reksia 为自然词形 |
| `shiba inu`、`psiak` | 柴犬犬种与亲近的狗子称呼 | 犬种依据同上；`psiak` 是普通亲昵称呼，未将其冒充新热词 | 从梗形象转到具体屏幕上的狗 |
| `wirtualny pies`、`na pulpicie` | 虚拟狗、在电脑桌面上 | 按本作事实形成的描述性候选表达 | 接到任务栏、表情、帽子和衣服 |
| `Bongo Cat`、`towarzyszy pulpitu` | 同类桌宠名，桌面伙伴的变格 | [波兰语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=polish)标题与 `Towarzysz pulpitu` 标签可核对 | 按自然语法用于开头 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更亲近狗狗，Lucky Dog Rise 也许适合你。Doge、Cheems，还有网络梗里那只熟悉的 pieseł——看到柴犬，很难不微笑。又或者，你童年最记得的是 Reksio？这只狗子会选择待在你工作或游戏时的任务栏旁，而不是只住在动画或图片里。它是一只虚拟狗，有搞笑的小表情，还有能收集的帽子和衣服。这样一位日常的小伙伴，只需要桌面上的一个角落。

取舍：这里主动采用了与游戏外形不同的 Reksio，通过“童年动画狗—现在桌面狗”的关系衔接，没有因犬种不同就直接排除；同时未写成官方联动，也未添加动画角色或剧情功能。

## 乌克兰语

使用 `ти`，电脑写作 `комп’ютер`，桌面写作 `робочий стіл`。不把俄语尾段直接换字母，也不借用俄语狗网名当成本地词。`Очки Удачі` 为幸运点数的统一叫法，正文保留变格。

最大的幸运：`Зустріти тебе — найбільша удача в його житті.` 回译：遇见你，是它生命中最大的幸运。`його` 的所属关系承接小狗。

Slogan：`Метушливі дні розходяться по швах. Песик їх зшиває` 回译：奔忙的日子从接缝处散开，小狗把它们缝回一起。`розходяться по швах / зшиває` 构成脱线与修补的回应，`їх` 指向日子。

邀请：`Може, і для цього песика знайдеться куточок на твоєму екрані? Йому б зовсім трішечки місця.` 回译：能不能也在你的屏幕上，为这只小狗找一个角落呀？它只求那么一点点位置。第二句使用愿望／条件式，保留小心请求而非安置命令。

| 选词／实际写法 | 中文释义与搜索联想 | 名称核实与依据 | 采用与连接 |
| --- | --- | --- | --- |
| `песик`、`собачка` | 亲近的小狗、狗狗称呼 | 已核实。[乌克兰语词典 песик 条目](https://slovnyk.ua/index.php?swrd=%D0%BF%D0%B5%D1%81%D0%B8%D0%BA)与 [多词典汇编](https://slovnyk.me/amp/dict/vts/%D0%BF%D0%B5%D1%81%D0%B8%D0%BA)可核对爱称与同义关系 | 两词均写入尾段，以温柔称呼指向游戏里的狗 |
| `Сірка`（来自 `Сірко`） | 当地童话里的熟悉狗名，狗的文化记忆线索 | 已核实。[乌克兰语民间故事全文](https://www.ukrlib.com.ua/books/printit.php?tid=29660)开头明确狗名 Сірко；不把该名字当作所有狗的正式通名或当前热度第一 | `Сірка з казки` 明确只是在联想童话中的狗，采用自然宾格 |
| `Доге (Doge)`、`сіба-іну` | 梗名的当地拼写与原写法，以及柴犬 | 已核实。[NV 乌克兰语报道](https://nv.ua/ukr/lifestyle/gerojinya-mema-doge-svyatkuye-16-den-narodzhennya-smishni-foto-video-50193378.html)使用 Доге 与 сіба-іну；[当地另一篇报道](https://news.telegraf.com.ua/ukr/mir/2024-05-24/5853879-pomer-nayvidomishiy-svitu-sobaka-mem-yakiy-stav-oblichchyam-kriptovalyuti-dogecoin)也出现拉丁写法 Doge | 两种梗名拼写都自然写入；犬种通过 `мордочкою сіба-іну`（柴犬模样的小脸）连接 |
| `віртуальний пес`、`чотирилапий друг` | 虚拟狗、四脚朋友 | 描述性候选表达，没有独立搜索量数据 | 用桌面、任务栏和装扮场景串联 |
| `Bongo Cat`、`компаньйони на робочому столі` | 同类桌宠与电脑桌面伙伴 | [乌克兰语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=ukrainian)核对标题与 `панель завдань` 等术语；本稿明确电脑桌面，避免把平台的“桌面伙伴”类型误读成桌游 | 两个表达均在受众开头实际出现 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的电脑桌面伙伴，但心里更偏爱狗狗，Lucky Dog Rise 也许适合你。有人想到狗时会想起童话里的 Сірко，有人想起梗图里的 Доге（Doge）。而这只长着柴犬小脸的狗狗，只想在你工作或游戏时陪在旁边。任务栏边的一只虚拟狗，一位四脚朋友，有逗趣的小表情，也有可以收集的帽子。即使家里暂时还没条件养狗，这只狗狗也能在你的电脑桌面上找到安静的小角落。

取舍：Cheems 的乌克兰语音译写法在本次找到的材料中依据较弱；已有可核对的 Доге 与 Сірко 能分别承载网络梗与本地童话联想，因此本批没有为统一各语言词表而强加 Cheems。保留可用的西里尔与拉丁梗名，而非套用俄语整段。

## 本地核对与待测试范围

- 已逐段顺读、反向检查关键句与七份尾段，并检查邀请语的加入感和小心请求。未取得母语者审校。
- 采用词表在各语言尾段实际出现；涉及自然变格的词按词形核对，包括 Bongo Catin、vovsen、vofsen、jycken、hauvan、Reksia、Сірка。
- JSON 能解析，保留全部 30 个语言键和原始字符串身份；与第一批比较只有指定的 14 个字段改变，原有语言与其他字段相同。
- 七份 BBCode 与 JSON 解码后逐字一致；简介与纯文本文件一致，均少于 300 个字符。两处媒体引用完整且标签配对，没有双重 HTML 编码或混入 Markdown。
- 原始下载 SHA-256 仍为 `b89476f999278652f3bfa287875427081e11ecb7c2002670b1541cdf7bde5a2c`。
- 2026-10-15，主人提供挪威语页面的两张截图：开头两句长短分明，Slogan 单行居中，邀请语靠右，正文媒体与尾段衔接正常；“小狗最大的幸运”、委婉请求与尾段关键词均与本批稿件一致。截图中的正文媒体显示挪威语文字，但静态截图不能证明完整视频播放。
- 两张截图未展示简介，因此本次只确认挪威语长描述。其余六种语言、七份简介、其他屏幕尺寸和完整媒体播放尚未逐项视觉验收；第一批的排版检查不代替本批检查。
- 主人已将本批文案判断交由 AI 把握，并在挪威语抽查后要求提交 Git。累积 JSON 保留为本批交付文件，第三批尚未开展。
