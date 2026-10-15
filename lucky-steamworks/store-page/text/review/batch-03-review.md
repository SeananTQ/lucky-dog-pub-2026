# 第三批商店文案审阅

日期：2026-10-15。状态：本地文案与完整累积 JSON 已整理，马来语长描述已通过主人提供的 Steam 实际页面截图抽查；其余语言及简介待页面核对。

本批补齐最后 7 种空白语言：保加利亚语、匈牙利语、印度尼西亚语、土耳其语、希腊语、罗马尼亚语、马来语。JSON 中全部 30 种语言现在都有长描述和简介。第一、二批的文案保留，既有语言不会因本批重新翻译或改写。

## 文件与导入范围

长描述文件只包含 Steam BBCode；简介文件只包含一段纯文本。审阅解释、来源与回译均留在本文，不混入商店文案。

| 语言 | Steam 键 | 长描述 | 简介 | 简介字符数 |
| --- | --- | --- | --- | ---: |
| 保加利亚语 Български | `bulgarian` | [BBCode](../about/关于此游戏-Български.bbcode.txt) | [纯文本](../short/简介-Български.txt) | 210 |
| 匈牙利语 Magyar | `hungarian` | [BBCode](../about/关于此游戏-Magyar.bbcode.txt) | [纯文本](../short/简介-Magyar.txt) | 228 |
| 印度尼西亚语 Bahasa Indonesia | `indonesian` | [BBCode](<../about/关于此游戏-Bahasa Indonesia.bbcode.txt>) | [纯文本](<../short/简介-Bahasa Indonesia.txt>) | 218 |
| 土耳其语 Türkçe | `turkish` | [BBCode](../about/关于此游戏-Türkçe.bbcode.txt) | [纯文本](../short/简介-Türkçe.txt) | 214 |
| 希腊语 Ελληνικά | `greek` | [BBCode](../about/关于此游戏-Ελληνικά.bbcode.txt) | [纯文本](../short/简介-Ελληνικά.txt) | 244 |
| 罗马尼亚语 Română | `romanian` | [BBCode](../about/关于此游戏-Română.bbcode.txt) | [纯文本](../short/简介-Română.txt) | 227 |
| 马来语 Bahasa Melayu | `malay` | [BBCode](<../about/关于此游戏-Bahasa Melayu.bbcode.txt>) | [纯文本](<../short/简介-Bahasa Melayu.txt>) | 216 |

完整累积稿：[storepage_1315198_batch-03_review.json](../upload/storepage_1315198_batch-03_review.json)。可直接作为本轮导入文件，不需要另行手动合并第一、二批。

- 保留 Demo 的原始商店编辑身份：字符串 `itemid = "1315198"`，对应 AppID `5220880`。正式版仍需以正式版自己的导出身份生成文件。
- 与第二批比较，只新增本批 7 种语言的 `app[content][about]` 和 `app[content][short_description]`，共 14 个字段；其余 23 种语言和所有系统需求字段完全保留。
- 原始下载、第一批及第二批 JSON 均保持不变，第三批使用单独的累积文件，避免改写已确认的批次快照。
- 30/30 表示导入文件中所有语言的长短描述字段已填齐，不代表全部旧 Markdown 已重新同步，也不代表 30 种语言都完成实际页面或母语者验收。
- 本轮没有由 AI 执行 Steam 后台上传、保存或发布。

## 正文与简介的处理

正文继续沿用已确认的语义：已有其他桌面伙伴，这次也加入一只小狗；它因遇见玩家而幸运；它安静陪伴、不催促，并可在其他全屏游戏中暂时避让；键鼠输入积累游戏内幸运点数；扑克小游戏选择保留、替换手牌；用积累的点数获得盒子和装扮。没有新增付费筹码、现金收益、命名、语音识别或联动角色功能。

各语言开头先交代电脑／屏幕，第二句短于铺陈句。开头和 Slogan 居中，委婉邀请语与 `adoptionbox` 靠右，`store-dialogue-v009` 居中，关键词尾段在媒体后。两个 Steam 资源组名称不翻译，仍使用原有 `img` 标签。

