# 原有语言第三批：备份与地区关键词尾段

日期：2026-10-15。状态：五种语言的本地文件及两页完整累积导入稿已准备；新增尾段待主人审阅及 Steam 页面抽查。本轮原有 13 种语言已全部完成本地整理。

## 范围与文件入口

| 语言／地区 | Steam 键 | 长描述 | 简介 | 简介字符数 |
| --- | --- | --- | --- | ---: |
| 繁体中文 | tchinese | [BBCode](../about/关于此游戏-繁体中文.bbcode.txt) | [纯文本](../short/简介-繁体中文.txt) | 126 |
| 西班牙语（西班牙） | spanish | [BBCode](<../about/关于此游戏-Español (España).bbcode.txt>) | [纯文本](<../short/简介-Español (España).txt>) | 296 |
| 西班牙语（拉丁美洲） | latam | [BBCode](<../about/关于此游戏-Español (Latinoamérica).bbcode.txt>) | [纯文本](<../short/简介-Español (Latinoamérica).txt>) | 296 |
| 葡萄牙语（葡萄牙） | portuguese | [BBCode](<../about/关于此游戏-Português (Portugal).bbcode.txt>) | [纯文本](<../short/简介-Português (Portugal).txt>) | 296 |
| 葡萄牙语（巴西） | brazilian | [BBCode](<../about/关于此游戏-Português (Brasil).bbcode.txt>) | [纯文本](<../short/简介-Português (Brasil).txt>) | 293 |

- 繁中正文、简介及此前确认的关键词尾段原样保留，不重复追加。
- 另外四种仅在原正文媒体之后各加一个关键词段。JSON 原字段的全部字符作为固定前缀保留；原正文、Slogan、邀请语、媒体及全部简介不改。
- 本地复制稿只做一次 HTML 实体解码和 CRLF 转 LF。繁中原 BBCode 已有，核对发现只比 JSON 解码结果多一个末尾 LF；本批移除此额外换行，文字与标签均未改。
- 五份旧 Markdown 原始字节移入 archive，不作为新文案来源。两份第二批 JSON 及此前下载、上传快照保持不变。

## 研究方法与地区边界

本批分别寻找日常狗称呼、狗名或网络狗文化，再推断用户脑中想着狗时可能输入的词。普通爱称、经典梗和根据产品形式推断的搜索短语分开记录；没有获得 Steam 搜索量、自动补全排序或转化数据，不把这些候选都称为当下热度榜上的热词，也不保证收录与排序。

地区差异只用于新增尾段。西班牙版使用 perrete、tecleas、conjuntos 等自然表达；拉美版采用以墨西哥用法为重要线索的 lomito、Firulais，不能由此推断所有拉美国家都同样常用。葡萄牙版使用 tu 的动词变位、ecrã、cãozinho；巴西版使用 você、tela、cachorro、doguinho。两组地区版各自围绕词表成文，不只替换电脑／屏幕的几个词。

Bongo Cat 作为共用受众入口，写法及桌面伙伴关系沿用前批已经核对的依据。四个新尾段都没有机械复制中文“大狗”与缩放功能句，也没有新增付费、联网或开发计划承诺。

## 西班牙语：西班牙

### 采用词与覆盖

