# xlsx2csv 使用说明

将 `xlsx/` 目录下的 Excel 文件按 Sheet 拆分为 CSV 文件，输出到 `csv/` 目录。

## 运行方式

**方式一：双击运行**
```
xlsx2csv.bat
```

**方式二：命令行**
```bash
python xlsx2csv.py
```

## 命令行参数

- `--config` / `-c` — 指定配置文件路径（默认同目录下的 `xlsx2csv.config.json`）
- `--xlsx-dir` / `-x` — 指定 xlsx 源目录（默认 `xlsx/`）
- `--csv-dir` / `-o` — 指定 csv 输出目录（默认 `csv/`）

## 配置文件

配置文件 `xlsx2csv.config.json`：

```json
{
    "ignore_files": [
        "__beans__.xlsx",
        "__enums__.xlsx"
    ],
    "ignore_sheets": [
        "^~.*",
        "^备注.*"
    ]
}
```

- `ignore_files` — 忽略的 xlsx 文件名（精确匹配）
- `ignore_sheets` — 忽略的 sheet 名称（正则表达式）

## 自动忽略规则

以下规则无需配置，程序自动生效：

- `~$` 开头的 Excel 临时文件自动忽略
- A1 单元格不以 `##` 开头的 sheet 自动忽略（与 Luban 约定一致）

## 同名文件处理

当目标 CSV 文件已存在时，自动追加 `_1`、`_2`... 直到找到不冲突的文件名。

## 输出命名规则

`xlsx/卡牌数据表.xlsx` 的 `卡牌总表` Sheet → `csv/卡牌数据表_卡牌总表.csv`

格式：`{xlsx文件名}_{sheet名}.csv`

## 依赖

需要 `openpyxl` 库：
```bash
pip install openpyxl