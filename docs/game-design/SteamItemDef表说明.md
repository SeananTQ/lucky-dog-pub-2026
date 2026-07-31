---
last_editor: Codex
last_edit: 2026-07-31
status: draft
---

# SteamItemDef 表说明

## 功能定位

`SteamItemDef` 表是 Lucky Dog Rise 的 Steam Inventory 平台规则源。它主要描述没有对应本地 `Item` 行的回执、盲盒券、Bundle 和 Generator。

本表不是玩家实际库存，也不直接描述游戏本地背包。Steam 为每一位玩家实际生成的物品称为库存实例；本表中的每一行只是生成该类实例时使用的定义。

实际装扮、饮品和卡背等游戏物品仍由 `Item` 表统一管理。转换器合并 `SteamItemDef` 与 `Item` 的 Steam 映射后，生成最终可上传的 Steam schema。

## 与本地 Item 表的关系

`Item` 表描述游戏里的实际内容道具，并通过 `SteamItemDefId` 等字段生成对应的 Steam `item` 定义。实际道具不应在 `SteamItemDef` 表里再复制一行。

`SteamItemDef` 表管理 LinkTree 回执、盲盒券、Generator 和 PlaytimeGenerator 等平台规则定义。`BlindBox` 表通过 `SteamOpenCostItemDefId` 和 `SteamExchangeTargetItemDefId` 表达开箱关系，由转换器派生 Steam `exchange` 与 `container_contents_generator`；`BlindBoxSchedule` 通过 `SteamPlaytimeGeneratorItemDefId` 表达按游玩时间生成盲盒券的关系。

当 LinkTree 奖励已有道具时，`LinkTree.RewardItemId` 直接填写原有 `Item.Id`，不复制一行新的本地道具数据。`LinkTree.SteamPromoItemDefId` 则填写本表中对应的永久领奖回执 ID。

```mermaid
flowchart LR
    LinkTree[LinkTree 行] -->|RewardItemId| Item[Item: 实际奖励道具]
    LinkTree -->|SteamPromoItemDefId| Receipt[SteamItemDef: 永久领奖回执]
    Receipt -->|调用 AddPromoItem| SteamInventory[Steam Inventory]
    SteamInventory -->|结果包含回执实例| LocalInventory[确认首次领取并发放本地奖励]
    SteamInventory -->|结果不包含回执实例| RefuseReward[拒绝发放本地奖励]
```

## 当前 LinkTree 回执

当前已预留四条永久领奖回执：

1. `401001` / `LinkTreeTwitterFollowClaim`
   - 对应 `TwitterFollow`。

2. `401002` / `LinkTreeSteamCommunityClaim`
   - 对应 `SteamCommunity`。

3. `401003` / `LinkTreeSteamStoreClaim`
   - 对应 `SteamStore`。

4. `401004` / `LinkTreeXiaohongshuProfileClaim`
   - 对应 `XiaohongshuProfile`。

四条回执统一使用以下规则：

1. `Type=Item`。
2. `PromoRule=manual`。
3. `GrantedManually=TRUE`。
4. `Tradable=FALSE`、`Marketable=FALSE`。
5. `GameOnly=TRUE`、`StoreHidden=TRUE`。
6. `AutoStack=FALSE`。

客户端在玩家完成 LinkTree 的打开流程后调用 `AddPromoItem`。Steam 只会为符合规则且尚未领取的玩家创建一次回执实例；该实例不销毁，用于后续判断该奖励是否已经领取。

## 回执生命周期与测试限制

`AddPromoItem` 发放的是一次性 Promo。接口接受请求或返回 `k_EResultOK`，不代表一定生成了库存实例；玩家不符合资格或该 Promo 已经发放过时，结果仍可能成功，但其中不包含任何新增物品。客户端只有在返回结果或后续完整库存中实际确认目标 ItemDef 后，才能发放对应的本地奖励。

`ConsumeItem` 只会永久删除指定库存实例，不会重置 Steam 服务器对该账号保存的一次性 Promo 发放记录。回执被消耗后，再次调用同一 ItemDef 的 `AddPromoItem` 可能仍返回成功，但不会重新生成回执。因此，LinkTree 永久回执在正式业务中不得被消耗，也不能将 `ConsumeItem` 作为重置领奖资格的调试手段。