| 关键词／中文释义 | 使用依据与可能搜索的原因 | 文案实际写法 |
| --- | --- | --- |
| perrete，亲昵的狗狗称呼 | [西班牙 RTVE《Dog House》](https://www.rtve.es/play/videos/dog-house/chiqui-emma-alex-dog-house-chenoa/17206283/)在狗狗找家语境中实际使用；这是当地称呼证据，不是搜索量证明。 | Un perrete junto a la barra de tareas |
| Doge、Cheems，网红柴犬梗名；Shiba Inu，柴犬 | [RTVE 对两个狗梗的介绍](https://www.rtve.es/noticias/20210212/san-valentin-trae-vuelta-doge-cheems/2075278.shtml)同时核实名称、柴犬形象及当地梗文化。 | carita de Shiba Inu、Doge o a Cheems |
| perro virtual，虚拟狗；compañeros de escritorio，桌面伙伴 | 按目标语言词义与实际产品形式推断的搜索组合，未验证高频。 | un perro virtual、los compañeros de escritorio |
| peludo，毛茸茸的伙伴 | 普通形容词名词化的亲昵表达；本段狗语境明确。只作为狗称呼的补充，不宣称为独立热门词。 | un pequeño peludo |

Toby 留作传统狗名候选；本批找到的零散使用不足以证明其当前搜索价值，已有 Doge、Cheems 作为狗名文化入口，不为补词数硬加。chucho 可能带贬义或杂种犬指向，本作以柴犬形象为核心，当前段落用 perrete 更自然，因此不采用。

### 新增尾段

En resumen, si te gustan los compañeros de escritorio como Bongo Cat pero eres más de perros, Lucky Dog Rise puede ser para ti. ¿Esa carita de Shiba Inu te recuerda a Doge o a Cheems? Aquí, la sonrisa de los memes se queda contigo en forma de un perro virtual. Un perrete junto a la barra de tareas, un pequeño peludo que te acompaña mientras tecleas, trabajas o juegas. Entre sus gestos, los sombreros y los conjuntos que puedes coleccionar, este compañero de cuatro patas va encontrando su propio estilo. Quizá también haya un huequecito para él entre tantas ventanas.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更是个爱狗的人，Lucky Dog Rise 或许适合你。这张柴犬脸，会让你想起 Doge 或 Cheems 吗？在这里，梗图带来的笑意会以虚拟狗的模样留在你身边。任务栏旁的一只 perrete，一个毛茸茸的小伙伴，陪你打字、工作或游戏。它的表情，以及你能收集的帽子和服装，让这位四脚伙伴逐渐找到自己的风格。也许在那么多窗口之间，还能给它留一个小空位。

## 西班牙语：拉丁美洲

### 采用词与覆盖

| 关键词／中文释义 | 使用依据与可能搜索的原因 | 文案实际写法 |
| --- | --- | --- |
| lomito，当地网络和亲昵语境中的狗狗称呼 | [墨西哥 Lomito A.C. 救助组织](https://lomito.mx/)以该词命名并用于狗狗救助；[墨西哥报道](https://petlife.mx/tendencias/2024/07/27/el-origen-de-la-palabra-lomito-por-que-le-dicen-asi-los-perros-5249.html)讨论其狗称呼用法。取用实际称呼，不采信未经交叉核实的单一词源说法。 | un lomito con carita de Shiba Inu |
| Firulais，常见狗名及泛称 | [Milenio 的当地狗名报道](https://www.milenio.com/virales/firulais-por-que-se-llama-asi-a-los-perritos-origen)说明其称呼狗的用法。报道中的 free of lice 词源只是一种说法，本批不将其当定论，更不写入商店正文。 | le digas Firulais a cualquier perrito；是玩家称呼习惯，不改角色正式名称，也不添加取名功能。 |
| Cheems、Doge、Shiba Inu | RTVE 核实名称与犬种关系；[墨西哥当地 Cheems 主题商店报道](https://grupoanimal.mx/tendencias/abarrotes-cheems-meme-tienda-cdmx)补充实际文化传播线索。使用历史传播证据，不称为本年热度排行。 | Cheems y Doge、carita de Shiba Inu |
| perrito virtual，虚拟小狗；compañeros de escritorio，桌面伙伴 | 按产品形式推断的搜索短语；perrito 是日常称呼，未验证搜索量。 | Un perrito virtual、los compañeros de escritorio |

未追加另一串传统名字，避免段落成为狗名清单。Firulais 与 lomito 以墨西哥文化为主要线索，保留其特色，同时让整个段落的语法和陪伴场景能被其他拉美用户理解。

### 新增尾段

En pocas palabras, si te gustan los compañeros de escritorio como Bongo Cat pero prefieres a los perros, Lucky Dog Rise podría ser para ti. Tal vez conozcas a Cheems y Doge por sus memes, o le digas Firulais a cualquier perrito: aquí hay un lomito con carita de Shiba Inu que quiere hacerte compañía. Un perrito virtual junto a la barra de tareas, con sus propias expresiones y un montón de sombreros y ropa para coleccionar, mientras escribes, trabajas o juegas. Y aunque todavía no puedas tener un perro en casa, quizá este amigo de cuatro patas encuentre también un rinconcito entre tus ventanas.

### 中文回译

简单来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更偏爱狗，Lucky Dog Rise 或许适合你。也许你从梗图认识 Cheems 和 Doge，也许你会把任何小狗都叫作 Firulais：这里有一只长着柴犬脸的 lomito，想陪在你身边。任务栏旁的一只虚拟小狗，有自己的表情，还有许多可收集的帽子和衣服，陪你打字、工作或游戏。即使你还不能在家养狗，也许这位四脚朋友，仍能在你的窗口之间找到一个小角落。

## 葡萄牙语：葡萄牙

### 采用词与覆盖

| 关键词／中文释义 | 使用依据与可能搜索的原因 | 文案实际写法 |
| --- | --- | --- |
| patudo，宠物的亲昵称呼 | [Priberam 词典](https://dicionario.priberam.org/patudo)确认宠物狗或猫的含义；[葡萄牙 União Zoófila 的实际用法](https://www.uniaozoofila.org/en/inicio/parcerias/)提供宠物语境。段落已明确犬种，避免单独使用时的其他含义。 | Um patudo com expressões divertidas |
| cãozinho，小狗的亲昵称呼；cãozinho virtual，虚拟小狗 | cão 的普通指小形式与实际产品结合；是搜索表达推断，不是热度实测。 | um cãozinho virtual |
| Doge，网红狗梗名；Shiba Inu，柴犬 | [葡萄牙 Renascença 报道](https://rr.pt/noticia/vida/2024/05/24/morreu-kabusu-a-cadela-que-da-o-focinho-a-dogecoin/379724/)确认 Doge 在当地的名称及网络文化关联；犬种联想结合前述 RTVE 及原正文设定核对。 | focinho de Shiba Inu、o Doge |
| amigo de quatro patas，四脚朋友；companheiros de ecrã，屏幕伙伴 | 普通陪伴称呼和根据产品形式推断的搜索组合；不宣称为热词。 | este amigo de quatro patas、companheiros de ecrã |

本版用已核实的 Doge 承担网络狗名称入口，以 patudo、cãozinho 串入当地亲昵称呼，没有为了凑狗名数量追加未经本批核实的名称。巴西的 vira-lata caramelo 不直接移植到葡萄牙版。

### 新增尾段

Em resumo, se gostas de jogos com companheiros de ecrã, como Bongo Cat, mas preferes cães, Lucky Dog Rise pode ser para ti. Aquele focinho de Shiba Inu faz-te lembrar o Doge? Desta vez, o sorriso sai do meme e fica contigo: um cãozinho virtual junto à barra de tarefas, a fazer-te companhia enquanto escreves, trabalhas ou jogas. Um patudo com expressões divertidas, chapéus e roupas para colecionar, que vai ganhando um estilo só dele. Talvez ainda haja um cantinho no teu ecrã para este amigo de quatro patas, mesmo que já tenhas outros companheiros por perto.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样有屏幕伙伴的游戏，却更偏爱狗，Lucky Dog Rise 或许适合你。这张柴犬脸，会让你想起 Doge 吗？这一次，笑意从梗图走出来，留在你身边：任务栏旁的一只虚拟小狗，陪你打字、工作或游戏。这个 patudo 有有趣的表情，还有可收集的帽子和衣服，渐渐拥有自己的风格。即使你身边已经有其他伙伴，也许屏幕上仍能给这位四脚朋友留个小角落。

## 葡萄牙语：巴西

### 采用词与覆盖

| 关键词／中文释义 | 使用依据与可能搜索的原因 | 文案实际写法 |
| --- | --- | --- |
| doguinho，巴西网络／亲昵语境的狗狗称呼 | [累西腓市 Adota Pet 平台的狗狗介绍](https://adotapet.recife.pe.gov.br/descricao-pet/1)实际使用，确认其狗称呼语境，不靠词典猜测。 | Um doguinho ao lado da barra de tarefas |
| vira-lata caramelo，焦糖色米克斯犬的巴西文化称呼 | [Gshow 的当地狗梗回顾](https://gshow.globo.com/cultura-pop/viralizou/noticia/memes-de-cachorro-caramelo-relembre-os-vira-latas-que-viralizaram.ghtml)及[Petlove 的用法介绍](https://www.petlove.com.br/dicas/cachorro-vira-lata-caramelo)核实名称和文化关联；不是本作犬种，也不是一种纯种犬。 | Já se derreteu por um vira-lata caramelo… Talvez este Shiba Inu virtual também…；连接爱狗的情感，不把游戏柴犬说成米克斯。 |
| Doge，网络狗梗名；Shiba Inu，柴犬 | [CNN Brasil 对 Kabosu 与 Doge 的报道](https://www.cnnbrasil.com.br/economia/money/mercado/morre-kabosu-cachorra-que-inspirou-o-dogecoin/)确认当地名称及网络梗关联；犬种同时与原正文及前述文化资料核对。 | memes do Doge、este Shiba Inu virtual |
| cachorro virtual，虚拟狗；companheiros de desktop，桌面伙伴 | 根据巴西用词与实际产品形式推断的搜索表达，未验证搜索量。 | este cachorro virtual、companheiros de desktop |

Cheems 在其他地区版采用，本批巴西版未继续叠加：当前已用 Doge 建立柴犬梗关联、用 caramelo 建立巴西狗文化入口；额外列名会削弱段落节奏。没有把 caramelo 当作游戏毛色或可换犬种功能，也没有生硬写缩放来凑大狗关键词。

### 新增尾段

Resumindo: se você gosta de companheiros de desktop como Bongo Cat, mas prefere cachorros, Lucky Dog Rise pode ser a sua cara. Já se derreteu por um vira-lata caramelo ou deu risada com os memes do Doge? Talvez este Shiba Inu virtual também encontre um cantinho no seu coração. Um doguinho ao lado da barra de tarefas, com carinhas engraçadas, chapéus e roupas para colecionar, fazendo companhia enquanto você digita, trabalha ou joga. Mesmo que ainda não dê para ter um cachorro em casa, este cachorro virtual só precisa de um pedacinho da sua tela para ficar por perto.

### 中文回译

总的来说，如果你喜欢 Bongo Cat 这样的桌面伙伴，却更偏爱狗，Lucky Dog Rise 或许正合你的口味。你曾被一只焦糖色米克斯犬萌到，或被 Doge 的梗图逗笑过吗？也许这只虚拟柴犬，也能在你心里找到一个小角落。任务栏旁的一只 doguinho，带着有趣的表情，以及可收集的帽子和衣服，陪你打字、工作或游戏。即使暂时还不能在家养狗，这只虚拟狗也只需要屏幕上的一小块地方，就能待在你身边。

## 当前两页完整累积导入稿

| 页面 | 完整文件 | itemid |
| --- | --- | --- |
| Demo | [storepage_1315198_existing-03_review.json](../upload/storepage_1315198_existing-03_review.json) | 1315198 |
| 正式版 | [storepage_602645_existing-03_Main_review.json](../upload/storepage_602645_existing-03_Main_review.json) | 602645 |

分别以[Demo 第二批稿](../upload/storepage_1315198_existing-02_review.json)和[正式版第二批稿](../upload/storepage_602645_existing-02_Main_review.json)为底稿，保留第二批四个新尾段，累计加入本批四个新尾段。各包含完整 30 种语言，可直接导入对应页面，无需手动合并；两份身份不可混用。

相比各自第二批，仅 spanish、latam、portuguese、brazilian 的 about 字段追加内容，其余 26 种语言、全部 30 个简介和非文字字段保持原值。相比本轮起始快照，合计只改变 8 个 about 字段；所有原字段仍是完整前缀。两页 60 个长短描述字段仍一致。

## 校验及验收边界

- 核对四个新增尾段与所列文案一致，采用词在自然词形中全部出现；每段只有一对完整 p 标签，位于原正文媒体之后。
- 核对所有原文固定前缀、简介及两个正文媒体引用不变；繁中保留此前已确认的尾段。
- 核对本批 10 份本地复制稿与对应 JSON 一次解码结果逐字一致；五份旧 Markdown 归档前后字节相同。
- 核对全部原始下载及此前上传快照的字节未变；两页身份、语言集合、字段集合和其他值保留。
- 本批简介最多 296 字符，未为了压缩或统一地区用词而改写任何简介。
- 本轮原有 13 种语言均已有 BBCode 和独立纯文本简介。意大利语、俄语、泰语的历史 Markdown 格式收尾仍按计划另列，本批不扩展处理。
- 本地检查通过不等于新尾段已在 Steam 页面实际预览或母语审校。AI 没有执行 Steam 上传、保存、发布或 Git 提交。

归档旧稿：[繁中](../about/archive/关于此游戏-繁体中文.md)、[西班牙西语](<../about/archive/关于此游戏-Español (España).md>)、[拉美西语](<../about/archive/关于此游戏-Español (Latinoamérica).md>)、[葡萄牙葡语](<../about/archive/关于此游戏-Português (Portugal).md>)、[巴西葡语](<../about/archive/关于此游戏-Português (Brasil).md>)。
