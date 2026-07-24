# Steamworks Achievement Sync

项目专用的 Steamworks 成就批量同步工具。它在已登录的 Steamworks 成就配置页面内运行，通过页面现有控件新建、检查、更新成就并上传图标。

当前允许的 AppID：

- `4972240`：Lucky Dog Rise Playtest
- `2583700`：Lucky Dog Rise

脚本不会删除成就，也不会点击 Steamworks 的最终“发布”操作。它分为两种明确的操作模式：常规同步会自动保存成就条目；图标暂存会上传图标，但绝不会保存编辑行。

## 当前状态

这是第一版控制台脚本。请先在 Playtest AppID 上用少量测试数据验证，再用于完整列表和正式 AppID。

## 使用方法

1. 登录 Steamworks，打开对应 AppID 的“成就配置”页面。
2. 确保页面中没有处于编辑状态、尚未保存的成就行。
3. 打开浏览器开发者工具的 Console。
4. 将 `steamworks-achievement-sync.js` 的完整内容粘贴并运行。
5. 在页面右上角出现的面板中选择成就 JSON。
6. 如果需要上传图标，选择包含图标的目录。
7. 点击“分析差异”查看计划。
8. 确认目标 AppID、创建数和检查数后，点击“执行同步”。
9. 第一遍取消勾选“批量暂存已有成就的图标”，执行常规同步。脚本会批量新建/更新字段，并自动保存每条成就。
10. 刷新页面后重新“分析差异”，勾选“批量暂存已有成就的图标”，再次执行同步。脚本会先打开所有目标编辑行，再上传图标，且不会点击任何一行的“保存”。
11. 逐条复核图标，手动点击每行的“保存”。全部完成后在 Steamworks 页面再次复核，再自行发布更改。

Steamworks 或浏览器可能阻止首次在 Console 粘贴代码。请只粘贴仓库中已审阅的脚本，不要粘贴来源不明的网页代码。

## JSON 格式

可参考 `example.achievements.json`。根节点可以直接是数组，也可以是：

```json
{
  "achievements": []
}
```

每项支持以下字段：

| 字段 | 必填 | 说明 |
|---|---:|---|
| `apiName` | 是 | Steam API 稳定名称，只允许字母、数字和下划线 |
| `displayName` / `name` | 否 | 默认英语显示名称；仅在启用英语文案同步时更新已有成就 |
| `description` / `desc` | 否 | 默认英语描述；仅在启用英语文案同步时更新已有成就 |
| `localizations.english.name` | 可替代 | 可替代 `displayName` |
| `localizations.english.description` | 可替代 | 可替代 `description` |
| `permission` | 否 | `0` 客户端、`1` 游戏服务器、`2` 官方游戏服务器；默认 `0` |
| `hidden` / `isHidden` | 否 | 是否隐藏；默认 `false` |
| `progressStat` | 否 | 进度统计的 option value；无进度时为 `-1` |
| `minValue` | 否 | 进度最小值；默认 `0` |
| `maxValue` | 否 | 进度最大值；默认 `0` |
| `achievedIcon` | 否 | 已达成图标在所选目录内的相对路径或文件名 |
| `unachievedIcon` | 否 | 未达成图标在所选目录内的相对路径或文件名 |

若使用 `localizations`，脚本只读取 `english` 作为可选的英语文本来源。其他语言应使用 Steamworks 官方“成就本地化”页面导出/上传的本地化文件维护。

新建成就时，Steamworks 要求英语名称和描述非空。如果 JSON 未提供这两个字段，脚本自动使用以下稳定占位内容：

```text
显示名称：<API Name>
描述：Pending localization.
```

之后上传包含英语在内的官方本地化文件即可覆盖占位文本。

## 图标目录示例

```text
achievement-icons/
├── achieved/
│   ├── ACH_FIRST_DOG.jpg
│   └── ACH_FIRST_THREE_OF_A_KIND.jpg
└── unachieved/
    ├── ACH_FIRST_DOG.jpg
    └── ACH_FIRST_THREE_OF_A_KIND.jpg
```

选择最外层的 `achievement-icons` 目录。脚本会同时按完整相对路径、去掉根目录后的路径以及文件名建立索引。

## 同步行为

- JSON 中存在、Steamworks 不存在：常规同步会新建并自动保存；缺少英语文案时使用占位文本。
- 两边都存在：常规同步仅比较已勾选职责范围内的字段；有差异才自动保存。
- Steamworks 存在、JSON 不存在：不处理、不删除。
- “检查并更新已有成就的结构字段”默认开启，管理 API 名称、进度、权限和隐藏状态。
- “更新已有成就的英语名称与描述”默认关闭，避免覆盖官方本地化文件上传的正式文案。
- “批量暂存已有成就的图标”默认关闭，因为页面没有稳定的图像内容哈希可供差异比较。
- 图标暂存模式要求 JSON 中的成就都已存在；Steamworks 会拒绝未保存新成就的图标上传。请先完成一次常规同步并刷新页面。
- 图标暂存时，脚本会先验证 Steamworks 是否能同时保留全部目标编辑行；验证通过后才上传图标，之后将所有编辑行保留给主人复核和手动保存。
- 图标上传优先等待对应单元格的预览图发生变化，同时兼容 Steamworks 隐藏 iframe 的完成事件；单张图最长等待 90 秒。
- 常规同步会点击成就行的“保存”；图标暂存模式则绝不点击“保存”。
- 任意一步失败后停止。重新加载页面并再次分析即可续跑，已经保存的项目会被识别为已有项目。

## 安全限制

- 目标 AppID 不在白名单时脚本拒绝运行。
- 页面存在未保存编辑行时拒绝分析或同步。
- 执行前显示目标 AppID 和操作数量，并要求二次确认。
- 不读取、保存或导出 Steam Cookie、会话令牌及 Steam Guard 信息。
- 不删除任何 Steamworks 数据。
- 不执行最终发布。

## 已知限制

- Steamworks 是未公开承诺 DOM 兼容性的后台页面；Valve 修改页面结构后，脚本可能需要同步调整。
- 成就创建/保存与图标上传均已在 Playtest 后台进行过小批量验证；正式使用前仍应先在 Playtest 上抽样验证。
- `progressStat` 使用 Steamworks 页面中统计项的 option value，不是显示文字；两边 AppID 的统计定义也必须一致。
- Steamworks 的图标上传回调偶尔不会可靠触发 iframe `load` 事件，因此脚本同时观察图标预览变化；若仍超时，应刷新页面确认后台是否已经收到图片。