七份简介都保留四项：桌面小狗陪伴、工作／休息／游戏、一起打扑克并观察表情、开盒收集装扮并形成两者的小窝，同时交代点数来自键鼠操作。简介保持场景和情绪引入，没有强制写成游戏类型定义。

以下 Slogan 均为本次创作，不宣称是当地现成俗语。印尼语、马来语采用“忙碌使心受损—小狗把心缝好”的意象，字面是撕破心，情绪比“日子脱线”更直接；修补的是疲惫与情绪，而非缝纫玩法。

## 选词依据与边界

先核实狗的爱称、文化狗名和当地狗梗，再围绕采用词写尾段。词典只能说明含义和语域；狗名名录、文学用例说明名字确实存在；本地报道和社区用例说明梗的当地传播。没有将这些材料当成 Steam 搜索权重或实时搜索量证明，也没有把每个普通狗称呼都称为近期热词。

七种语言的 Bongo Cat 商店标题都保留该名字，因此允许共用“喜欢 Bongo Cat 这类伙伴，但更喜欢狗”的开头。部分商店介绍或标签仍回退英语；下面只在确实本地化的地方借鉴平台术语，其他表达按目标语言自行组织，不冒充官方标签。

## 保加利亚语

使用亲近的 `ти`；`кученце` 为中性名词，正文相应使用 `то`，不混用其他语言的代词结构。

最大的幸运：`За него най-големият късмет е срещата с теб.` 回译：对它来说，最大的幸运就是与你相遇。`него` 的指代是前面的小狗。

Slogan：`Забързаните дни се разнищват. Кученцето ги съшива` 回译：奔忙的日子渐渐脱线，小狗把它们缝回一起。`ги` 指日子，保留磨损与修补。

