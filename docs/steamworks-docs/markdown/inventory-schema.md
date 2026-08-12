---
source: "https://partner.steamgames.com/doc/features/inventory/schema"
retrieved_at: "2026-08-12T19:16:41.967Z"
language: zh-CN
---

# Steam 库存架构

> Steamworks 官方页面本地快照。原始页面正文见 ../raw/inventory-schema.html。

ItemDef 架构概览
核心架构和物品类型
这些是 Steam 能够理解和解释的核心属性，对于经济体、交易功能以及库存展示的正常运作是必需的。 这些属性中有许多与 ISteamEconomy/GetAssetClassInfo Web API 返回的相同。
ItemDef 的类型必须为下列之一:
名称	描述
item	一种可在玩家库存中找到的物品类型。
bundle	代表一组 ItemDef，每种类型均有相关数量。 当此物品被授予时，它会自动扩展到 bundle 属性中配置的一组物品中。
generator	代表一个随机物品。 授予此物品时，将从 bundle 属性中随机选择一个物品类型，并创建该类型的物品一件。 （例如：假设解锁一个箱子时，即会创建其中一项可能的物品）
playtimegenerator	这是一种特殊形式的 generator，可以通过应用程序调用 ISteamInventory::TriggerItemDrop 授予。
tag_generator	对物品实例应用标签的特殊物品定义（参见 Steam 库存物品标签，了解更多信息。）
有关复杂物品类型的指定方法，请参阅下文内容。
ItemDef 属性
名称	描述
appid	您的应用程序的 ID。
name	您的物品的英语名称。 参考以下示例，为您的物品名称提供本地化版本：

name_english: "Hat"
name_schinese: "帽子"

参见本地化和语言文档，了解可用作后缀的有效语言名称。
description	您物品的英语描述。
您可以参考以下示例，为您的物品描述提供本地化版本：

description_english: "This is a tall hat"
description_schinese: "这是一顶高帽子"

参见本地化和语言文档，了解用于后缀的有效语言名称。
display_type	物品“类型”的英语描述。
您可以参考以下示例，为您的物品类型提供本地化版本：

display_type_english: "Weapon"
display_type_schinese: "武器"

