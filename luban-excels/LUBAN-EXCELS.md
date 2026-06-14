---
status : revised
last_editor: 主人
last_edit: 2026-05-07
---

# Luban 配置工具目录

CardLight 项目的 Luban 配置数据管理目录。

## 目录结构

```
luban-excels/
├── luban.conf              ← Luban 主配置
├── xlsx/                   ← Excel 源数据
├── csv/                    ← xlsx2csv 导出目录
├── defines/                ← Luban 生成的 schema 定义
├── output-data/            ← Luban 生成的 CSV 文件
├── output-code/            ← Luban 生成的 c sharp 代码
├── xlsx2csv.py             ← Excel 转 CSV 工具
├── xlsx2csv.bat            ← 双击运行 xlsx2csv
├── xlsx2csv.config.json    ← xlsx2csv 配置忽略名单
├── xlsx2csv-help.md        ← xlsx2csv 使用说明
├── csvdiff.py              ← CSV 差异对比工具
├── __csvdiff.bat           ← 双击运行 csvdiff
├── csvdiff-help.md         ← csvdiff 使用说明
└── __gen_pretest.bat       ← 在测试文件夹中生成数据，审阅后再手动复制到项目中
```

## 工作流

### 阶段一：表结构设计

1. 用户和 AI 根据系统文档讨论表结构，AI 生成初版 CSV （小驼峰命名）
2. 用户将 CSV 转为符合 Luban 格式的 xlsx 文件（表头 + 样板数据），放入 `xlsx/`

### 阶段二：表头迭代完善（循环）

3. 用户在 xlsx 中需要 AI 生成内容的单元格填入 `@AI` 标记
4. 双击 `xlsx2csv.bat` 导出到 `csv/`
5. 将导出的 CSV 交给 AI，AI 填写 `@AI` 标记的内容（如变量英文名、类型等）
6. 用户将 AI 填充后的 CSV 转回 xlsx，放入 `xlsx/` 进行检查
7. 如需自定义类型（枚举等），编辑 `__enums__.xlsx`
8. 重复 3-7 步直到表头完善，期间可用 `__csvdiff.bat` 对比新旧 CSV 确认变更

> **注意**：阶段二是一个迭代过程，用户 ↔ AI 反复协作直到表头定型。

### 阶段三：Mock 数据填充

9. 表头确定后，再次导出 CSV，交给 AI 填充 Mock 数据
10. 用户将 AI 填充后的 CSV 转回 xlsx，放入 `xlsx/` 进行校验和存档

### 阶段四：代码生成与部署

11. 双击 `__gen_pretest.bat` 运行 Luban，在测试目录生成 JSON 和 C# 代码
12. 校验无误后，将生成的代码投入正式项目目录


## 工具

- **xlsx2csv** — 将 xlsx 按 Sheet 拆分为 CSV
- **csvdiff** — 对比两个 CSV 文件的差异
- **Luban** — 配置数据生成工具

## 说明

详细使用说明请查看各工具对应的 help 文档。