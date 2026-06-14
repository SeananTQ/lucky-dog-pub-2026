#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
xlsx2csv - 将 xlsx 文件的每个 sheet 导出为 CSV

用法:
    python xlsx2csv.py [配置路径]

配置项 (xlsx2csv.config.json):
    ignore_files  - 忽略的 xlsx 文件名列表（精确匹配）
    ignore_sheets - 忽略的 sheet 名称正则表达式列表
"""

import argparse
import json
import os
import re
import sys

try:
    import openpyxl
except ImportError:
    print("错误: 需要 openpyxl 库，请先运行: pip install openpyxl")
    sys.exit(1)


# 默认配置
DEFAULT_CONFIG = {
    "ignore_files": [],
    "ignore_sheets": []
}


def load_config(config_path):
    """加载配置文件，不存在则返回默认配置"""
    if os.path.isfile(config_path):
        with open(config_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    return DEFAULT_CONFIG.copy()


def should_ignore_file(filename, ignore_files):
    """判断 xlsx 文件是否应该忽略"""
    # 自动忽略 Excel 临时文件
    if filename.startswith('~$'):
        return True
    # 精确匹配忽略名单
    if filename in ignore_files:
        return True
    return False


def should_ignore_sheet(sheet, ignore_sheets):
    """判断 sheet 是否应该忽略
    
    规则：
    1. 自动忽略 A1 单元格不是 ## 开头的 sheet（与 Luban 一致）
    2. 匹配忽略名单正则表达式的 sheet
    """
    # 检查 A1 单元格是否以 ## 开头（Luban 约定）
    a1_value = sheet['A1'].value
    if a1_value is None or not str(a1_value).startswith('##'):
        return True
    
    # 检查 sheet 名称是否匹配忽略规则
    for pattern in ignore_sheets:
        if re.search(pattern, sheet.title):
            return True
    return False


def xlsx_to_csv(xlsx_path, csv_dir, ignore_files, ignore_sheets):
    """将单个 xlsx 文件的每个 sheet 导出为 CSV"""
    filename = os.path.basename(xlsx_path)

    if should_ignore_file(filename, ignore_files):
        print(f"  [忽略] {filename}")
        return 0

    print(f"  [处理] {filename}")
    wb = openpyxl.load_workbook(xlsx_path, read_only=True, data_only=True)
    count = 0

    for sheet in wb.worksheets:
        sheet_name = sheet.title

        if should_ignore_sheet(sheet, ignore_sheets):
            print(f"    [忽略 sheet] {sheet_name} (A1 不符合 ## 约定)")
            continue

        # 生成 CSV 文件名: xlsx文件名_sheet名.csv
        # 如果同名文件已存在，追加 _1, _2, ...
        base_name = f"{os.path.splitext(filename)[0]}_{sheet_name}"
        csv_filename = f"{base_name}.csv"
        csv_path = os.path.join(csv_dir, csv_filename)

        if os.path.exists(csv_path):
            counter = 1
            while True:
                csv_filename = f"{base_name}_{counter}.csv"
                csv_path = os.path.join(csv_dir, csv_filename)
                if not os.path.exists(csv_path):
                    break
                counter += 1

        # 写入 CSV（UTF-8 BOM，Excel 兼容）
        with open(csv_path, 'w', encoding='utf-8-sig', newline='') as f:
            writer = __import__('csv').writer(f)
            for row in sheet.iter_rows():
                writer.writerow([
                    '' if cell.value is None else cell.value
                    for cell in row
                ])

        count += 1
        print(f"    [导出] {csv_filename}")

    wb.close()
    return count


def main():
    parser = argparse.ArgumentParser(description='将 xlsx 的每个 sheet 导出为 CSV')
    parser.add_argument('--config', '-c', help='配置文件路径')
    parser.add_argument('--xlsx-dir', '-x', help='xlsx 源目录')
    parser.add_argument('--csv-dir', '-o', help='csv 输出目录')
    args = parser.parse_args()

    # 确定脚本所在目录（默认 excels/）
    script_dir = os.path.dirname(os.path.abspath(__file__))

    # 确定配置路径
    if args.config:
        config_path = args.config
    else:
        config_path = os.path.join(script_dir, 'xlsx2csv.config.json')

    # 确定目录路径
    xlsx_dir = args.xlsx_dir or os.path.join(script_dir, 'xlsx')
    csv_dir = args.csv_dir or os.path.join(script_dir, 'csv')

    # 加载配置
    config = load_config(config_path)
    ignore_files = config.get('ignore_files', [])
    ignore_sheets = config.get('ignore_sheets', [])

    print(f"xlsx 目录: {xlsx_dir}")
    print(f"csv  输出: {csv_dir}")
    print(f"忽略文件: {ignore_files}")
    print(f"忽略 sheet: {ignore_sheets}")
    print()

    # 确保输出目录存在
    os.makedirs(csv_dir, exist_ok=True)

    # 遍历所有 xlsx 文件
    xlsx_files = [f for f in os.listdir(xlsx_dir) if f.endswith('.xlsx')]

    if not xlsx_files:
        print("未找到任何 .xlsx 文件")
        return

    total = 0
    for filename in sorted(xlsx_files):
        xlsx_path = os.path.join(xlsx_dir, filename)
        count = xlsx_to_csv(xlsx_path, csv_dir, ignore_files, ignore_sheets)
        total += count

    print(f"\n完成! 共导出 {total} 个 CSV 文件")


if __name__ == '__main__':
    main()