参见本地化和语言文档，了解用于后缀的有效语言名称。
itemdefid	此 itemdef 的 ID。 对于非创意工坊项目，此值必须小于 1,000,000，且不大于 2,147,483,647。
type	内部值 （'item' | 'bundle' | 'generator' | 'playtimegenerator' | 'tag_generator'）。
bundle	（参见下文的 Bundle 和 Generator 物品类型。）
promo	（参见下文的促销格式。）
drop_start_time	UTC 时间戳，用于阻止在此时间前授予促销品，仅在促销 = 手动时适用（参见下文的促销格式）。
exchange	（参见下文的 exchange 格式。）
price	（参见下文的价格格式。）
price_category	预设价格，我们会为您换算为不同的货币价值。 （参见下文的价格格式。）
background_color	在库存背景中显示的 6 位十六进制颜色。
name_color	在库存中显示名称的 6 位十六进制颜色。
icon_url	物品小图标的 URL。 由于 Steam 服务器将下载并缓存，此 URL 应能公开访问。 我们建议您将图片托管在您自己的公共网页服务器上，而不是使用共享图床服务，以确保图片持续可用。 建议大小为 200 x 200。
icon_url_large	物品大图片的 URL。 由于 Steam 服务器将下载并缓存，此 URL 应能公开访问。 我们建议您将图片托管在您自己的公共网页服务器上，以确保图像持续可用，而不是使用共享图床服务。 建议大小为 2048 x 2048。
marketable	为 false 或 true。 此物品是否可以在 Steam 社区市场上出售给其他用户。
tradable	为 false 或 true。 此物品是否可以利用 Steam 交易与其他用户进行交易。
tags	（参见Steam 库存物品标签。）
tag_generators	要应用的 tag_generator 物品定义 id 的列表（参见Steam 库存物品标签）。
tag_generator_name	标签类型令牌的名称（参见Steam 库存物品标签）。
tag_generator_values	标签值列表与标签选中的几率（参见 Steam 库存物品标签）。
container_contents_generator	“generator”类型物品的 itemdefid，代表该物品的内容，在物品描述中显示。 例如，如果此物品是一个箱子，则可将此字段设置为生成箱子所含物品的相应生成器。 这将在物品描述中显示所有可能掉落的物品，按掉落几率从高到低排列。 请参见 Bundle 和 Generator 物品类型部分。
store_tags	使用“;”字符分隔的“tags”字符串。 这些标签将用于在您应用的 Steam 物品商店中进行分类与筛选。
store_images	使用“;”字符分隔的图像 URL。 这些图片将通过代理处理，并在您应用的 Steam 物品商店详情页中使用。
game_only	为 false 或 true。 如为 true，则物品（包括新物品通知）不会出现在用户的 Steam 库存中。 常见用途是用于您授予的物品，这些物品会立即被消耗掉。
hidden	为 false 或 true。 如为 true，物品定义将不会在客户端上显示，也无法购买。 用于隐藏未使用或正在开发的 itemdef。
store_hidden	为 false 或 true。 如为 true，此物品将在您应用的 Steam 物品商店中隐藏。 默认情况下，任何标价物品都将显示在商店中。
use_drop_limit	为 false 或 true。 如为 true，我们将依据 drop_limit 限制通过 ISteamInventory::TriggerItemDrop 进行的物品授予。 （参见下文的游戏时间物品掉落。）
drop_limit	整数。 限制此物品通过 ISteamInventory::TriggerItemDrop 对特定用户的掉落次数。 设置为零，即会阻止此物品在未来进行任何掉落。 （参见下文的游戏时间物品掉落。）
drop_interval	整数。 用户可获得物品时需要达到的游戏时间分钟数。 （参见下文的游戏时间物品掉落。）
use_drop_window	为 false 或 true。 如为 true，我们将为此 itemdef 使用“drop_window”。 （参见下文的游戏时间物品掉落。）
drop_window	整数。 在授予物品之前的冷却时间窗口，以分钟为单位计算的已过时间。 （参见下文的游戏时间物品掉落。）
drop_max_per_window	整数。 在冷却生效前的时间窗口内可授予的次数。 默认值为 1。 每个窗口最多 10 个。 （参见下文的游戏时间物品掉落。）
granted_manually	为 false 或 true。 如为 true，仅在使用显式物品定义 id 调用 AddPromoItem() 或 AddPromoItems() 时才被授予。 否则，也可以通过调用 GrantPromoItems() 授予。 默认为 false。
use_bundle_price	为 false 或 true。 参见下文的出售捆绑包物品。 默认为 false。
auto_stack	为 false 或 true。 如果为 true，则物品授予将自动添加到给定类型的单个堆栈中。 数量变化时，授予将在库存回调中可见。 默认为 false。
purchase_limit	整数。 如果用户已经拥有 N 件相同物品，则会阻止用户在物品商店中再次购买该物品。 不阻止用户购买消耗品/可交易/可销售物品。 此标签仅对 ItemDef 类型“item”起作用。
扩展架构
您也可以专门根据自己游戏的需求定义额外的属性。
使用复杂的物品定义
Bundle 和 Generator 物品类型
Bundle、generator 和 playtimegenerator 规则在 bundle 字段定义。

对于 bundle，我们描述包含项目的类型和数量。

对于 generator 或 playtimegenerator，我们描述可能生成的项目类型，以及每种类型的相对权重。 请注意，权重不必非要加起来等于 100，虽然这样做可能更方便。

bundle 字段表示为一系列由“;”分隔的物品配方。 每个配方包含一个 itemdef ID，可选择在后面加上“x”分隔符及所需（整数）数量。

如果没有指明的数量，则使用“1”作为默认值。
Bundle 格式
bundle_def : item_recipe , { ";" , item_recipe }
item_recipe : item_def , [ "x" , quantity ]

