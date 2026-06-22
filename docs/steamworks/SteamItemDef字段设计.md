---
last_editor: Codex
last_edit: 2026-06-22
status: draft
---

# Steam ItemDef 字段设计

## 设计目标

本文记录 Lucky Dog Rise 为导出 Steam Inventory ItemDef 需要准备的字段。

本文只讨论 Steam ItemDef 配置本身，不讨论盲盒开盒流程、CD、筹码、Refreshment Buff 等游戏逻辑。

项目内的 Excel / Luban 表应继续使用游戏语义。Steam ItemDef JSON 应由导出工具根据项目表生成，不建议把所有 Excel 表头直接改成 Steam schema 字段。

## 基本原则

Item 表负责描述游戏内真实物品，例如小狗、头饰、眼镜、桌布、背景、卡背、Refreshment 等。

Steam ItemDef 是 Steam Inventory Service 使用的平台配置。它用于告诉 Steam 某个物品的定义、类型、是否可交易、是否显示在 Steam 库存、是否堆叠等。

两者的关系是：

1. Item 表是策划主数据。
2. Steam ItemDef 是平台导出数据。
3. Item 表只补充必要的 Steam 映射字段。
4. generator、bundle、exchange、promo 等复杂规则不应强行塞进普通 Item 行。

## Item 表建议新增字段

以下字段适合直接添加到 Item 表，因为它们描述的是单个游戏物品与 Steam ItemDef 的一对一关系。

### SteamItemDefId

类型建议：`int`

默认值：`0`

说明：

Steam `itemdefid`。用于把本地 Item 行映射到 Steam Inventory 的物品定义。

`0` 表示该物品暂不导出到 Steam。

非 Workshop 物品的 Steam `itemdefid` 必须小于 1,000,000。实际 ID 分段规则后续需要单独约定。

### SteamItemDefType

类型建议：枚举

默认值：`Item`

说明：

对应 Steam ItemDef 的 `type`。

推荐枚举：

1. `None`：不导出到 Steam。
2. `Item`：普通库存物品。
3. `Bundle`：固定礼包。
4. `Generator`：随机生成器。
5. `PlaytimeGenerator`：可由游玩时间掉落触发的生成器。
6. `TagGenerator`：标签生成器。

普通装扮和 Refreshment 大多数情况下使用 `Item`。

盲盒 generator、bundle、playtime generator 通常不建议放在普通 Item 表里，后续可以通过盲盒表、发奖来源表或导出专用表生成。

### SteamGameOnly

类型建议：`bool`

默认值：`true`

说明：

对应 Steam `game_only`。

当该字段为 true 时，物品不会显示在玩家 Steam Backpack 中，也不会触发 Steam 新物品通知。

Lucky Dog Rise 的装扮道具默认不打算让玩家交易或在 Steam 库存界面查看，因此装扮道具建议默认 true。

### SteamTradable

类型建议：`bool`

默认值：`false`

说明：

对应 Steam `tradable`。

用于控制物品是否允许通过 Steam Trading 在玩家之间交易。

当前项目装扮道具和 Refreshment 默认不允许交易。

### SteamMarketable

类型建议：`bool`

默认值：`false`

说明：

对应 Steam `marketable`。

用于控制物品是否允许在 Steam Community Market 上出售。

当前项目装扮道具和 Refreshment 默认不允许上市场。

### SteamAutoStack

类型建议：`bool`

默认值：`false`

说明：

对应 Steam `auto_stack`。

当该字段为 true 时，同一类型的物品授予会自动合并到单个堆叠中。

建议：

1. 装扮道具默认 false。
2. Refreshment、领取券、消耗品、进度类令牌可以根据需要设为 true。

### SteamHidden

类型建议：`bool`

默认值：`false`

说明：

对应 Steam `hidden`。

该字段用于隐藏未使用或开发中的 ItemDef。正式可用物品不应依赖该字段隐藏。

不要把 `hidden` 和 `game_only` 混用：

1. `game_only`：物品可被游戏使用，但不显示在 Steam Backpack。
2. `hidden`：物品定义不显示给客户端或不可购买，更适合未启用、废弃、开发中的定义。

正式装扮道具默认 `SteamHidden = false`。

### SteamDisplayType

类型建议：`string`

默认值：空

说明：

对应 Steam `display_type`。

用于在 Steam 库存界面显示物品类型文字，例如 Hat、Card Back、Refreshment。

如果物品设置为 `SteamGameOnly = true`，该字段可以先留空。若后续某类物品需要在 Steam 库存或商店展示，再补充该字段。

### SteamTags

类型建议：`string`

默认值：空

说明：

对应 Steam `tags`。