邀请：`Би ли се намерило мъничко място на екрана ти и за това кученце? Едно ъгълче му стига.` 回译：你的屏幕上，能不能也给这只小狗找到一点点位置？一个小角落就够它了。条件式、`и за`（也给）、`мъничко`（一点点）共同形成轻轻请求。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Шаро` | 保加利亚语里可唤起熟悉狗形象的狗名 | 已核实狗名身份。[保加利亚国家广播电台的《Лазарица》节目介绍](https://old-news.bnr.bg/post/100506585)公开搜索正文明确将 Шаро 列为主人公的狗；直接页面读取超时，未把它当作已完整读到的正文或狗名热度统计 | `твоят малък Шаро`（你的小 Шаро）作熟悉伙伴的比喻，保留 Lucky Dog 角色身份 |
| `Doge`、`шиба ину` | 玩家从柴犬脸想到的狗梗名与犬种 | 已核实。[当地 24 Chasa 报道](https://www.24chasa.bg/mezhdunarodni/article/18008231)明确使用拉丁写法 Doge 与西里尔犬种名 | 从小狗脸转到不只待在梗图里的陪伴，不涉及报道中的货币话题 |
| `кученце`、`четириног приятел` | 小狗、四脚朋友；亲昵称呼与描述性候选表达 | [保加利亚语词典](https://rechnik.chitanka.info/w/%D0%BA%D1%83%D1%87%D0%B5%D0%BD%D1%86%D0%B5)可核对前者；后者按当地语义自然组合，无搜索量排名依据 | 在桌面陪伴与不需要整个院子的玩笑中写入 |
| `виртуално куче` | 虚拟狗，产品搜索组合 | 按本作实际用途创作，未取得该完整短语搜索量 | 与任务栏、表情和收集装扮连接 |
| `Bongo Cat`、`приятелчета за работния плот` | 同类桌宠名与电脑桌面伙伴 | [保加利亚语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=bulgarian)核对标题；尾段自行明确 `работния плот`（电脑桌面），避免混成桌游 | 用于受众引入 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面小伙伴，却更偏狗派，Lucky Dog Rise 或许正适合你。柴犬的小脸会让你想到 Doge，不过这只狗不会只停留在搞笑图片里。它会挨着任务栏坐下来，陪你打字、工作或游戏。一只虚拟狗，有小表情和可以收集的帽子、衣服；一位不用占整个院子的四脚朋友。也许 Lucky Dog 会成为你的小 Шаро，只是家安在屏幕角落。

取舍：未把来源较弱的西里尔 Doge／Cheems 音译或按外形猜出的梗名加入。采用可核对的拉丁 Doge 和本地狗名 Шаро，未机械补上大小变化句。

## 匈牙利语

采用 `te` 的亲近语气。狗没有强制性别代词，名词与外语品牌按匈牙利语自然附加词尾，如 `Bongo Cathez`；不为关键词匹配破坏语法。

最大的幸运：`Számára az a legnagyobb szerencse, hogy találkozott veled.` 回译：对它而言，最大的幸运就是遇见了你。

Slogan：`A rohanó napok felfeslenek. Egy kiskutya összevarrja őket` 回译：奔忙的日子绽开了线，一只小狗把它们缝在一起。`őket` 指日子，不新增针线玩法。

邀请：`Jutna a képernyődön ennek a kiskutyának is egy kis zug? Egy apró sarok is elég neki.` 回译：你的屏幕上，能不能也给这只小狗分到一小隅？小小的角落就够它了。条件式 `Jutna` 与 `is` 保留试探与也加入。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Buksi` | 传统、熟悉的狗名，承担“想到狗就想到某个名字”的文化联想 | 已核实。[当地狗生活杂志](https://kutyabarat.hu/hasznos_tanacsok/125066/adjunk_a_kutyanknak_emberi_nevet/)将 Buksi 列为传统名字；[训犬学校名录](https://www.kutyaiskola-veresegyhaz.hu/kutyanev-gyujtemeny/)也列有该名。传统身份不等于现在排名最热门 | `egy saját Buksi`（属于自己的 Buksi）只作亲近伙伴的比喻 |
| `blöki`、`kutyus`、`kutyás` | 狗狗／狗子、狗派用户 | 已核实。[Szotar.net 的 blöki 搜索正文](https://www.szotar.net/magyar/bl%C3%B6ki)出现 kutyus、kutya 的对应；完整词典需要登录，本次未读取付费正文。[当地语言讨论](https://www.gyakorikerdesek.hu/kultura-es-kozosseg__nyelvek__8670436-kozismert-kutyus-nev-a-bloki-de-vajon-mit-jelent-es-honnan-ered)也可见 blöki 的狗称呼用法 | 在受众与狗伙伴场景中分散使用，不把普通口语词冒充新热词 |
| `Doge`、`siba inu` | 狗梗与柴犬的当地写法 | 已核实。[24.hu 匈牙利语报道](https://24.hu/kultura/2024/05/24/meghalt-kabosu-a-doge-memet-inspiralo-kutya/)使用 Doge 与 siba inu | 写作 `siba inu-pofija`（柴犬小脸），从照片联想过渡到陪伴 |
| `virtuális kutya`、`négylábú barát` | 虚拟狗、四脚朋友，描述性候选短语 | 按产品事实与当地词形组织，无独立搜索量数据 | 以任务栏、表情、帽子与衣服串联 |
| `Bongo Cathez`、`asztali társakat` | 同类桌宠名与桌面伙伴的自然词形 | [匈牙利语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=hungarian)标题和 `Asztali társ` 标签可核对 | 用于共用开头 |

尾段回译：如果你喜欢 Bongo Cat 这样的桌面伙伴，却更偏狗派，Lucky Dog Rise 也许适合你。Doge 的柴犬小脸，只在一张图片里就能让人微笑；这只狗狗则会在你翻完图片后继续陪着你。一只任务栏旁的虚拟狗，有自己的小表情、可以收集的帽子和衣服。一位四脚朋友，有点像属于自己的 Buksi；只是这个狗子住在屏幕上，安静陪你工作、游戏或短暂休息。

取舍：Buksi 的传统联想比堆入多个人名式狗名更集中。Cheems 没有因其他语言采用就自动写入；本批以已核实的 Doge、Buksi 和 blöki 为重点。

## 印度尼西亚语

使用 `kamu`，叙述小狗可用 `dia`、`si kecil`。简介用 `bareng`、`buat` 等亲近的日常表达，正文稍完整但不故作文绉绉。幸运点数统一叫 `Poin Hoki`。

最大的幸运：`Baginya, bisa bertemu denganmu adalah keberuntungan terbesar dalam hidupnya.` 回译：对它来说，能够遇见你，是它生命中最大的幸运。

Slogan：`Hari sibuk merobek hati. Si guguk menjahitnya kembali` 回译：忙碌的日子撕破了心，狗狗把它重新缝好。`guguk` 是狗的亲近称呼，`-nya` 在这里回指心；保留损耗与修复的情绪隐喻。

邀请：`Boleh si kecil ini ikut punya sudut di layarmu juga? Sedikit tempat saja sudah cukup.` 回译：这个小家伙能不能也一起在你的屏幕上占个角落？一点点地方就够了。`ikut / juga / saja` 同时表达加入、也、只要一点。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `anabul` | “毛孩子”的当地缩略爱称，不仅指猫，也包括狗等宠物 | 已核实。[ANTARA 印尼语说明](https://babel.antaranews.com/amp/berita/453028/apa-itu-anabul-simak-penjelasan-dan-jenis-jenisnya)解释 anak bulu 并列出狗；[当地宠物服务的狗文章](https://petkingdom.co.id/berita-terbaru/cara-melatih-anjing)也实际使用该词 | 先交代偏爱狗和柴犬脸，再说 anabul，避免读成猫 |
| `guguk` | 狗狗，来自叫声的亲近称呼 | 已核实。[印尼语 Wikikamus](https://id.wiktionary.org/wiki/guguk)给出狗的释义并指向 KBBI；本次没有声称已登录读取 KBBI 原站全文 | 用于 Slogan 和尾段的 `si guguk`，不暗示角色会响应语音 |
| `Doge`、`Cheems`、`Shiba Inu` | 当地可见的两只狗梗及犬种 | 已核实。[IDN Times 的区别说明](https://www.idntimes.com/hype/viral/perbedaan-cheems-dan-doge-anjing-meme-yang-serupa-tapi-tak-sama-00-sd4vv-3qprjq)和 [Kompas 报道](https://amp.kompas.com/tren/read/2023/08/20/173000065/cheems-shiba-inu-di-balik-meme-anjing-populer-mati-akibat-leukemia)支持当地名称及外形关联 | 从柴犬脸联想 Doge、Cheems，再转向实际屏幕伙伴；不把两只梗犬说成同一只 |
| `anjing virtual`、`teman berbulu` | 虚拟狗、毛茸茸的伙伴，描述性搜索组合 | 按实际产品与当地语义创作，无完整短语搜索量数据 | 与表情、收集帽子和现实暂时不能养狗的场景串联 |
| `Bongo Cat`、`teman desktop` | 同类桌宠名与桌面伙伴 | [印尼语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=indonesian)标题与 `Teman Desktop` 标签可核对 | 共用引入后独立组织尾段 |

尾段回译：简而言之，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更喜欢狗狗，Lucky Dog Rise 或许适合你。看到柴犬脸，会不会马上想起 Doge 或 Cheems？这次，可爱的毛孩子不只是在梗图里经过，还会坐到任务栏旁。狗狗陪你打字、工作或游戏，不需要带它绕小区遛。一只虚拟狗，有逗趣的表情和可以收集的帽子、衣服。也许，你终于有一个毛茸茸的朋友陪着度过忙碌日子，即使家里暂时还没条件养狗。

取舍：检索中也出现 Bleki 狗名，但找到的零散文学与名字列表不足以判断其用户认知价值；本批已有当地爱称 anabul、guguk 和两只可核实的梗犬，未为填满狗名名单而强加 Bleki。

## 土耳其语

使用亲近的 `sen` 语气，土耳其语人称不强加玩家性别。幸运点数为 `Şans Puanı`，不写成充值筹码或金钱收益。

最大的幸运：`Onun hayatındaki en büyük şans, seninle tanışmak.` 回译：它一生中最大的幸运，就是与你相识。

Slogan：`Telaşlı günler sökülür. Küçük bir köpek ilmek ilmek onarır` 回译：奔忙的日子脱了线，一只小狗一针一线地修补。`ilmek ilmek` 保留一点点缝补的节奏。

邀请：`Ekranında bu küçük köpeğe de bir köşecik düşer mi acaba? Azıcık yer ona yeter.` 回译：你的屏幕上，能不能也分给这只小狗一个小角落呀？那么一点点地方就够它了。`de / acaba / azıcık` 保留也加入、试探和少少一点的要求。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Boji` | 伊斯坦布尔因公共交通出行而出名的狗，具有本地网红狗记忆 | 已核实。[TRT Haber 的当时报道](https://www.trthaber.com/haber/yasam/toplu-ulasimi-seven-boji-gunde-29-durak-geziyor-613451.html)及 [Metro İstanbul 2021 年报的公开搜索正文](https://www.metro.istanbul/Content/assets/uploaded/2021_faaliyet_raporu.pdf)都说明其身份 | 用“还记得因出行而出名的 Boji 吗”引出路线反差，不声称它现在仍每天乘车，也不承诺本作有城市／乘车玩法 |
| `kuçu`、`patili dost` | 儿语狗狗、带爪子的朋友 | `kuçu` 的狗义可由 [Nişanyan 词典的公开搜索正文](https://www.nisanyansozluk.com/kelime/ku%C3%A7u%20ku%C3%A7u)核对；`patili dost` 为宠物语境的自然描述，不标为新热词 | 指向同一只虚拟狗，不写成“喊一声就回应”的语音控制 |
| `Doge`、`Cheems`、`Shiba Inu` | 狗梗名及柴犬脸 | 已核实。[土耳其语 Tamindir 文章](https://www.tamindir.com/haber/doge-meme-sevimli-kopek-veda_82429/)使用 Doge 与 Cheems；[Independent Türkçe](https://www.indyturk.com/node/656266/ya%C5%9Fam/viral-doge-mimiyle-%C3%BCn-kazanan-shiba-inu-kansere-yenik-d%C3%BC%C5%9Ft%C3%BC)同时出现犬种与两个梗名 | 只借当地可见名称建立外形联想；不照搬部分报道混称原始 Doge 与 Cheems 的叙述 |
| `sanal köpek` | 虚拟狗，产品搜索组合 | 按产品事实和当地语义组织，无搜索量排名数据 | 接到表情、帽子和衣服收集 |
| `Bongo Cat`、`masaüstü dostlarını` | 同类游戏名与桌面伙伴的自然词形 | [土耳其语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=turkish)核对标题；`masaüstü dostları` 为本稿自然表达，不冒充平台固定标签 | 用于受众引入 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更亲近狗狗，Lucky Dog Rise 可能适合你。在柴犬脸上联想到 Doge 或 Cheems 很容易。你还记得那只因在伊斯坦布尔乘公共交通而出名的 Boji 吗？这位带爪子的伙伴路线更短：你工作或游戏时，就待在任务栏旁的角落。一只可爱的狗狗，一只可以为它收集帽子和衣服的虚拟狗。它不必逛完整座城市，能在你身边度过一天就已经满足。

取舍：Boji 的本地文化联系可以通过路线反差自然写入，即使本作没有旅行功能也不排除；没有再堆入 Karabaş 等名字。全段以明确比较表达，不是联动或角色替换。

## 希腊语

采用亲近的 `σου`。小狗 `σκυλάκι` 为中性名词，正文用 `του` 等相应形式；游戏名通常加当地自然的中性冠词 `το`。

最大的幸运：`Η μεγαλύτερή του τύχη ήταν που σε γνώρισε.` 回译：它最大的幸运，是能够遇见你。所属代词明确承接小狗。

Slogan：`Η ζωή ξηλώνεται στο τρέξιμο. Ένα σκυλάκι τη ράβει ξανά` 回译：生活在奔忙中脱线，一只小狗把它重新缝好。`στο τρέξιμο` 用日常忙碌的表达，`τη` 指生活。

邀请：`Θα χωρούσε κι αυτό το σκυλάκι σε μια γωνίτσα της οθόνης σου; Μια σταλιά χώρο θέλει.` 回译：这只小狗也能挤进你的屏幕一角吗？它只想要那么一点点位置。条件式、`κι αυτό`（它也）和口语“小小一点”共同表达心软式请求。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Αζόρ` | 当地可见、能唤起狗伙伴联想的狗名，不声称当前排名第一 | 已核实狗名身份。[希腊语作者的狗名语境](https://melindasblog.me/2019/11/08/%CE%BF%CE%BD%CF%8C%CE%BC%CE%B1%CF%84%CE%B1-%CF%83%CE%BA%CF%8D%CE%BB%CF%89%CE%BD-%CF%83%CF%84%CE%B7%CE%BD-%CE%B1%CF%81%CF%87%CE%B1%CE%AF%CE%B1-%CE%B5%CE%BB%CE%BB%CE%AC%CE%B4%CE%B1/)直接把 Αζόρ 作为称呼狗的名字举例；[当地狗名文章](https://podaraki.gr/blog/onomata-gia-skylous)可作为命名文化参考，未作名字热度统计 | `τον δικό σου μικρό Αζόρ`（属于你的小 Αζόρ）只作亲近狗伙伴的比喻 |
| `Doge`、`Shiba Inu` | 柴犬外形与狗梗 | 已核实。[Kathimerini 希腊语报道](https://www.kathimerini.gr/world/563041099/kabosu-pethane-o-skylos-thrylos-ton-meme-kai-prosopo-toy-dogecoin/)记录 Doge 及犬种关联 | 从小脸想到梗图，再转到任务栏旁的伙伴 |
| `σκυλάκι`、`τετράποδος φίλος` | 小狗、四脚朋友 | [希腊语词典](https://el.wiktionary.org/wiki/%CF%83%CE%BA%CF%85%CE%BB%CE%AC%CE%BA%CE%B9)可核对前者作为狗的指小词；后者为描述性候选表达，无搜索量数据 | 用于陪伴、外形和装扮场景，明确是狗而非该词的植物义 |
| `εικονικός σκύλος` | 虚拟狗，产品搜索组合 | 按本作事实与当地语义组织 | 与表情、收集帽子和服装连接 |
| `Bongo Cat`、`παρέες στην επιφάνεια εργασίας` | 同类桌宠名与电脑桌面伙伴 | [希腊语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=greek)标题可核对，类型标签仍为英语 `Desktop Companion`；希腊语表达为本稿改写 | 用于受众开头，不把英语回退标签当作希腊语证据 |

尾段回译：简而言之，如果你喜欢 Bongo Cat 这样的电脑桌面伙伴，又对狗狗特别心软，Lucky Dog Rise 也许适合你。柴犬的小脸可能让你想起 Doge，但这里的狗狗不会只待在图片里。它会成为任务栏旁的一位四脚朋友，陪你打字、工作或游戏。一只虚拟狗，有逗趣的小脸和可以收集的帽子。有点像属于你的小 Αζόρ，只不过它的家在屏幕角落，穿着你为它挑选的衣服。

取舍：没有在 Doge、犬种之外再硬塞未充分核实当地用法的 Cheems 音译；使用有依据的 Αζόρ 和狗相关短语，保留 Lucky Dog 的正式角色身份。

## 罗马尼亚语

采用 `tu` 的亲近语气。用自然的“对狗有一份偏爱”，不硬译英语 dog person；资源名为 `Puncte de Noroc`，在句中按语法变化。

最大的幸运：`Cel mai mare noroc al lui e că te-a întâlnit.` 回译：它最大的幸运，就是遇见了你。

Slogan：`Zilele agitate se destramă. Un cățel le coase la loc` 回译：忙碌的日子逐渐散了线，一只小狗把它们缝回原样。`le` 指日子，`la loc` 表达修复。

邀请：`S-ar găsi și pentru el un colțișor pe ecranul tău? Doar puțin loc, atât îi trebuie.` 回译：你的屏幕上，能不能也给它找到一个小角落？只要一点点地方，它需要的就是这些。条件式和 `și pentru el / doar` 保留也加入与小小要求。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Grivei` | 有当地文化使用基础的狗名 | 已核实。[DEXonline 汇集的词典条目](https://dexonline.ro/definitie/grivei)明确收有狗名义，也有花毛／颜色相关义；本稿只采用名字联想，不声称游戏犬的毛色与词源相同 | `un mic Grivei`（一个小 Grivei）比喻熟悉狗伙伴 |
| `Doge`、`Shiba Inu` | 狗梗名和柴犬外形 | 已核实。[Digi24 罗马尼亚语报道](https://www.digi24.ro/stiri/externe/mapamond/a-murit-kabosu-cainele-din-celebra-mema-doge-2802503)记录名称和犬种关联 | 从网上熟悉的脸转到会持续陪伴的桌面狗 |
| `cățelușul`（来自 `cățeluș`）、`prieten patruped` | 亲昵的小狗、四脚朋友 | [罗马尼亚语词典汇编](https://dexonline.ro/definitie/c%C3%AEine/964047)可核对小狗指小称呼；四脚朋友为自然描述性组合 | 使用自然的定指词形，接到小表情、服装和不需要整个院子的玩笑 |
| `câine virtual` | 虚拟狗，产品搜索组合 | 按本作事实与当地语义组合，未取得搜索量排名 | 以任务栏位置和表情连接 |
| `Bongo Cat`、`companionii de desktop` | 同类桌宠名与桌面伙伴 | [罗马尼亚语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=romanian)标题和 `Însoțitor pentru desktop` 标签可核对；本稿将其自然改写为 companionii | 用于受众引入 |

尾段回译：简而言之，如果你喜欢 Bongo Cat 这样的桌面伙伴，又对狗有一份特别偏爱，Lucky Dog Rise 或许适合你。Doge 让一张柴犬脸变得家喻户晓；这只小狗则会走出梗图来陪伴你。一只任务栏旁的虚拟狗，有讨喜的小表情，也有能收集的衣服。它也许会让你想起一个小 Grivei，只是这位四脚朋友不要求整个院子。给它挑顶帽子、配套衣服，让桌面一角成为你们相见的地方。

取舍：不因 Grivei 的词源带毛色含义就完全排除狗名联想，但明确只作熟悉名字的比喻；没有再添加同作用的 Azorel 或未经核实的梗名。

## 马来语

使用 `kamu`，叙述动物用 `ia` 或亲昵的 `si kecil`。用本地电脑词 `skrin / tetikus / kekunci / tetingkap / bar tugas`，与印尼语的 `layar / mouse / tombol / bilah tugas` 分开。幸运点数统一为 `Mata Tuah`。

最大的幸运：`Bertemu dengan kamu ialah tuah terbesar dalam hidupnya.` 回译：与你相遇，是它生命中最大的幸运。

Slogan：`Hari sibuk mengoyak hati. Anjing kecil menjahitnya semula` 回译：忙碌的日子撕破了心，小狗把它重新缝回。以心的损耗表达日常疲惫，修补对象明确。

邀请：`Boleh si kecil ini tumpang satu sudut di skrin kamu juga? Sedikit ruang pun sudah cukup.` 回译：这个小家伙能不能也借你的屏幕一角待着？只要一点空间就已经够了。`tumpang` 表现轻轻借住、不想多打扰，`juga` 保留也加入。

| 采用词／实际写法 | 中文释义与搜索联想 | 来源与名称核实 | 连接方式 |
| --- | --- | --- | --- |
| `Hachiko` | 本地马来语报道中可见的忠犬名字，唤起与狗相伴的文化记忆 | 已核实。[Astro Awani 马来语报道](https://www.astroawani.com/berita-dunia/hachiko-anjing-paling-setia-cecah-usia-100-tahun-426387)专门介绍 Hachiko；[UtusanTV](https://utusantv.com/2024/05/28/hachiko-setia-seekor-anjing-yang-menanti-tuannya-yang-telah-mati-selama-9-tahun-9-bulan-15-hari-tanpa-jemu/)也使用该名。不是对搜索量的证明 | 只取忠诚联想，随后单独说明柴犬脸与 Doge，不把 Hachiko 说成柴犬或游戏角色 |
| `Doge`、`Shiba Inu` | 狗梗与柴犬，网络狗形象联想 | 已核实马来语写法与关系。[马来语 Wikinews 孵育页](https://incubator.wikimedia.org/wiki/Wn/ms/Kabosu%2C_anjing_yang_popular_dengan_meme_%22Doge%22_meninggal_dunia)使用 Doge 及犬种；属当地语言用例，不是原创采访或热度榜 | 与 Hachiko 的忠诚记忆分开叙述，连接游戏的柴犬形象 |
| `anak anjing maya`、`kawan berkaki empat` | 虚拟小狗、四脚伙伴，描述性搜索组合 | [DBP 的英马词典搜索结果](https://prpm.dbp.gov.my/Cari1?d=54140&keyword=pup)可核对 anak anjing；完整组合按产品事实写作，没有单独搜索量数据 | 通过任务栏、服装与共度日常串联 |
| `teman setia`、`membela anjing` | 忠实伙伴、养狗的表达 | 前者与 Hachiko 报道的忠诚主题相接，后者为本地语义下的产品相关候选表达 | 实际写入“即使家里暂时不能养狗”的场景，不要求现实条件或现实养宠 |
| `Bongo Cat`、`teman desktop` | 同类桌宠名与桌面伙伴 | [马来语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=malay)标题可核对，但类型标签仍是英语；teman desktop 为本稿自然表达 | 用于共用开头，不称为已验证的马来语官方标签 |

尾段回译：总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更喜欢狗，Lucky Dog Rise 也许适合你。Hachiko 的故事让人想起忠实的伙伴；柴犬的脸则容易让人想起 Doge 梗。这个小家伙把那份亲近带到你的屏幕角落。一只虚拟小狗、一位四脚朋友，在任务栏附近陪你工作、休息或游戏。收集帽子和衣服，为这位忠实伙伴选个模样。即使暂时还不能在家里养狗，也许在你那些打开的窗口之间，已经能给它留一点位置。

取舍：没有将印尼语 anabul 直接移植过来。马来语 anak bulus／si bulus 在本次找到的 [Bernama 本地用例](https://lifestyle.bernama.com/news.php?id=2534780)里明显偏向猫，不能由此声称是狗专属别称；Cheems 的较强马来西亚新闻来源是英语，也不能直接证明马来语搜索偏好。因此本段采用有当地语言依据的 Hachiko、Doge 和准确狗相关短语。

## 本地核对与待测试范围

- 已逐段顺读并反向检查关键句、邀请关系与七份尾段。没有取得母语者审校；来源支持选词与词义，不为整篇自然度或搜索效果作认证。
- 采用词表在尾段实际出现；自然变化的词按词形核对，如 Bongo Cathez、siba inu-pofija、masaüstü dostlarını、cățelușul。没有为了与中文结构对应加入大小变化或未确认的游戏功能。
- JSON 保留全部 30 个语言键及字符串身份，30 种语言均有长短描述；与第二批只有指定的 14 个字段不同，原有语言与其他字段相同。
- 七份 BBCode 与 JSON 解码后逐字一致，简介与纯文本文件一致，字符数为 210–244。媒体引用完整、标签配对，没有 Markdown 标题、围栏或双重 HTML 编码混入正文。
- 原始下载 SHA-256 仍为 `b89476f999278652f3bfa287875427081e11ecb7c2002670b1541cdf7bde5a2c`；第一、二批累积 JSON 未修改。
- 主人先提供了一组显示英语正文的马来语页面截图，后续截图已显示当前马来语稿，并与本地 BBCode 一致。本次只记录可见结果，不推断此前英语回退的原因。
- 马来语长描述实际截图抽查通过：开头两句与 Slogan 居中正常；邀请句及纸箱插图靠右；媒体后关键词尾段正常显示。关键语义保留小狗因遇见玩家而幸运，以及“也借一个小角落，只要一点空间”的委婉邀请。
- 本次截图未覆盖简介；其余 6 种语言的实际排版及本批 7 份简介仍待页面抽查。马来语媒体的本地化画面可见，但静态截图不能确认完整动画／视频播放；前两批的截图也不能替代本批其余语言的验证。
