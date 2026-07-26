# Steamworks 成就生成工具

`build-steamworks-achievements.js` 从 Luban 导出的 `tbachievement.json` 生成 Steamworks 所需文件。

## 生成

在仓库根目录运行：

```powershell
node lucky-steamworks/achievement/tools/build-steamworks-achievements.js
```

默认输入与输出：

```text
lucky-dog-rise/Data/Json/tbachievement.json
  └── lucky-steamworks/achievement/generated/steamworks-achievements.json
  └── lucky-steamworks/achievement/generated/validation-report.json
  └── lucky-steamworks/achievement/vdf/steamworks-achievements.english.vdf
  └── lucky-steamworks/achievement/vdf/steamworks-achievements.schinese.vdf
```

生成器要求每条成就都具有 `ApiName`、`IsHidden`、英文/简中名称和描述。图标默认按以下约定校验：

```text
icon/achieved/<ApiName>.png
icon/unachieved/<ApiName>.png
```

开发文案时可暂时跳过图标校验：

```powershell
node lucky-steamworks/achievement/tools/build-steamworks-achievements.js --skip-icon-check
```

这只用于预览 VDF；准备导入 Steamworks 前必须使用默认校验成功的构建。

## VDF Token

Token 永远由 API 名派生，不在 Excel 中人工维护：

```text
ACH_FIRST_DOG_NAME
ACH_FIRST_DOG_DESC
```

Steamworks 为新建成就自动分配的 `NEW_ACHIEVEMENT_*` Token。同步脚本会在“[本地化字符串]”视图中把它们改为这套稳定 Token；英文和简中等实际文案只由 VDF 上传。

## Steamworks 操作顺序

1. 导出 Luban JSON。
2. 运行生成器，修复所有校验错误。
3. 在成就后台的语言下拉框选择 **[本地化字符串]**，再载入 `generated/steamworks-achievements.json` 并执行常规同步。它会创建/保存条目，并填写 `<ApiName>_NAME`、`<ApiName>_DESC`。
4. 切换到 English / Simplified Chinese；此时尚未上传 VDF 的成就文案为空是正常现象。
5. 在 Steamworks 本地化页分别上传英文和简中 VDF。
6. 重新分析并生成“图片操作报表”。图标根目录可在面板中修改；粘贴时允许带单/双引号或使用正反斜杠，脚本会自动规范化。脚本只判断 Steamworks 当前是否缺图，不上传图片；将报表交给浏览器操作模型，按 API 名称逐条上传并保存。
7. 人工复核后才在 Steamworks 发布变更。

图片报表的实际执行顺序：每条成就进入 `Edit`，上传 `achieved` 和 `unachieved` 两张图，等待页面预览图出现后点击该行 `保存` 一次。浏览器文件选择器使用 Windows 路径时，推荐把报表路径转换为正斜杠；Steamworks 上传提示超时也不代表一定失败，必要时先刷新页面确认图片是否已经保存，再从未完成项继续。

正式版首次上传的实测注意事项：28 条成就的 56 张图标需要逐张经过 Steamworks 异步回调，速度可能明显慢于 Playtest。每次上传后当前编辑行可能被重绘，下一张图片必须重新定位文件控件和“上传”按钮；设置文件后要等待控件更新，再点击上传。页面成功提示是可复用节点，不能按提示次数计数，应以对应预览图出现新的 Steam 图片 URL 为准。超时后先检查当前编辑行和已保存行，从未完成图片续跑，不要重复保存已完成成就。
