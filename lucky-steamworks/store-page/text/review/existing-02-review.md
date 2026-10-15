# 原有语言第二批：备份与尾部关键词补充

日期：2026-10-15。状态：本地文件及两页完整导入稿已准备，新增尾段待主人审阅及 Steam 页面抽查。第三批尚未开始。

## 修改范围

- 法语、德语、捷克语、韩语、越南语各保存完整长描述 BBCode 和独立简介，共 10 份文件。
- 仅法语、德语、韩语、越南语追加媒体后的一个关键词段。四种语言的原长描述在 JSON 中逐字符作为前缀保留，正文、Slogan、邀请语和媒体不改。
- 捷克语连同已有尾段原样备份；五种简介均与原 JSON 字段完全一致。
- 本地复制稿只对 JSON 字段解码一层 HTML 实体，并将 CRLF 转为 LF；原有其他空白保留。四份旧 Markdown 原样归档，捷克语此前没有对应旧稿。
- 本批以 [Demo 补译完成稿](../upload/storepage_1315198_batch-03_review.json)及[正式版首次同步稿](../upload/storepage_602645_all_Main_review.json)为起点；前面的第一批只做备份，没有额外上传稿。

## 文件入口

| 语言 | 长描述 | 简介 | 简介字符数 |
| --- | --- | --- | ---: |
| 法语 | [Français BBCode](../about/关于此游戏-Français.bbcode.txt) | [纯文本](../short/简介-Français.txt) | 284 |
| 德语 | [Deutsch BBCode](../about/关于此游戏-Deutsch.bbcode.txt) | [纯文本](../short/简介-Deutsch.txt) | 269 |
| 捷克语 | [Čeština BBCode](../about/关于此游戏-Čeština.bbcode.txt) | [纯文本](../short/简介-Čeština.txt) | 271 |
| 韩语 | [한국어 BBCode](../about/关于此游戏-한국어.bbcode.txt) | [纯文本](../short/简介-한국어.txt) | 155 |
| 越南语 | [越南语 BBCode](../about/关于此游戏-越南语.bbcode.txt) | [纯文本](../short/简介-越南语.txt) | 284 |

## 选词方法与证据边界

先分别调查狗的别称、传统狗名及网红狗／狗梗，再考虑玩家可能输入的搜索词。以下区分“已有用法依据的称呼或文化名称”和“据此推断的产品搜索短语”。词典只核实词义；当地报道、公开使用和文化资料用于补充实际语境。

本批没有取得 Steam 搜索量、自动补全排名或点击转化数据，也没有验证 Steam 对各词的收录与排序。因此不将传统狗名或普通爱称冒充当下流行榜上的热词，不承诺搜索效果。Doge 等属于已有文化关联，不能据旧报道宣称其在 2026 年仍处于热度峰值。