Bundle 示例
授予一个玩家 itemdef 201、itemdef 202、itemdef 203 各一件：

type: bundle
bundle: 201;202;203
授予 itemdef 101 的物品 1 件，itemdef 102 的物品 5 件：

type: bundle
bundle: 101x1;102x5
90% 概率生成 itemdef 501，9% 概率生成 itemdef 502，1% 概率生成 itemdef 503：

type: generator
bundle: 501x90;502x9;503x1
90% 概率获得普通物品；10% 概率获得特殊物品。

itemdefid: 600
name: Common generator
type: generator
bundle: 601;602;603;604;605

itemdefid: 700
name: Special generator
type: generator
bundle: 701;702;703;704;705

itemdefid: 800
name: Master generator
type: generator
bundle: 600x9;700x1

注意：如上例所示，bundle 和 generator 的定义是可以串联使用的。 在授予物品时，任何复杂物品类型都会被反复展开，直到只剩下基本的 itemdef 为止。
在物品描述中显示内容

虽然生成器无法直接出现在玩家的库存中，但可以将部分物品与其链接。 举个例子，玩家可能会有一个箱子，打开后会使用上例所述的“Master generator”生成一个物品。 这种关系可以用 container_contents_generator 字段表示。

itemdefid: 801
name: Everything Crate
type: item
container_contents_generator: 800

当玩家在 Steam 社区查看物品时，物品描述会包括由 generator 800（上文中描述的“Master generator”）生成的物品列表，按稀有度升序排列。 会最先列出 5 个普通物品，毕竟它们最容易掉落，接着是 5 个特殊物品。
Exchange 公式
ExchangeItems API 允许您定义物品的合成/转化配方，这些配方可以从客户端安全地调用。 Steam 服务器将检查玩家库存，并在满足条件的情况下原子性地消耗指定材料并授予目标物品。

目标物品可以是 bundle 或 generator 类型。

该公式在目标物品的 exchange 字段中提供。 公式被指定为一个配方集合，其中包含一个或多个由分号分隔的配方。

每个配方都是一组必需的 material 物品，以逗号分隔。

可通过 itemdefid 或标签明确提供所需材料。 如果未给出数量，则假定为 1。

当使用 ExchangeItems 时，调用方需提供一组材料物品，用于兑换目标物品。 服务器将检查每个配方，并选择第一个能通过所给材料实现的配方。

Exchange 具有高度灵活性——无论是使用钥匙开启箱子、用零件合成特殊物品、物品回收，还是物品升级，都可以通过这些公式实现。
Exchange 格式
<exchange>: <recipe> { ";" <recipe> }
<recipe>: <material> { "," <material> }
<material>: <item_def_descriptor> / <item_tag_descriptor>
<item_def_descriptor>: <itemdefid> [ "x" <quantity> ]
<item_tag_descriptor>: <tag_name> ":" <tag_value> [ "*" <quantity> ]

如果未明确给出，则所需数量为 1。
Exchange 示例
// 须满足以下条件之一：
// - 物品 #100 1 件和物品 #101一件；或
// - 物品 #102 5 件；或
// - 物品 #103 3 件和物品 #104 3 件。
"exchange":"100,101;102x5;103x3,104x3"

// 需要左手套一只和右手套一只：
"exchange":"handed:left,handed:right"

// 需要三棵树再加上某件华丽物品：
"exchange":"type:tree*3,quality:fancy"

// 须满足以下条件之一：
// - 物品 #201 和物品 #202；或
// - 一件香蕉味的物品和一件重物。
"exchange":"201x1,202x1;flavor:banana,mass:heavy"

// 回收 5 件“普通”物品，合成为一件“特别”物品。
{
  "name":"special_generator",
  "type":"generator",
  "tags":"rarity:special",
  "bundle":....,
  "exchange":"rarity:common*5",
  ...
}

促销物品
促销物品可以根据以下几种条件授予玩家：

具备 Appid（包括 DLC appid）的所有权。

解锁某项成就。

