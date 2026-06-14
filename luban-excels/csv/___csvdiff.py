#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
csvdiff - 比对 CSV 文件，生成 Markdown 格式的 diff 报表

用法:
    python csvdiff.py <文件A> <文件B>       比对两个 CSV 文件
    python csvdiff.py <文件夹>              批量比对文件夹中同名的 CSV 版本文件

输出:
    在当前目录生成 {旧文件名}.diff.md
"""

import csv
import re
import sys
import os


def read_csv(filepath):
    """读取 CSV 文件，返回行列表"""
    with open(filepath, 'r', encoding='utf-8-sig', newline='') as f:
        reader = csv.reader(f)
        return [row for row in reader]


def get_column_letter(col_index):
    """将列索引转换为 Excel 列字母（0 -> A, 1 -> B, ...）"""
    result = ""
    col_index += 1
    while col_index > 0:
        col_index -= 1
        result = chr(ord('A') + col_index % 26) + result
        col_index //= 26
    return result


def diff_csv(file_a, file_b):
    """比对两个 CSV 文件"""
    rows_a = read_csv(file_a)
    rows_b = read_csv(file_b)

    changes = []
    max_rows = max(len(rows_a), len(rows_b))

    def is_empty_row(row):
        """判断一行是否全为空"""
        if row is None:
            return True
        return all(cell is None or str(cell).strip() == '' for cell in row)

    for row_idx in range(max_rows):
        row_a = rows_a[row_idx] if row_idx < len(rows_a) else None
        row_b = rows_b[row_idx] if row_idx < len(rows_b) else None

        # 跳过两行都为空的情况
        if is_empty_row(row_a) and is_empty_row(row_b):
            continue

        if row_a == row_b:
            continue

        # 找出该行中变化的列
        max_cols = max(len(row_a) if row_a else 0, len(row_b) if row_b else 0)
        row_changes = []

        for col_idx in range(max_cols):
            raw_a = row_a[col_idx] if row_a and col_idx < len(row_a) else None
            raw_b = row_b[col_idx] if row_b and col_idx < len(row_b) else None

            # 如果两边都是空，跳过
            if (raw_a is None or str(raw_a).strip() == '') and \
               (raw_b is None or str(raw_b).strip() == ''):
                continue

            val_a = raw_a if raw_a is not None else "<无>"
            val_b = raw_b if raw_b is not None else "<无>"

            if val_a != val_b:
                row_changes.append((get_column_letter(col_idx), val_a, val_b))

        if row_changes:
            changes.append((row_idx + 1, row_changes))

    return changes


def generate_report(file_a, file_b, changes):
    """生成 Markdown 格式报表"""
    name_a = os.path.splitext(os.path.basename(file_a))[0]
    name_b = os.path.splitext(os.path.basename(file_b))[0]

    lines = []
    lines.append(f"# CSV Diff 报表")
    lines.append("")
    lines.append(f"- **旧文件**: `{name_a}.csv`")
    lines.append(f"- **新文件**: `{name_b}.csv`")
    lines.append(f"- **变更行数**: {len(changes)}")
    lines.append("")

    if not changes:
        lines.append("无变更。")
        lines.append("")
    else:
        lines.append("## 变更明细")
        lines.append("")

        for row_num, row_changes in changes:
            lines.append(f"### 第 {row_num} 行")
            lines.append("")
            lines.append("| 列 | 旧值 | 新值 |")
            lines.append("|---|---|---|")
            for col_letter, val_old, val_new in row_changes:
                # 转义管道符防止表格格式错误
                val_old = str(val_old).replace("|", "\\|")
                val_new = str(val_new).replace("|", "\\|")
                lines.append(f"| {col_letter} | {val_old} | {val_new} |")
            lines.append("")

    return "\n".join(lines)


def determine_old_new(file_1, file_2):
    """根据文件名判断哪个是旧文件、哪个是新文件
    
    规则：
    - 带 _数字 的是新文件，不带的是旧文件
    - 如果都带数字后缀，数字小的是旧文件
    """
    name_1 = os.path.basename(file_1)
    name_2 = os.path.basename(file_2)
    
    pattern = r'_(\d+)\.csv$'
    match_1 = re.search(pattern, name_1)
    match_2 = re.search(pattern, name_2)
    
    if match_1 and match_2:
        num_1 = int(match_1.group(1))
        num_2 = int(match_2.group(1))
        if num_1 < num_2:
            return file_1, file_2
        else:
            return file_2, file_1
    elif match_1:
        return file_2, file_1
    elif match_2:
        return file_1, file_2
    else:
        return file_1, file_2


def get_base_name(filename):
    """提取基础文件名，去除 _数字 后缀
    
    例如: __enums___Sheet1_1.csv -> __enums___Sheet1
          __enums___Sheet1.csv   -> __enums___Sheet1
    """
    name = os.path.splitext(filename)[0]
    return re.sub(r'_\d+$', '', name)


def pair_csv_files_in_dir(directory):
    """从同一个目录中配对 CSV 版本文件
    
    配对规则：基础文件名相同，取版本最小的作为旧文件，版本最大的作为新文件
    例如: __enums___Sheet1.csv 和 __enums___Sheet1_1.csv 配对
    返回 [(old_path, new_path), ...]
    """
    files = [f for f in os.listdir(directory) if f.endswith('.csv')]
    
    # 按基础文件名分组
    base_to_files = {}
    for f in files:
        base = get_base_name(f)
        base_to_files.setdefault(base, []).append(f)
    
    pairs = []
    for base in sorted(base_to_files.keys()):
        file_list = base_to_files[base]
        if len(file_list) < 2:
            continue  # 只有一个版本，无需比对
        
        def version_key(filename):
            m = re.search(r'_(\d+)\.csv$', filename)
            return int(m.group(1)) if m else 0
        
        file_list.sort(key=version_key)
        pairs.append((
            os.path.join(directory, file_list[0]),
            os.path.join(directory, file_list[-1])
        ))
    
    return pairs


def diff_pair(file_old, file_new):
    """比对一对文件，返回 (report, output_path)"""
    changes = diff_csv(file_old, file_new)
    report = generate_report(file_old, file_new, changes)
    
    name_old = os.path.splitext(os.path.basename(file_old))[0]
    output_file = f"{name_old}.diff.md"
    
    return report, output_file


def main():
    if len(sys.argv) < 2:
        print("用法:")
        print("  python csvdiff.py <文件A> <文件B>       比对两个 CSV 文件")
        print("  python csvdiff.py <文件夹>              批量比对文件夹中的 CSV 版本文件")
        sys.exit(1)

    input_1 = sys.argv[1]

    # 单参数 + 是目录 => 文件夹批量模式
    if len(sys.argv) == 2 and os.path.isdir(input_1):
        directory = input_1
        print(f"扫描目录: {directory}")
        print()
        
        pairs = pair_csv_files_in_dir(directory)
        
        if not pairs:
            print("未找到可配对的 CSV 版本文件")
            return
        
        print(f"找到 {len(pairs)} 对文件需要比对:\n")
        
        for file_old, file_new in pairs:
            name_old = os.path.basename(file_old)
            name_new = os.path.basename(file_new)
            print(f"  {name_old} vs {name_new}")
        
        print()
        
        total_changes = 0
        for file_old, file_new in pairs:
            report, output_file = diff_pair(file_old, file_new)
            
            changes_count = len(diff_csv(file_old, file_new))
            total_changes += changes_count
            
            with open(output_file, 'w', encoding='utf-8') as f:
                f.write(report)
            
            status = f"{changes_count} 处变更" if changes_count else "无变更"
            print(f"  [{status}] {output_file}")
        
        print(f"\n完成! 共生成 {len(pairs)} 份报表，总计 {total_changes} 处变更")
    
    # 两参数 => 文件比对模式
    elif len(sys.argv) == 3:
        input_2 = sys.argv[2]
        
        if not os.path.isfile(input_1):
            print(f"错误: 找不到文件 {input_1}")
            sys.exit(1)
        if not os.path.isfile(input_2):
            print(f"错误: 找不到文件 {input_2}")
            sys.exit(1)
        
        file_old, file_new = determine_old_new(input_1, input_2)
        
        changes = diff_csv(file_old, file_new)
        report = generate_report(file_old, file_new, changes)
        
        name_old = os.path.splitext(os.path.basename(file_old))[0]
        output_file = f"{name_old}.diff.md"
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(report)
        
        print(report)
        print(f"\n报表已保存到: {output_file}")
    
    else:
        print("错误: 参数数量不正确")
        print("  python csvdiff.py <文件A> <文件B>       比对两个 CSV 文件")
        print("  python csvdiff.py <文件夹>              批量比对文件夹中的 CSV 版本文件")
        sys.exit(1)


if __name__ == '__main__':
    main()