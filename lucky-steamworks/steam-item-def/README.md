# Steam ItemDef 转换器

`build-steam-item-defs.js` 合并 Luban 生成的 Steam 平台规则、实际物品、盲盒概率和投放计划，转换为 Steam Inventory schema，并校验 LinkTree 回执、BlindBox 交换关系与 PlaytimeGenerator 投放关系。

## 图形界面

双击：

```text
Start-SteamItemDefTool.cmd
```

启动器会在后台开启仅监听 `127.0.0.1` 的本地服务，并使用默认浏览器打开 `http://127.0.0.1:43117/`。界面提供：

- SteamItemDef、Item、LinkTree、BlindBox 与游玩投放计划引用总览。
- Playtest / Release AppID 切换。
- 每条 ItemDef 最终上传 JSON 预览。
- 本地奖励映射、错误和警告展示。
- 输出文件是否落后于 Luban 数据的状态检查。
- 校验通过后生成两个渠道的 schema。

页面每 30 秒向本地服务发送一次心跳。关闭所有工具页面后，服务会在连续空闲 10 分钟后自动退出；也可以点击右上角“关闭工具”立即退出。10 分钟内重新双击启动器会复用现有服务。

网页不接受任意文件路径；服务只读取项目内固定的七份 Luban JSON，并只写入本目录的 `generated`。生成和打开目录等操作要求工具页面发送专用请求头，普通外部网页无法直接调用写操作。

## 运行

在仓库根目录执行：

```powershell
node lucky-steamworks/steam-item-def/build-steam-item-defs.js
```

默认读取：

```text
lucky-dog-rise/Data/Json/tbsteamitemdef.json
lucky-dog-rise/Data/Json/tblinktree.json
lucky-dog-rise/Data/Json/tbitem.json
lucky-dog-rise/Data/Json/tbblindbox.json
lucky-dog-rise/Data/Json/tbblindboxschedule.json
lucky-dog-rise/Data/Json/tbblindboxrarityrate.json
lucky-dog-rise/Data/Json/tbgamedevelopconfig.json
```

默认生成：

```text
lucky-steamworks/steam-item-def/generated/steam-itemdefs.playtest.json
lucky-steamworks/steam-item-def/generated/steam-itemdefs.release.json
lucky-steamworks/steam-item-def/generated/validation-report.json
```

Playtest schema 固定使用 AppID `4972240`，Release schema 固定使用 AppID `2583700`。转换器不接受任意 AppID，避免把定义上传到错误应用。

只生成一个渠道：

```powershell
node lucky-steamworks/steam-item-def/build-steam-item-defs.js --channel playtest
node lucky-steamworks/steam-item-def/build-steam-item-defs.js --channel release
```

查看全部参数：

```powershell
node lucky-steamworks/steam-item-def/build-steam-item-defs.js --help
```

## 校验规则

转换器会阻止以下配置生成可上传 schema：

- ItemDef ID 重复、超出 `1..999999` 或 Key 重复。
- `SteamItemDef` 与 `Item` 的 ItemDef ID 冲突，或多个 `Item` 共用同一 ID。
- Generator/Bundle 配方引用了未导出的 ItemDef。
- `@AUTO` 被用于非 Generator 定义，或自动奖池候选物品没有 Steam ItemDef 映射。
- LinkTree 引用了不存在或已禁用的永久回执或领奖 Bundle。
- 多条 LinkTree 共用同一个永久回执或领奖 Bundle。
- 永久回执不是安全的 `Type=Item`，或领奖目标不是安全的 `Type=Bundle`、`PromoRule=manual`、`GrantedManually=true`。
- LinkTree 领奖 Bundle 的实际内容与奖励类型不一致。固定物品必须包含永久回执和 `Item.SteamItemDefId`；筹码只包含永久回执；盲盒必须包含永久回执和 `BlindBox.SteamOpenCostItemDefId`。
- Bundle/Generator/PlaytimeGenerator 没有配置内容配方。
- 非 PlaytimeGenerator 配置了 Steam 投放上限，或启用 Schedule 引用了已经显式停发的 Generator。
- BlindBox 只填了一个 Steam 开箱 ID，或消耗项/交换目标的类型不正确。
- 多个盲盒共用同一个 `@AUTO` Generator，或自动奖池缺少有效品质概率/候选物品。
- BlindBoxSchedule 引用的 PlaytimeGenerator 不存在、被多条计划共用，或其 Bundle 与盲盒开箱成本不一致。

BlindBox 的两个 Steam ID 都为 `0` 时不生成交换规则；如果该盲盒配置了 `IsPlatformInventoryRequired=true`，转换器会给出警告。

## 自动生成规则

### Generator 奖池

当 `SteamItemDef.Type=Generator` 且 `Bundle=@AUTO` 时，转换器根据引用该 Generator 的 `BlindBox` 自动生成奖池。它先读取 `BlindBoxRarityRate` 的品质概率，再读取 `Item` 中该盲盒类型对应的权重列，将两阶段概率展平为 Steam 的单层权重：

```text
物品最终概率 = 品质概率 * 物品在该品质内的权重占比
```

生成权重统一缩放到 `1000000`，并进行整数舍入和约分。没有候选物品的品质不会被静默转移给其它品质，而是使转换失败，避免 Steam 实际概率与策划表不一致。

`Item.ItemRarity` 会自动生成小写的 Steam `rarity:` 标签；`Item.SteamTags` 仅填写其它自定义标签，不要重复填写 `rarity:`。

### PlaytimeGenerator 投放

`BlindBoxSchedule.SteamPlaytimeGeneratorItemDefId` 把一条本地投放计划映射到一个 Steam PlaytimeGenerator。转换器会校验该生成器的显式 `Bundle` 等于盲盒的 `SteamOpenCostItemDefId x1`，并自动生成 Steam 的分钟级投放参数。

```text
一次性 Steam 资格秒数 = max(0, StartSeconds * BlindBoxWaitDurationMultiplier - SteamPlaytimeDropLeadSeconds)
循环 Steam 资格秒数 = IntervalSeconds * BlindBoxWaitDurationMultiplier
drop_interval = max(1, ceil(Steam 资格秒数 / 60))
```

一次性计划继续使用提前量；循环计划不扣除提前量。循环行可通过 `SteamDropWindowSeconds` 和 `SteamDropMaxPerWindow` 生成 `use_drop_window`、`drop_window` 和 `drop_max_per_window`。`MaxGrantCount >= 0` 时生成对应的发放上限；无上限循环计划生成 `use_drop_limit=false`。

已经发布但需要永久停止投放的 PlaytimeGenerator 应继续保留在 `SteamItemDef` 中，并填写 `SteamUseDropLimit=true`、`SteamDropLimit=0`。显式上限只允许用于没有被启用 Schedule 引用的 PlaytimeGenerator，避免与 Schedule 自动派生规则冲突。

验证失败时只更新 `validation-report.json`，不会覆盖 schema 文件。禁止在校验失败后上传目录中可能残留的旧 schema。

## IsEnabled 约定

`IsEnabled=false` 的行不会进入生成结果。这个开关只适用于尚未发布的草稿；已经发布并产生过库存实例的 ItemDef 必须继续保留在 Steam schema 中，不得通过关闭该字段移除或复用 ID。

## 测试

```powershell
node --test lucky-steamworks/steam-item-def/*.test.js
```

转换器只生成文件，不会连接 Steamworks，也不会自动上传或发布配置。主人需要在 Playtest 和 Release 的 Inventory Service 页面分别上传、检查并发布对应 schema。