在某 appid 中达到一定的游玩时长。

手动授予 – 客户端需使用特定 item def ID 调用 AddPromoItem。

由 Steam 完成对促销物品的检查，因此可安全地从客户端请求这些物品。参见 ISteamInventory::AddPromoItem 和 ISteamInventory::GrantPromoItems。 要定义一项促销物品，请在物品定义的“promo”属性中设置一个或多个授予规则。

请注意促销物品也可能是一个 bundle。

手动授予的促销物品也可存在掉落时间间隔。 举例来说，您可以根据玩家完成某些任务（例如游戏内任务）的情况，每周授予一些物品。 要使用此功能，请在 itemdef 中设置 drop_start_time 和 drop_interval 的值。

促销物品不会因临时拥有免费游戏（如免费周末、通过家庭共享访问等）而授予。
促销规则格式
<promo>: <rule> { ";" <rule> }
<rule>: app_rule / ach_rule / played_rule / manual_rule
<app_rule>: "owns:" <appid>
<ach_rule>: "ach:" <achievement name>
<played_rule>: "played:" <appid>/<minutes played, defaults to 1>
<manual>: "manual"

促销示例
// 简单的促销规则定义：
"promo":"owns:440;owns:480"
// 在 appid 为 570 的游戏中至少玩了 15 分钟：
"promo":"played:570/15"

// 可以每周授予一次的可消耗物品：
	"itemdefid": 404,
	"type": "item",
	"name": "Weekly Quest Item",
	"promo": "manual",
	"drop_start_time": "20170801T120000Z",
	"drop_interval": 10080,
...

掉落开始时间
通过设置促销物品的掉落开始时间，将会防止物品在此时间之前被授予给玩家。 这允许您在该物品开始掉落之前部署授予代码。 需要用 UTC 时区以 ISO 8601 格式指定时间戳：YYYYMMDDTHHMMSSZ。 例如：20050515T171151Z。
游戏时间物品掉落
游戏时间物品掉落功能使 Steam 服务器能够根据用户的游戏时间来跟踪和管理物品掉落。 当您认为应授予物品时，您的游戏只需调用 ISteamInventory::TriggerItemDrop 函数即可。 您需要创建“playtimegenerator“类型的物品来实现此类掉落。
基于游戏时间的物品授予由您的应用程序控制。 我们不支持完全基于用户游戏时间的自动授予。 换言之，游戏时间是进行授予的条件，但应该由您的游戏来启动是否授予物品的评估流程。

物品掉落频率可通过应用程序得到控制，可在“社区”>“库存服务”>“游戏时间物品授予”栏目中操作。 有三类控制，分别允许进行以下三种自定义：
（1）要等多久才进行一次物品掉落？
(2) 一个时间窗口里有多少物品掉落？ 以及，
(3) 在授予下一个物品掉落之前，冷却时间窗口是多久？

每个 itemdef 存在同样的控制功能。 为物品设置的值将覆盖应用程序中该特定物品的设置。 这允许每个物品有自己的掉落频率、每窗口最大掉落量和冷却时间窗口。

如果指定了任何掉落设置（“drop_interval”、“use_drop_window”、“drop_window”、“drop_max_per_window”），每个 playtime generator 授予都会被单独追踪。 换言之，如果 itemdef 没有任何掉落设置，那么将与其他所有无掉落设置的 playtime generators 一起共享掉落，并受到应用程序中掉落间隔的限制。 否则，如果 itemdef 确实明确指定了任何掉落设置，那么其掉落物品则在 generator 级别上被单独授予或追踪，独立于应用程序或其他 itemdef playtime generators 进行。
游戏时间 ItemDef 授予示例
允许用户在游戏 30 分钟后，获得一个物品。 这种设定的一个缺点是，由于每 30 分钟就可以获取一次物品，因此也允许了刷物品的行为存在。 我们强烈建议添加一个掉落窗口设置：
"drop_interval" : 30