用于导出 Steam 物品标签。标签可用于分类、交换公式、筛选等。

示例语义：

1. `rarity:rare`
2. `slot:headwear`
3. `type:refreshment`

具体标签格式后续需要与导出工具约定。

### SteamIconUrl

类型建议：`string`

默认值：空

说明：

对应 Steam `icon_url`。

Steam 文档要求该 URL 可以被公网访问，因为 Steam 服务器会下载并缓存图标。推荐尺寸为 200x200。

如果物品设置为 `SteamGameOnly = true`，该字段可以暂时不作为必填字段。

若后续某个物品需要显示在 Steam 库存、Steam Item Store 或 Steam 社区界面中，则必须准备公网可访问图标 URL。

### SteamIconUrlLarge

类型建议：`string`

默认值：空

说明：

对应 Steam `icon_url_large`。

Steam 文档建议大图尺寸为 2048x2048，并要求 URL 可以被公网访问。

与 `SteamIconUrl` 一样，隐藏在游戏内使用的物品可以暂时留空；需要对 Steam 玩家可见时再补充。

## 可复用现有字段

以下 Steam 字段可以由现有 Item 表字段导出，不一定需要新增字段。

### name

Steam 字段：`name` 或 `name_english`

可由 Item 表现有名称字段导出。

如果项目已有多语言表，后续可以导出 `name_schinese`、`name_english` 等本地化字段。

### description

Steam 字段：`description` 或 `description_english`

如果 Item 表已有描述字段，可以直接导出。

如果当前没有描述字段，而所有物品都设置为 `SteamGameOnly = true`，第一阶段可以暂不强制补。

### background_color / name_color

Steam 字段：`background_color`、`name_color`

这两个字段可以由物品品质 UI 或稀有度配置表导出，不一定放在 Item 表里。

例如不同品质统一映射不同颜色。

## 不建议放入 Item 表的字段

以下 Steam 字段属于盲盒、发奖、交换、促销或掉落规则，不建议放进普通 Item 表。

### bundle

用于 `bundle`、`generator`、`playtimegenerator`。

它描述固定礼包内容或随机候选池。

该字段应由奖池表、盲盒表和权重字段生成，不建议策划手写字符串。

### exchange

用于 Steam `ExchangeItems`。

它描述开箱、合成、回收、升级等交换公式。

该字段属于盲盒或交换规则，应由发奖来源表、盲盒表或导出工具生成。

### promo

用于促销发奖。

它描述拥有 App、成就、游玩时间或手动领取等发奖条件。

该字段属于活动或发奖来源，不属于普通物品自身属性。

### drop_interval / drop_window / drop_max_per_window

用于 playtime 掉落或周期发奖。

这些字段属于掉落规则或领取券规则，不属于普通装扮道具。

### container_contents_generator

用于箱子显示潜在掉落内容。

该字段属于箱子 ItemDef，不属于普通装扮道具。

如果装扮盲盒箱子需要在 Steam 库存中展示潜在内容，可以由盲盒表导出。

### price / price_category

用于 Steam Item Store 售卖。

当前设计中，装扮盲盒不优先走 Steam Item Store 直接售卖，因此该字段暂不放入普通 Item 表。

## 推荐默认值

普通装扮道具建议默认：

1. `SteamItemDefType = Item`
2. `SteamGameOnly = true`
3. `SteamTradable = false`
4. `SteamMarketable = false`
5. `SteamAutoStack = false`
6. `SteamHidden = false`
7. `SteamIconUrl = 空`
8. `SteamIconUrlLarge = 空`

Refreshment 建议默认：

1. `SteamItemDefType = Item`
2. `SteamGameOnly = true`
3. `SteamTradable = false`
4. `SteamMarketable = false`
5. `SteamAutoStack = true`
6. `SteamHidden = false`

如果 Refreshment 最终不进入 Steam Inventory，而只使用本地存档，则 `SteamItemDefId` 可以保持 0。

## 后续导出工具要求

Steam ItemDef 导出工具应满足以下要求：

1. 从 Item 表读取 Steam 映射字段。
2. 跳过 `SteamItemDefId = 0` 或 `SteamItemDefType = None` 的物品。
3. 自动生成 Steam schema 需要的字段名。
4. 从物品名称和描述字段生成 Steam 显示文本。
5. 从品质配置生成颜色字段。
6. 从盲盒和奖池表生成 generator 的 `bundle`。
7. 从发奖来源和盲盒配置生成 `exchange`、`promo`、drop 相关字段。
8. 输出上传 Steamworks 后台所需的 ItemDef JSON。

导出工具应作为平台适配层存在。Steam schema 变化时，优先修改导出工具，而不是大规模修改策划表。
