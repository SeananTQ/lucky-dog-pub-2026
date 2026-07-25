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

Steamworks 为新建成就自动分配的 `NEW_ACHIEVEMENT_*` Token，需要在后台规范化为这套稳定 Token 后再上传生成的 VDF。

## Steamworks 操作顺序

1. 导出 Luban JSON。
2. 运行生成器，修复所有校验错误。
3. 在成就后台载入 `generated/steamworks-achievements.json`：先执行常规同步以创建并保存条目。
4. 将后台 Token 规范化为 `<ApiName>_NAME` / `<ApiName>_DESC`。
5. 重新分析并生成“图片操作报表”。脚本只判断 Steamworks 当前是否缺图，不上传图片；将报表交给浏览器操作模型，按 API 名称逐条上传并保存。
6. 在 Steamworks 本地化页分别上传英文和简中 VDF。
7. 人工复核后才在 Steamworks 发布变更。