将频率限制为 1 天 1 次，每天在游玩 30 分钟后即可获取物品掉落。 这样，当玩家每天回来玩您的游戏时都会得到奖励。
"drop_interval" : 30,
"use_drop_window" : "true",
"drop_window" : "1440"

允许用户每天获得 3 件物品，游玩时间至少为 90 分钟。 游玩时间不必是连续不断的。 此方式鼓励玩家长时间进行游戏。
"drop_interval" : 30,
"use_drop_window" : "true",
"drop_window" : "1440",
"drop_max_per_window" : "3"
如果一个特定的 itemdef 设置缺失，则默认为应用程序中规定的设置。
掉落限制
drop_limit 变量允许某个 generator 类的物品设定基于游戏时间的最大授予次数。
您可以用它来限制一个物品的生成次数（例如：只有玩家第一次以大神级难度通关时才授予）。
或者，如果将值设为 0，则可用来防止在未来掉落已弃用的物品。 只有当 use_drop_limit 为“true”时，我们才承认此设置。
出售物品
为了使某些游戏物品可以出售，只需在相应的 itemdef 中定义价格或价格类别。
参见 Steam 物品商店文档，了解更多关于启用和自定义您的商店页面的信息。
指定价格
物品价格可以通过以下两个字段之一进行定义，但不能同时使用两者。

名称	描述
price	为各币种定义一个具体价格。 任何未定义的币种将在购买时自动转换。
price_category	定义一个价格，系统将根据 Valve 维护的定价表，自动转换并显示为各支持货币的价格。

price_category 字段以特殊的“VLV”货币指定。 VLV100 相当于 0.99 美元，并通过 Valve 的兑换率转换为所有受支持的货币。

VLV 兑换率经过精心管理，以适应随时变化的汇率浮动。 我们将根据需要更新价格，同时允许小幅波动，以便为顾客提供稳定的定价和良好的用户体验。
价格格式
Price: <version>;<pricelist>

Version: "1"
<pricelist> : <originalprice>(;<price>)*

<originalprice>: <currency><integer>(,<currency><integer)*
<price>: (<daterange>)<currency><integer>(,<currency><integer)*

<currency> 3 个字母，如 "USD"
<integer> 以特定货币单位表示的金额
<daterange>: YYYYMMDDTHHMMSSZ-YYYYMMDDTHHMMSSZ

<daterange> 必须有且仅有 33 个字符。

Daterange 列表必须以降序排列（未来日期排在前面）。
价格示例
price_category: 1;VLV100
预设价格类别，使用 Valve 维护的定价表。

price: 1;USD100
（1美元）。

price: 1;USD100,EUR080
（1 美元或 0.8 欧元）。

price: 1;USD100,EUR080;20130607T080000Z-20130606T080000ZUSD50,EUR40
（1 美元或 0.8 欧元，但自 2013 年 6 月 6 日起价格降为 0.5 美元/0.4 欧元）。

price: 1;USD100,EUR080;20130609T080000Z-20130606T080000ZUSD50,EUR40
（1 美元或 0.8 欧元，但在 2013 年 6 月 6 日至 2013 年 6 月 9 日期间，价格会降为 0.5 美元/0.4 欧元）。
出售 bundle （捆绑包）

捆绑的物品可在物品商店中上架销售。 捆绑包将在付款流程中展开，以便用户查看授予物品的列表。 Steam 退款政策允许在一定期限内退款，但是包内所有物品均需位于玩家的库存中，且未被修改。
捆绑包价格
为捆绑包定价时，需要考虑几个额外的步骤。 Steam 使用包内各个物品的单价来决定捆绑包价格，并相应地对捆绑包的销售收入进行分配。 此分配将决定对创意工坊的贡献者发放的金额。 一个捆绑包可以轻松组合来自不同创意工坊作者的内容，或将创意工坊内容与您自己的第一方内容组合在一起。

捆绑物品的定价规则如下：

为捆绑包中的每项物品指定价格信息。

如果包内有任何物品不能单独出售，请将这些物品的 store_hidden 设置为“true”。

