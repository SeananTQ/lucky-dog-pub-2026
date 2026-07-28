# Steam ItemDef 转换器

`build-steam-item-defs.js` 将 Luban 生成的 `tbsteamitemdef.json` 转换为 Steam Inventory schema，并校验 LinkTree 永久领奖回执的引用和安全配置。

## 图形界面

双击：

```text
Start-SteamItemDefTool.cmd
```

启动器会在后台开启仅监听 `127.0.0.1` 的本地服务，并使用默认浏览器打开 `http://127.0.0.1:43117/`。界面提供：

- SteamItemDef 与 LinkTree 引用总览。
- Playtest / Release AppID 切换。
- 每条 ItemDef 最终上传 JSON 预览。
- 本地奖励映射、错误和警告展示。
- 输出文件是否落后于 Luban 数据的状态检查。
- 校验通过后生成两个渠道的 schema。

页面每 30 秒向本地服务发送一次心跳。关闭所有工具页面后，服务会在连续空闲 10 分钟后自动退出；也可以点击右上角“关闭工具”立即退出。10 分钟内重新双击启动器会复用现有服务。

网页不接受任意文件路径；服务只读取项目内固定的两份 Luban JSON，并只写入本目录的 `generated`。生成和打开目录等操作要求工具页面发送专用请求头，普通外部网页无法直接调用写操作。

## 运行

在仓库根目录执行：

```powershell
node lucky-steamworks/steam-item-def/build-steam-item-defs.js
```

默认读取：

```text
lucky-dog-rise/Data/Json/tbsteamitemdef.json
lucky-dog-rise/Data/Json/tblinktree.json
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
- LinkTree 引用了不存在或已禁用的 ItemDef。
- 多条 LinkTree 共用同一个永久回执。
- 永久回执不是 `Type=Item`、`PromoRule=manual` 或 `GrantedManually=true`。
- 永久回执允许交易、出售或没有设置为游戏内隐藏。
- Bundle/Generator/PlaytimeGenerator 没有配置内容配方。

验证失败时只更新 `validation-report.json`，不会覆盖 schema 文件。禁止在校验失败后上传目录中可能残留的旧 schema。

## IsEnabled 约定

`IsEnabled=false` 的行不会进入生成结果。这个开关只适用于尚未发布的草稿；已经发布并产生过库存实例的 ItemDef 必须继续保留在 Steam schema 中，不得通过关闭该字段移除或复用 ID。

## 测试

```powershell
node --test lucky-steamworks/steam-item-def/*.test.js
```

转换器只生成文件，不会连接 Steamworks，也不会自动上传或发布配置。主人需要在 Playtest 和 Release 的 Inventory Service 页面分别上传、检查并发布对应 schema。