四种语言可沿用 Bongo Cat 与狗偏好的开头，但后文分别围绕当地词表创作，没有翻译中文“大狗”的缩放句，也没有新增缩放、付费或未来开发承诺。Bongo Cat 的写法及韩语桌面伙伴、任务栏术语参考[该游戏韩语 Steam 页面](https://store.steampowered.com/app/3419430/Bongo_Cat/?l=koreana)。

## 法语

### 采用词与覆盖

| 关键词及中文释义 | 为什么可能被搜索、核实依据 | 文案实际写法与连接 |
| --- | --- | --- |
| toutou，狗狗的亲昵说法 | [法兰西学院词典](https://www.dictionnaire-academie.fr/article/A9T1682)确认其狗的称呼义；适合想到狗而不只想到 jeu 的用户。不是搜索量证明。 | un toutou；把狗梗带来的笑意连接到桌面狗狗。 |
| Médor，法语文化中的传统狗名 | [里昂市立图书馆答复](https://www.guichetdusavoir.org/question/voir/7270)讨论该狗名的文化来历；作为熟悉的狗名意象，不宣称是当年最热门名字。 | votre petit Médor du quotidien；比喻日常伙伴，不改角色正式名称。 |
| Shiba Inu，柴犬；Doge，柴犬网络梗名 | [法语 Doge 文化资料](https://fr.wikipedia.org/wiki/Doge_(m%C3%A8me))核实狗名与柴犬形象的关系及当地写法。 | Un museau de Shiba Inu, un air de Doge；明确是形象联想，不声称联名或使用原梗角色。 |
| chien virtuel，虚拟狗；compagnon de bureau，桌面伙伴 | 根据普通词义、游戏实际形式推断的产品搜索组合，未验证搜索量。 | Un chien virtuel、les compagnons de bureau；与屏幕、任务栏和陪伴场景相连。 |

没有继续追加 Rex、Mirza 等更多传统狗名：本段已用 Médor 承担狗名入口，其他名字留作候选，避免一段变成名字清单。

### 新增尾段

En bref, si vous aimez les compagnons de bureau comme Bongo Cat mais avez un faible pour les chiens, Lucky Dog Rise pourrait bien vous plaire. Un museau de Shiba Inu, un air de Doge... et voilà un toutou qui quitte les mèmes pour s'installer près de votre barre des tâches. Un chien virtuel, votre petit Médor du quotidien, qui vous tient compagnie pendant que vous tapez, travaillez ou jouez. Avec ses mimiques, ses chapeaux et les tenues à collectionner, ce compagnon à quatre pattes trouve peu à peu son style. Peut-être qu'entre toutes vos fenêtres, il reste aussi une petite place pour lui.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，又偏爱狗狗，Lucky Dog Rise 或许很合你心意。柴犬的小脸，几分 Doge 的神态……这只狗狗从梗图里的笑意走到你的任务栏旁。一只虚拟狗，你日常里的小 Médor，陪你打字、工作或游戏。表情、帽子和可收集的服装，让这位四脚伙伴逐渐拥有自己的风格。也许在你打开的那些窗口之间，还能给它留下一小块地方。

## 德语

### 采用词与覆盖

| 关键词及中文释义 | 为什么可能被搜索、核实依据 | 文案实际写法与连接 |
| --- | --- | --- |
| Wauwau，童语中的狗狗 | [Duden 词条](https://www.duden.de/rechtschreibung/Wauwau)核实称呼义和语域；以一个亲昵称呼串入，不把整段写成幼儿语言。 | Ein kleiner Wauwau；自然指眼前的小狗。 |
| Bello，传统狗名 | [Duden 狗名词条](https://www.duden.de/rechtschreibung/Bello)核实名字；采用熟悉狗名入口，不宣称当代狗名排行。 | dein ganz eigener Bello auf dem Desktop；你的桌面伙伴的比喻昵称，不添加游戏改名功能。 |
| Shiba Inu，柴犬；Doge，狗梗名 | [德语 Doge 文化资料](https://de.wikipedia.org/wiki/Doge_(Netzkultur))核实当地用法及柴犬关联。 | Shiba-Inu-Gesicht、Doge；犬种按德语复合词写法融入句子，Doge 保留完整名称。 |
| Vierbeiner，四脚伙伴 | Duden 的 Wauwau 页面列出此狗相关近义称呼；较自然的另一搜索入口。 | der Vierbeiner；不单列同义词。 |
| virtueller Hund，虚拟狗；Desktop-Begleiter，桌面伙伴 | 从当地词义和游戏形式推断的产品搜索表达，未验证搜索量。 | Desktop-Begleiter、Als virtueller Hund；按句中格变化使用自然词形。 |

Wuffi 留作候选，本批未取得比已采用 Wauwau／Bello 更充分的依据，也不为增加词数硬塞多个昵称。没有借其他犬种的影视角色名改写游戏设定。

### 新增尾段

Kurz gesagt: Wenn du Desktop-Begleiter wie Bongo Cat magst, aber eher ein Hundemensch bist, könnte Lucky Dog Rise zu dir passen. Bei seinem Shiba-Inu-Gesicht denkst du vielleicht an Doge. Hier bleibt der Vierbeiner aber kein Meme: Als virtueller Hund macht er es sich neben deiner Taskleiste gemütlich und begleitet dich beim Tippen, Arbeiten und Spielen. Ein kleiner Wauwau, vielleicht dein ganz eigener Bello auf dem Desktop – mit lustigen Gesichtsausdrücken, Hüten und Outfits zum Sammeln. Und falls du dir schon lange einen Hund wünschst: Für diesen treuen Kumpel reicht vielleicht schon eine Ecke zwischen deinen Fenstern.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更是个爱狗的人，Lucky Dog Rise 或许适合你。它的柴犬脸也许让你想到 Doge，但这里的四脚伙伴不只停在梗图里：它作为一只虚拟狗，舒舒服服待在任务栏旁，陪你打字、工作和游戏。一只小 Wauwau，也许就是你桌面上自己的 Bello，带着有趣的表情，以及可收集的帽子和服装。如果你一直想养狗，也许只要在窗口之间留个角落，就足够这位忠实小伙伴待着了。

## 韩语

### 采用词与覆盖

| 关键词及中文释义 | 为什么可能被搜索、核实依据 | 文案实际写法与连接 |
| --- | --- | --- |
| 댕댕이，网络语“狗狗”；멍멍이，亲昵的“汪汪／狗狗” | [韩国语新词研究](https://journal.kci.go.kr/semantics/archive/articlePdf?artiId=ART002853545)说明两词关系；[江南区“댕댕이 학교”公开活动](https://www.gangnam.go.kr/board/B_000031/1072971/view.do?mid=ID01_0313)提供超出词典的实际使用依据。 | 바탕화면 속 댕댕이、작업 표시줄 옆의 멍멍이；分别连接桌面和任务栏场景。 |
| 시바견，柴犬；도지(Doge)，Doge 的韩语及原文写法 | [韩语媒体关于 Kabosu 的报道](https://www.khan.co.kr/article/202405241959001/?kref=rta)确认柴犬形象及网络文化关联；[韩语 Doge 文化条目](https://m.namu.moe/w/Shibe%20doge)补充梗名及当地传播写法。后者为社区资料，交叉参考，不作为热度排行。 | 시바견、도지(Doge) 밈；明确谈的是狗梗，不谈代币或投资。 |
| 강아지，小狗；가상 반려견，虚拟伴侣犬；바탕화면，电脑桌面 | 강아지 为普通狗相关词；后两者与实际游戏形式组成可能的搜索表达，未验证搜索量。 | 강아지、나만의 가상 반려견、바탕화면 속 댕댕이；不用角色名替换掉所选称呼。 |

未另外塞入未经本批核实的流行狗昵称，也未为了与越南语一致而硬加 Cheems 韩译。Doge 与韩国本地狗狗称呼已构成清楚的文化和搜索入口。

### 新增尾段

한마디로, Bongo Cat 같은 데스크톱 친구가 좋지만 고양이보다 강아지에게 더 마음이 간다면, Lucky Dog Rise가 잘 맞을지도 몰라요. 시바견 얼굴을 보면 도지(Doge) 밈이 떠오르나요? 이번에는 짤을 닫아도, 바탕화면 속 댕댕이가 곁에 남아 있어요. 작업 표시줄 옆의 멍멍이, 나만의 가상 반려견이 타자를 치거나 일하고 게임하는 동안 함께해요. 모자와 옷을 모아 이 아이에게 어울리는 모습으로 꾸밀 수도 있고요. 아직 집에서 강아지를 키울 수 없더라도, 화면 한구석에는 이 아이가 머물 작은 자리 하나쯤 있을지도 몰라요.

### 中文回译

一句话，如果你喜欢 Bongo Cat 这样的桌面朋友，却更偏爱小狗，Lucky Dog Rise 或许正适合你。看到柴犬脸，会想到 Doge 梗吗？这一次，即使关掉梗图，桌面上的 댕댕이 也还留在你身边。任务栏旁的 멍멍이、属于你的虚拟伴侣犬，会陪你打字、工作和游戏。你也可以收集帽子和衣服，把它装扮成适合自己的样子。即使暂时还不能在家养狗，也许屏幕的一角，仍能留一个让这孩子待着的小位置。

## 越南语

### 采用词与覆盖

| 关键词及中文释义 | 为什么可能被搜索、核实依据 | 文案实际写法与连接 |
| --- | --- | --- |
| cún con，亲昵的小狗称呼 | [Tuổi Trẻ 的狗题材报道](https://muctim.tuoitre.vn/thong-diep-gia-dinh-phang-phat-trong-lan-gio-chua-lanh-tim-nha-cho-boss-70259.htm)实际使用 cún、cún con；可作为想到狗时的日常叫法。 | Một bé cún con；自然连接任务栏旁的狗狗。 |
| boss，宠物“主子”；con sen，宠物主人的玩笑称呼 | 同一 Tuổi Trẻ 报道实际将这组称呼用于狗与主人，不是仅限猫的猜测。 | Một “boss” nhỏ、chẳng bắt “con sen” phải tất bật phục vụ；通过“不会让你忙着伺候”串入称呼，同时保持小狗不索取的定位。 |
| Cheems，网红柴犬梗名 | [Tiền Phong 的本地报道](https://tamviet.tienphong.vn/netizen-thuong-nho-nguoi-ban-bon-chan-duoc-che-meme-nhieu-nhat-mang-xa-hoi-post1562011.tpo)使用 Cheems、Shiba 及当地 meme 语境。 | Doge hay Cheems；写熟悉的形象联想，不声称本作小狗就是 Cheems。 |
| Doge，狗梗名；Shiba Inu，柴犬 | [越南语 Shiba 介绍](https://champetsfamily.com/blog/shiba-inu/cho-shiba-cuoi-cheems-cau-vang/)区分 Doge、Cheems 与犬种；这是当地商家文化介绍，与本地新闻交叉参考，不采用其商业宣传或热度结论。 | gương mặt Shiba Inu、Doge；保持两个狗梗与犬种的区别。 |
| chó ảo，虚拟狗；chó meme，狗梗／梗图狗 | 据普通词义及当地报道中的 chó、meme 语境推断的搜索组合，不是实测高频词。 | một chú chó ảo、những chú chó meme；用从梗图笑意到桌面陪伴的关系串联。 |

Cậu Vàng 留作候选但不采用：资料中包含文学／电影角色及特定选角争论，本批尝试以吉利狗名连接 Lucky Dog 后，仍容易把段落带向角色解释；现有 boss、cún con、Cheems 已能自然连接本作，不强行堆入。

### 新增尾段

Tóm lại, nếu bạn thích những người bạn trên màn hình như Bongo Cat nhưng lại mê chó hơn, Lucky Dog Rise có thể hợp với bạn. Nhìn gương mặt Shiba Inu là nhớ ngay đến Doge hay Cheems? Lần này, niềm vui từ những chú chó meme có thể ở lại bên bạn dưới hình hài một chú chó ảo. Một bé cún con nép cạnh thanh tác vụ, cùng bạn gõ phím, làm việc hay chơi game, với những nét mặt ngộ nghĩnh và tủ mũ áo để sưu tầm. Một “boss” nhỏ, nhưng chẳng bắt “con sen” phải tất bật phục vụ: cún chỉ muốn ở gần bạn thôi. Chưa nuôi được chó ở nhà thì biết đâu, giữa những ô cửa sổ, vẫn còn một góc nhỏ cho người bạn bốn chân này.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样的屏幕伙伴，却更爱狗，Lucky Dog Rise 或许适合你。看到柴犬脸就想起 Doge 或 Cheems？这一次，狗梗带来的快乐，可以以一只虚拟狗的模样留在你身边。小狗依偎在任务栏旁，陪你打字、工作或游戏，带着有趣的表情，还有可收集的一柜子帽子和衣服。一个小“主子”，却不会让“铲屎官”忙着伺候：它只是想靠近你。暂时不能在家养狗的话，说不定在那些窗口之间，仍能给这位四脚朋友留个小角落。

## 两页完整导入 JSON

| 页面 | 本批完整累积稿 | itemid |
| --- | --- | --- |
| Demo | [storepage_1315198_existing-02_review.json](../upload/storepage_1315198_existing-02_review.json) | 1315198 |
| 正式版 | [storepage_602645_existing-02_Main_review.json](../upload/storepage_602645_existing-02_Main_review.json) | 602645 |

这两份可分别作为对应页面的完整导入文件，无需与旧批次 JSON 手动合并。各含 30 种语言；相比各自起始稿，只有法、德、韩、越四个 about 字段追加尾段。其余 26 种语言、全部 30 个简介、页面身份和系统需求等非文字字段保持原值，两页全部 60 个文字字段仍一致。

原始下载和此前上传快照不改写。AI 没有上传、保存或发布到 Steam；静态文件校验不代表新增尾段已经过页面预览或母语审校。

## 本地校验

- 逐字段核对仅四个目标 about 改变，且以原字段的完整值开头；追加内容恰为所列尾段。
- 核对 10 份复制稿与对应 JSON 的一次解码结果逐字一致；捷克语含旧尾段。
- 核对每种语言两个正文媒体引用及原有标签位置不变；新增段落位于正文媒体之后。
- 检查确认采用的关键词在新增尾段中的实际词形全部覆盖，新段落只有完整的一对 p 标签。
- 核对四份旧 Markdown 归档前后字节相同；此前下载和上传快照保持原始字节。
- 核对两页应用身份、其他字段、30 语言完整性及共用文字一致；简介最大字符数 284，未修改任何简介。
- 本批只处理第二批，第三批尚未开始。

归档旧稿：[法语](../about/archive/关于此游戏-Français.md)、[德语](../about/archive/关于此游戏-Deutsch.md)、[韩语](../about/archive/关于此游戏-한국어.md)、[越南语](../about/archive/关于此游戏-越南语.md)。