将捆绑包的 price 或 price_category 字段设置为一个简单价格（例如 VLV0）。
此价格不会在商店中使用，但是有必要提示物品商店这是一个可销售物品。

可选择将 purchase_bundle_discount 设置为捆绑包的折扣百分比。

如果您愿意，也可以为各币种指定明确的捆绑包价格。 捆绑包价格替代将忽略您指定的任何 purchase_bundle_discount。 不过，各个物品的价格仍是按比例分配捆绑包收入的基础，因此必须提供。

替代捆绑包自动定价的步骤如下：

在捆绑包的 price 或 price_category 字段指定您希望的价格。

将捆绑包物品的 use_bundle_price 设置为 true。
出售 Generator 物品
不要试图在物品商店中直接出售 generator 类型的物品。

要出售有随机组成部分的物品（如箱子），您应将该箱子物品定义为简单的 item 类型。 然后创建一个 generator 类型的物品，该物品会接受箱子，将其作为 exchange 配方的输入项。

购买后，玩家可以选择“打开”箱子，这时您可以调用 ISteamInventory::ExchangeItems 执行该 generator。 箱子一旦打开，便不可再退款。
VLV 预设定价表
以下是当前可用于为您的物品指定 price_category 的可选值列表。

名称	美元价格
VLV25	$0.25 USD
VLV50	$0.49 USD
VLV75	$0.75 USD
VLV100	$0.99 USD
VLV150	$1.49 USD
VLV200	$1.99 USD
VLV250	$2.49 USD
VLV300	$2.99 USD
VLV350	$3.49 USD
VLV400	$3.99 USD
VLV450	$4.49 USD
VLV500	$4.99 USD
VLV550	$5.49 USD
VLV600	$5.99 USD
VLV650	$6.49 USD
VLV700	$6.99 USD
VLV750	$7.49 USD
VLV800	$7.99 USD
VLV850	$8.49 USD
VLV900	$8.99 USD
VLV950	$9.49 USD
VLV1000	$9.99 USD
VLV1100	$10.99 USD
VLV1200	$11.99 USD
VLV1300	$12.99 USD
VLV1400	$13.99 USD
VLV1500	$14.99 USD
VLV1600	$15.99 USD
VLV1700	$16.99 USD
VLV1800	$17.99 USD
VLV1900	$18.99 USD
VLV2000	$19.99 USD
VLV2500	$24.99 USD
VLV3000	$29.99 USD
VLV3500	$34.99 USD
VLV4000	$39.99 USD
VLV4500	$44.99 USD
VLV5000	$49.99 USD
VLV6000	$59.99 USD
VLV7000	$69.99 USD
VLV8000	$79.99 USD
VLV9000	$89.99 USD
VLV10000	$99.99 USD
ItemDef 架构示例：
{
	"appid": 480,
	"items": [
	{
		"itemdefid": 10,
		"type": "playtimegenerator",
		"bundle": "100x100;101x50;102x25;103x2;110x20;111x20;120x5;121x3",
		"name": "Drop Generator",
		"name_color":  "7D6D00",
		"background_color":  "3C352E",
		"icon_url": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"icon_url_large": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"tradable": false,
		"marketable": false
	},
	{
		"itemdefid": 100,
		"type":  "item",
		"name": "Hat decoration",
		"description": "Hat decoration description",
		"price": "1;USD99",
		"name_color":  "7D6D00",
		"background_color":  "3C352E",
		"icon_url": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"icon_url_large": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"tradable": true,
		"marketable": true
	},
	{
		"itemdefid": 200,
		"type":  "item",
		"price": "1;VLV100",
		"name_english": "Red Hat",
		"name_german":  "Roter Hut",
		"description_english": "Red Hat",
		"description_german": "Roter Hut",
		"store_tags": "hat;featured",
		"icon_url": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"icon_url_large": "http://cdn.beta.steampowered.com/apps/440/icons/c_fireaxe_pyro_xmas_large.fa878752e1aa09a721a03042a234063b6c929278.png",
		"tradable": true,
		"marketable": true
	}
	]
}
