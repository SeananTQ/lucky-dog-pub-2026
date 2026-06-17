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
| BlindBoxWeight | int | 盲盒权重，决定抽中概率 |
| IsUnique | bool | 是否唯一，非唯一的物品可重复获得 |
| HiddenRegionFlag | EHiddenRegionFlag | 在哪些国家无法抽到（bit flag） |
| SafeResourceId | int | 安全替换资源 ID，直播/安全模式下替换和谐资源 |
| BlindBoxId | int | 所属盲盒 ID，相同 ID 的物品在同一组随机 |
| AssetPathList | list\<string\> | 资源路径列表，狗皮肤和卡面填文件夹路径，其余填文件路径 |

## 重要详解

### BlindBoxId

`BlindBoxId`:为该物品所属的盲盒id,如果该物品的盲盒id为0,则玩家默认拥有这些物品。

### BlindBoxWeight

`BlindBoxWeight`:该物品的权重，在随机时，将`BlindBoxId`相同的道具加到一起获得总权重，然后按照权重随机看命中哪个物品。

### HiddenRegionFlag

`HiddenRegionFlag`：如果玩家所安装的游戏国家版本与隐藏项相同，则在计算权重之前将这些隐藏物品剔除。即正常情况下，处于隐藏规则匹配的国家的玩家无法通过开盲盒获得这些道具。

### SafeResourceId

`SafeResourceId`：如果玩家开启了直播模式/安全模式，则它拥有的这些道具在显示时会被替换成SafeResourceId所指定的另一个道具。

### AssetPathList

`AssetPathList`：首先这是一个列表型字段，可以填写多个资源路径。目的是为了方便后续扩展，（例如某些物品可能是需要跨层，由两张图片组成，一张在小狗前，一张在小狗后。） @AI [如果你认为List会影响效率，需要优化我可以改]

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