`GenerateItems` 可以为发行商组内的开发者账号生成测试实例，但它不会重置一次性 Promo 的发放记录。该接口只适合恢复测试账号中被误删的实例或验证库存读取，不适合验证完整的首次 `AddPromoItem` 领奖流程。

完整重测首次领奖流程时，应使用该 Steam 账号从未领取过的新 ItemDef，或使用另一个测试账号。独立 Steam Inventory 测试场景中的消耗和生成功能均会真实修改当前开发者账号的库存。

相关 Steam 官方说明：[ISteamInventory::AddPromoItem、ConsumeItem 与 GenerateItems](https://partner.steamgames.com/doc/api/isteaminventory)。

## 字段说明

### 基础识别

`Id`

Steam Inventory 的 ItemDef ID。每个 AppID 内唯一；发布并投入使用后不得改作其它用途。非创意工坊物品 ID 必须小于 `1000000`。

`Key`

稳定机器名，用于转换脚本、日志和策划识别。发布后不要改名。

`Type`

Steam 物品定义类型，使用 `ESteamItemDefType` 枚举。转换到 Steam schema 时会映射为 Steam 要求的小写字符串。

`Name`

Steam 后台的英文名称。`GameOnly=TRUE` 的内部回执不会向玩家显示，但仍应填写稳定、容易检索的英文名称。

`Description`

Steam 后台的英文说明。内部回执可使用简短技术说明。

`IsEnabled`

是否参与 Steam ItemDef 配置导出。已发布且不再使用的定义不应复用 ID；需要停止发放时应关闭促销资格或停用对应业务入口。

对于已经上传到 Steamworks 的定义，`IsEnabled=FALSE` 不应使转换器将该行从完整 Steam schema 中省略，否则仍持有该库存实例的玩家可能无法取得定义信息。该字段仅用于阻止从未发布的草稿参与首次发布；已发布定义需要停止使用时，应保留定义并关闭对应的业务入口或促销资格。

### 促销与发放

`PromoRule`

Steam 的 `promo` 属性原文。支持复杂规则，因此使用字符串而不是项目枚举。

常用填写：

1. `manual`
   - 仅在客户端显式调用 `AddPromoItem` 或 `AddPromoItems` 时检查和发放。
   - LinkTree 永久领奖回执使用此值。

2. `owns:2583700`
   - 正式 AppID 的拥有者满足促销资格。
   - 适合低价值的全员补偿；客户端调用 `GrantPromoItems` 后领取。

`GrantedManually`

是否只允许通过指定 ItemDef 的 `AddPromoItem` 或 `AddPromoItems` 发放。LinkTree 回执填写 `TRUE`，避免被通用 `GrantPromoItems` 意外领取。

### Steam 社区可见性

`Tradable`

是否允许在 Steam 玩家之间交易。永久回执、补偿回执、内部令牌通常填写 `FALSE`。

`Marketable`

是否允许在 Steam 社区市场出售。内部令牌通常填写 `FALSE`。

`GameOnly`

是否只在游戏内使用。填写 `TRUE` 时，物品不会显示在 Steam 背包和新物品通知中。永久领奖回执和内部资格令牌填写 `TRUE`。

`StoreHidden`

是否在 Steam Item Store 隐藏。非售卖物品填写 `TRUE`。

`AutoStack`

同一 ItemDef 的多次发放是否自动堆叠为一个库存实例。一次性永久回执不需要堆叠，填写 `FALSE`；可累积的消耗品可视业务需要填写 `TRUE`。

### 复杂物品

`Bundle`

仅 `Bundle`、`Generator` 和 `PlaytimeGenerator` 使用的内容配方。

1. `Bundle`
   - 填固定内容，例如 `100101;100102x5`。
   - Steam 发放礼包时自动展开为这些实际物品。

2. `Generator`
   - 填随机权重，例如 `100101x80;100102x20`。
   - Steam 发放生成器时按权重产生结果；生成器本身不会保留在玩家库存中。
   - 填 `@AUTO` 时，转换器会根据引用它的 `BlindBox`、`BlindBoxRarityRate` 和 `Item` 权重列生成完整奖池。

3. `PlaytimeGenerator`
   - 当前项目用于生成盲盒券，必须显式填写固定产物，例如 `402002x1`。
   - 客户端在合适时机调用 `TriggerItemDrop`，Steam 再根据游玩时间、冷却和投放上限决定是否发放。

普通 `Item` 和本来就没有内容配方的定义留空。不要使用“留空表示自动生成”的隐含约定。

## Generator 自动奖池

`@AUTO` 只允许用于 `Type=Generator`。转换器会通过 `BlindBox.SteamExchangeTargetItemDefId` 找到对应盲盒，再按与游戏本地盲盒相同的两阶段逻辑生成 Steam 的单层权重：

```text
物品最终概率 = 该品质概率 * 物品在该品质内的权重 / 该品质候选物品总权重
```

品质概率来自 `BlindBoxRarityRate`。品质内候选物品及权重列由盲盒类型决定，例如标准盲盒读取 `Item.StandardBoxWeight`，新手盲盒读取 `Item.NewbieBoxWeight`。转换器会把最终概率缩放为总量 `1000000` 的整数权重，并在生成前检查以下问题：

1. 启用品质缺少正概率。
2. 正概率品质没有可用候选物品。
3. 候选物品没有填写 `SteamItemDefId`。
4. 多个盲盒错误地共用同一个 `@AUTO` Generator。

因此，Steam 不需要先随机品质再随机物品。品质仍是本地策划结构，转换器负责把它无损展平为 Steam Generator 可以读取的单层奖池。

`Item.ItemRarity` 会自动生成 Steam 的小写 `rarity:` 标签。`Item.SteamTags` 只填写其它标签；手工填写 `rarity:` 会被转换器拒绝，避免品质字段与标签不一致。

## PlaytimeGenerator 投放

`BlindBoxSchedule.SteamPlaytimeGeneratorItemDefId` 指向负责该行投放资格的 PlaytimeGenerator。每个 PlaytimeGenerator 只能对应一条 Schedule，其 `Bundle` 必须显式产出该行盲盒的 `SteamOpenCostItemDefId`，例如：

```text
BlindBoxSchedule.BlindBoxId = 1002
BlindBox 1002.SteamOpenCostItemDefId = 402002
PlaytimeGenerator.Bundle = 402002x1
```

转换器依据本地投放时间、调试倍率和 Steam 提前量生成分钟级参数：

```text
实际投放秒数 = 本地基础秒数 * GameDevelopConfig.BlindBoxWaitDurationMultiplier
Steam 资格秒数 = max(0, 实际投放秒数 - GameDevelopConfig.SteamPlaytimeDropLeadSeconds)
drop_interval = max(1, ceil(Steam 资格秒数 / 60))
```

一次性 Schedule 的本地基础秒数使用 `StartSeconds`；循环 Schedule 使用 `IntervalSeconds`。`MaxGrantCount >= 0` 时生成 `use_drop_limit=true` 和相应 `drop_limit`；无限循环则生成 `use_drop_limit=false`。Steam 资格会比本地展示时间略早形成，但客户端仍需在本地 Schedule 到点后才展示和开启盲盒。

客户端不会等待 Steam 自动发放。运行时由 `BlindBoxSchedule` 在本地展示时间前 `SteamPlaytimeDropLeadSeconds` 秒，通过共享平台服务调用 `TriggerItemDrop`。下一条新手 Schedule 在本地倒计时期间就参与缺券恢复，不能等到可开启后才与后台循环投放竞争全局请求间隔。每次回执后都读取一次完整库存；回执为空时，Schedule 保持待处理并在限频间隔后复查。空结果不能证明该 Generator 已达到 `drop_limit`，也不能被记录为一次成功处理。若完整库存已经包含当前待展示盲盒所需的券，则当前 Schedule 已经得到实际满足，应停止重复触发对应 PlaytimeGenerator。

Steam 以分钟为粒度评估游玩投放，并会限制更频繁的 `TriggerItemDrop` 调用。客户端在所有 Schedule 之间共享至少 65 秒的请求间隔；不能在一批资格同时到期时连续触发多个 Generator。独立测试场景采用相同间隔，并在过早操作时直接显示剩余等待时间。

每条 Schedule 已处理到第几次 Steam 投放会保存在 `BlindBoxRuntimeState.SteamPlaytimeDropStates`。旧存档中已经领取过的新手 Schedule、循环轨道已经丢弃的历史资格不会重新补触发。Steam 连接中断或请求超过 10 秒时，平台恢复层会先读取完整库存比较盲盒券数量，再决定是否重试；不会直接重放一个结果未知的写请求。

独立测试场景 `TestSteamInventory.tscn` 可以选择任意已配置 Schedule 并真实调用对应 PlaytimeGenerator。该操作会修改当前 Steam 账号的投放记录；消耗产出的盲盒券或重置本地存档，都不会重置 Steam 的 `drop_limit` 和冷却状态。

Steamworks 后台已发布的 `playtimegenerator` 可能不会出现在客户端 `GetItemDefinitionIDs` 返回的定义列表中。客户端定义完整性检查只校验可枚举的普通物品、盲盒券、Bundle 和 Generator，不得因为 PlaytimeGenerator 未被枚举而把整套 Steam 库存判为不可用。PlaytimeGenerator 是否可用以实际 `TriggerItemDrop` 请求及其 Steam 回执为准；独立测试场景也采用同一规则。

独立测试场景的完整内容位于纵向滚动容器内，库存实例增加后仍可访问下方的发放、投放、维护、兑换和日志区域。

## 当前 Playtest 验证结果

当前 Playtest schema 共发布 120 条定义。Steam 客户端能够枚举 111 条普通物品及复杂定义，9 条 PlaytimeGenerator 不在枚举结果中，但均可提交真实 `TriggerItemDrop` 请求。

主人已在主游戏中验证：PlaytimeGenerator 自动产出盲盒券；`402002` 标准盲盒券和 `402003` 新手盲盒券都能被 `ExchangeItems` 原子消耗；Steam Generator 返回的装扮可以完成本地表演、背包同步和重启恢复。`402003` 堆叠数量从 2 逐次降为 1、0，归零后对应库存行消失，符合 Steam 堆叠语义。

## ESteamItemDefType

`ESteamItemDefType` 是项目的 Luban 枚举，不是 Steam 指定的数字枚举。Steam schema 使用的类型字符串由导出转换逻辑负责映射。

1. `Item=1`
   - Steam schema：`item`。
   - 普通物品，可实际存在于玩家 Steam 库存中。

2. `Bundle=2`
   - Steam schema：`bundle`。
   - 固定内容礼包，发放后自动展开。

3. `Generator=3`
   - Steam schema：`generator`。
   - 随机内容生成器。

4. `PlaytimeGenerator=4`
   - Steam schema：`playtimegenerator`。
   - 游玩投放生成器。

5. `TagGenerator=5`
   - Steam schema：`tag_generator`。
   - 为物品实例生成随机标签或词条；当前项目暂不使用。

## 导出与发布流程

Luban 导出和 Steamworks 发布是两步独立流程。前者只生成项目可读取的数据与代码，后者才会改变 Steam 服务器的 ItemDef 配置。

1. 主人在 Excel 的 `Item`、`SteamItemDef`、`BlindBox`、`BlindBoxSchedule`、`BlindBoxRarityRate`、`GameDevelopConfig` 和 `LinkTree` Sheet 维护业务数据。
2. Luban 导出本地数据和 C# 类型。
3. 转换脚本合并七张表的导出数据，生成自动奖池、游玩投放参数和盲盒交换关系，校验引用后输出 Steam schema JSON。
4. 主人在 Steamworks 后台分别为 Playtest AppID `4972240` 和正式 AppID `2583700` 上传并发布 ItemDef。
5. 客户端调用 `LoadItemDefinitions` 与对应的 Inventory API，同步 Steam 服务器已发布的定义和玩家库存。

Playtest 与正式版是独立 AppID。两边可以使用相同的 ItemDef ID，但必须分别上传和发布，玩家领取记录也不会自动继承。

## 当前范围与后续工作

当前转换器已支持：LinkTree 永久回执、盲盒券、盲盒交换关系、`@AUTO` Generator 奖池、Item 品质标签，以及由 `BlindBoxSchedule` 派生的 PlaytimeGenerator 投放参数。Playtest 与 Release 会生成结构相同、AppID 不同的完整 schema。

当前数据中仍保留部分 `4` 开头的测试 ItemDef。正式数据可继续使用新的 ID 段，但已经发布或产生过库存实例的测试定义仍需保留，不得删除或复用其 ID。
