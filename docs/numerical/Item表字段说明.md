---
last_editor: Codex
last_edit: 2026-08-28
status: draft
---

# Item 表字段说明

## 字段列表

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int | 物品唯一 ID |
| Name | string | 显示名称 |
| 图层名称 | string | 内部名称，方便记忆 |
| ItemType | EItemType | 物品类型 |
| ItemRarity | ERarity | 品质 |
| SortOrder | int | 排序权重，数字越小约靠前 |
| IsHiddenInBag | bool | 是否在背包中隐藏 |
| AcquisitionType | EAcquisitionType | 物品的主要获取来源；Initial 表示永久基础物品 |
| StandardBoxWeight | int | 标准装扮盲盒中的品质内权重 |
| NewbieBoxWeight | int | 新手装扮盲盒中的品质内权重 |
| RefreshmentBoxWeight | int | 消耗品盲盒中的品质内权重 |
| EventBoxWeight | int | 活动盲盒中的品质内权重 |
| HiddenRegionFlag | EHiddenRegionFlag | 在哪些国家无法抽到（bit flag） |
| SafeResourceId | int | 安全替换资源 ID，直播/安全模式下替换和谐资源 |
| AssetPathList | list\<string\> | 资源路径列表，狗皮肤和卡面填文件夹路径，其余填文件路径 |
| IconPath | string | 背包内显示的图标路径 |
| SteamItemDefId | int | 对应的 Steam ItemDef ID；未接入 Steam 时填 0 |
| SteamTags | string | 额外 Steam Item Tags；品质标签由转换器根据 ItemRarity 自动生成 |

## 重要详解

### AcquisitionType

`AcquisitionType` 表达物品的主要获取来源。新建或重置本地存档时，玩家默认拥有 `Initial` 物品。初始物品是必选装备槽的永久基础资产，不进入盲盒奖池，未来也不得被回收或熔炼。

`Retired` 表示已经下架的历史物品。玩家已有数量和 Steam 库存实例继续保留，但新客户端不得再通过盲盒、LinkTree、普通发奖或调试候选池主动发放，也不得自动穿戴或产生新的获得类成就和玩家统计。历史统计不追溯扣除。

`IsHiddenInBag` 只控制背包显示，不能替代 `Retired` 的业务语义。仍在运营但暂时隐藏的物品可以使用 `IsHiddenInBag`；需要永久停止新增获取时必须使用 `AcquisitionType=Retired` 并同步移出 Steam 发奖定义。

相同道具允许重复获得并累计数量。Steam Generator 不会根据玩家已有库存动态排除候选物品。

Dev 调试工具同样遵守下架边界：全物品随机穿戴、已拥有物品随机穿戴和轮换获得道具都排除 `Retired`。轮换获得道具仍按 `EItemType` 枚举轮换，并允许获得 Dog。该入口继续只选择 `IsHiddenInBag=FALSE` 的普通可见物品；这是调试候选的可见性限制，不承担下架语义。

### 盲盒权重

每种盲盒使用独立的品质内权重字段：标准装扮盲盒使用 `StandardBoxWeight`，新手装扮盲盒使用 `NewbieBoxWeight`，消耗品盲盒使用 `RefreshmentBoxWeight`，活动盲盒使用 `EventBoxWeight`。权重大于 0 的物品才会进入对应奖池。

品质概率由 `BlindBoxRarityRate` 配置。系统先按品质概率确定品质，再在该品质的候选物品中按上述字段随机。

### SteamTags

转换器根据 `ItemRarity` 自动生成小写的 Steam 品质标签，例如 `rarity:epic`。`SteamTags` 只填写无法从现有字段派生的额外标签；多个标签使用分号分隔。

### HiddenRegionFlag

`HiddenRegionFlag`：如果玩家所安装的游戏国家版本与隐藏项相同，则在计算权重之前将这些隐藏物品剔除。即正常情况下，处于隐藏规则匹配的国家的玩家无法通过开盲盒获得这些道具。

### SafeResourceId

`SafeResourceId`：如果玩家开启了直播模式/安全模式，则它拥有的这些道具在显示时会被替换成SafeResourceId所指定的另一个道具。

### AssetPathList

`AssetPathList` 是列表型字段，可以填写多个资源路径，以支持一个物品由多个跨层资源组成。

`v1\Shiba\Red\`：该路径为文件夹，则意味着和开发人员约定了到另外一个表里找具体的内容。例如该道具具体要在`DogSkin`表里找对应的数据，`SkinId`指定了改物品在`DogSkin`里所对应的数据行。

`v1\Card\CardFace\Classic\`：因为卡面数据为52张扑克牌，因此直接填写文件夹


### ItemRarity

道具品质对应道具的边框和底板，读`RarityUI`表即可理清资源关系




## 枚举

### EItemType

| 值 | 说明 |
|----|------|
| Dog | 小狗本体 |
| Headwear | 小狗头部装饰 |
| Eyewear | 小狗眼部装饰 |
| Arm | 手臂层的装饰 |
| Clothes | 玩家手臂衣服 |
| Table | 桌布 |
| Background | 背景 |
| Accessory | 玩家手部装饰 |
| Treat | 玩家嗜好品 |
| CardBack | 卡牌背面 |

### ERarity

| 值 | 中文 |
|----|------|
| Legendary | 传说 |
| Epic | 史诗 |
| Rare | 稀有 |
| Common | 普通 |
| Special1 | 特殊1 |
| Special2 | 特殊2 |

### EHiddenRegionFlag

bit flag 枚举，可组合使用（如 CN\|SA = 258）。

| 值 | 国家 | Flag |
|----|------|------|
| ALL | 全球 | 1 |
| SA | 沙特阿拉伯 | 2 |
| AE | 阿联酋 | 4 |
| IR | 伊朗 | 8 |
| PK | 巴基斯坦 | 16 |
| MY | 马来西亚 | 32 |
| ID | 印尼 | 64 |
| RU | 俄罗斯 | 128 |
| CN | 中国 | 256 |
| KP | 朝鲜 | 512 |
| SY | 叙利亚 | 1024 |
| CU | 古巴 | 2048 |

## 备注

- **AssetPathList**：狗皮肤因由多个文件组成，直接填文件夹路径（如 `v1\Shiba\Red\`）；其余物品填写具体文件路径（如 `v1\Headwear\Beret_Green.png`）
- **SafeResourceId**：指向另一 Item 的 Id，当直播/安全模式开启时用该资源替换原资源，替换规则高于国家规则
- **HiddenRegionFlag**：为空表示不限国家；有值时该物品在对应国家盲盒抽奖中不可出现，即不可被抽中
