---
last_editor: Codex
last_edit: 2026-06-22
status: draft
---

# Steam Inventory 阅读笔记

## 说明

本文不是 Steamworks 官方文档的全文复制，而是项目内部阅读索引。

Steamworks 官方文档内容可能更新，后续做正式接入前应重新打开原文核对。

## 相关官方页面

### Steam Inventory Service

官方链接：

https://partner.steamgames.com/doc/features/inventory

本页用于理解 Steam Inventory Service 的整体定位。

重点结论：

1. Steam Inventory Service 可以托管玩家持久库存。
2. 它支持无自建服务器的 server-less 模式。
3. 在 server-less 模式下，客户端不能被信任，不能随意给玩家指定物品。
4. 如果有可信服务器，服务器可以用更高权限授予明确物品。
5. 无服务器方案更适合使用 Steam 配置好的掉落、交换、购买、促销等规则。

关键原文短摘：

> persistent player inventories without having to run special servers

> the client can't be trusted

> you can't give users specific items in this scheme

对本项目的含义：

如果 Lucky Dog Rise 不自建服务器，正式库存应尽量托管在 Steam Inventory 中。本地存档可以保存 UI 状态、装备选择、开盒演出进度，但不应作为正式道具库存的唯一来源。

### ISteamInventory Interface

官方链接：

https://partner.steamgames.com/doc/api/ISteamInventory

本页用于查 Steam 客户端侧库存 API。

重点函数：

1. `GetAllItems`
2. `ExchangeItems`
3. `TriggerItemDrop`
4. `AddPromoItem`
5. `AddPromoItems`
6. `GrantPromoItems`
7. `RequestPrices`
8. `StartPurchase`
9. `SerializeResult`
10. `GenerateItems`

重点结论：

1. `GetAllItems` 用于拉取玩家当前 Steam 库存。
2. `ExchangeItems` 可让客户端发起安全交换，由 Steam 服务器校验材料并原子性消耗/发放。
3. `StartPurchase` 是 Steam 钱包/本地货币购买流程，不等同于游戏内筹码购买。
4. `GenerateItems` 是开发测试用途，不适合作为正式发奖方案。
5. `AddPromoItem` 等促销发奖要求 itemdef 中配置 `promo` 条件。
6. `SerializeResult` 可以把库存结果序列化，并带有签名，用于防止玩家谎称拥有稀有物品。

关键原文短摘：

> atomically consume the given materials and grant a target item

> only intended for prototyping

> user will be prompted in the Steam Overlay

> result sets contain a short signature

对本项目的含义：

普通盲盒如果走 Steam，应优先考虑“消耗箱子/开箱券，交换到 generator 结果”的模型。客户端负责播放动画和展示结果，但正式物品发放由 Steam 完成。

### Steam Inventory Schema

官方链接：

https://partner.steamgames.com/doc/features/inventory/schema

本页用于理解 Steam ItemDef 的类型、属性和奖池表达方式。

重点 ItemDef 类型：

1. `item`：普通物品，会出现在玩家库存中。
2. `bundle`：固定礼包，授予时展开为一组物品。
3. `generator`：随机物品，授予时从 `bundle` 配置中随机选择结果。
4. `playtimegenerator`：可由 `TriggerItemDrop` 调用的特殊 generator。
5. `tag_generator`：给物品实例应用 tag。

重点字段：

1. `itemdefid`
2. `type`
3. `bundle`
4. `exchange`
5. `promo`
6. `price`
7. `container_contents_generator`
8. `tags`
9. `icon_url`
10. `icon_url_large`

重点结论：

1. `generator` 可以表达固定权重随机。
2. `generator` 可以链式嵌套，用于实现“先抽品质，再抽品质内物品”的结构。
3. `container_contents_generator` 可以把箱子和 generator 关联起来，让 Steam 社区库存显示潜在掉落内容。
4. `exchange` 可以表达开箱、合成、回收、升级等消耗材料换结果的公式。
5. `promo` 可以表达拥有某个 App、成就、游玩时间或手动领取等促销发奖规则。

关键原文短摘：

> generator represents a random item

> bundle and generator definitions can be chained

> player might have a crate

> Promotional items can be granted

对本项目的含义：

普通盲盒可以在 Steam 中建成：

1. 一个可持有的箱子或开箱券 `item`。
2. 一个或多个品质 `generator`。
3. 一个主 `generator`，用于按品质概率随机到品质 generator。
4. 一个 `exchange` 公式，用于消耗箱子或券，并生成主 generator 的结果。

活动盲盒可以建成：

1. `promo` item。
2. `bundle`。
3. 固定品质 `generator`。
4. 或通过活动入口发放一次性箱子/券，再走 `ExchangeItems`。

## 对 Lucky Dog Rise 的设计影响

### 需要推翻的本地设计

以下内容如果目标是上 Steam 且不自建服务器，应避免作为正式方案：

1. 本地程序直接决定最终奖品。
2. 本地 JSON 存档作为正式库存。
3. 本地筹码可无限购买正式 Steam 库存盲盒。
4. 客户端调用发放指定道具作为正式奖励。

### 可以保留的本地设计

以下内容仍可保留：

1. 开盒动画和盲盒升级表演。
2. 中断保护和“不穿帮”规则。
3. 装备状态。
4. New 标记。
5. 本地 Item 表作为资源显示、UI 排序、装备类型、皮肤资源映射。
6. 调试模式本地背包。

### 需要新增的兼容概念

Item 表建议补充 Steam 对齐字段：

1. `SteamItemDefId`
2. `SteamItemType`
3. `SteamTradable`
4. `SteamMarketable`

BlindBox 表建议补充 Steam 对齐字段：

1. `SteamContainerItemDefId`
2. `SteamGeneratorItemDefId`
3. `SteamExchangeTargetItemDefId`
4. `SteamOpenCostItemDefId`

具体字段名称后续应按 Luban 表命名风格再定。

## 当前未确认问题

### 游戏内筹码是否能安全驱动 Steam 盲盒

如果筹码只存在本地存档中，玩家可以修改筹码数量，再用筹码购买能兑换 Steam 道具的箱子或开箱券。

Steam 可以保护最终发放的物品真实性，但无法保护本地筹码来源。

需要重新确认：

1. 筹码是否只用于本地游戏循环。
2. Steam 正式库存盲盒是否改为由 Steam 掉落、活动、购买、成就等方式获得。
3. 是否接受单机游戏中玩家修改本地筹码的风险。

### 唯一道具不膨胀规则是否保留

Steam `generator` 是固定权重随机。当前未看到官方文档支持“根据玩家已拥有唯一物品动态二次随机，把权重转给同品质可重复物品”的配置能力。

如果坚持该规则，可能需要：

1. 自建可信服务器。
2. 放弃 Steam server-less generator。
3. 或把该规则降级为本地调试/非正式库存规则。

### 开盒演出与 Steam 结果的关系

Steam 开箱会先产生结果。游戏客户端需要在本地保存“待揭晓记录”，并在演出完成前避免在游戏背包 UI 中暴露最终结果。

Steam 社区库存可能已经能看到新物品，这一点是否会破坏“不穿帮”，需要后续实测 Steam Inventory 的结果同步和社区库存刷新行为。
