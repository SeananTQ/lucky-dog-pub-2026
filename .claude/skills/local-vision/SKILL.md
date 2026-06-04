---
name: local-vision
description: 当需要看图片时使用该技能
---

# local-vision

## 概述

若当前模型不具备视觉能力时，可通过 Python 脚本调用本地 LM Studio 视觉模型进行分析，不依赖任何云端 API，图片数据不出本机。


## 工程结构

```
local-vision/
  ├── SKILL.md                    ← 本文件
  ├── config.json                 ← API 地址、模型名、超时、提示词路径，可在此处配置预设 prompt 的路径
  ├── scripts/
  │   └── analyze_image.py        ← 主脚本，零外部依赖
  └── prompts/
      ├── system.md               ← 系统提示词
      ├── describe.md             ← 通用描述
      ├── game_asset.md           ← 游戏资产结构化分析（分点）
      └── game_asset_structured.md ← 游戏资产结构化分析（JSON）

## 使用方法

脚本路径：`local-vision/scripts/analyze_image.py`

| 参数 | 作用 |
|------|------|
| `--image` | 单张图片路径 |
| `--dir` | 图片文件夹路径（递归扫描） |
| `--prompt` | 预设 prompt 键名，对应 config.json 的 prompts 映射 |
| `--prompt-text` | 临时用户提示词（覆盖 --prompt，不读文件） |
| `--system` | 临时系统提示词（覆盖 prompts/system.md） |
| `--json` | 输出保存为 .json |
| `--md` | 输出保存为 .md |


## 示例

简单查看图片，用于快速了解图片的内容和类型，判断是否有必要进一步分析：
```bash
# 简单分析单张图片（使用预设 describe prompt）
python local-vision/scripts/analyze_image.py --image 图片路径.png --prompt describe
```


以游戏资产的角度分析图片的内容：
```bash
# 单张图分析（使用预设 game_asset prompt）
python local-vision/scripts/analyze_image.py --image 图片路径.png --prompt game_asset
```


自定义提示词，适合临时需求或调试：
```bash
# 自定义系统提示词（临时，不改文件）
python local-vision/scripts/analyze_image.py --image 截图.png --prompt describe --system "你是一个UI测试工具，检查布局异常"

# 临时用户提示词（跳过 prompt 文件，直接问）
python local-vision/scripts/analyze_image.py --image 底板.png --prompt-text "这张图的圆角半径大概多少像素？"
```

**编程时用于查看游戏运行时的界面布局问题可以这样做：**
```bash
# system + prompt-text 组合，适合编程 AI 传上下文给视觉 AI
python local-vision/scripts/analyze_image.py --image screenshot.png \
  --system "你是一个卡组构筑游戏项目的 UI 布局分析助手，你需要通过游戏运行时的界面截图来分析用户提出的具体问题，在分析时请结合 UGUI 相关的知识" \
  --prompt-text "这是当前卡牌主游戏界面截图，用户反馈UI元素堆在一起了。请先客观描述你看到的问题，然后如果可以请分析可能的原因和解决方案。"
```
>具体的提示词需要根据当前正在开发的功能以及用户提供的反馈来调整，灵活使用 system 和 prompt-text 组合可以让视觉分析更聚焦当前问题。


当需要批量分析整个文件夹的图片时，脚本会自动跳过已经生成过描述文件的图片，适合增量分析：
```bash
# 批量处理整个文件夹，每张图生成同名 .json
python local-vision/scripts/analyze_image.py --dir 素材文件夹/ --prompt game_asset
```


用户想要自行查看分析结果时可以选择输出描述文件，可以是 .json 也可以是 .md 格式，后者更适合直接阅读：
```bash 
# 保存为 .json 文件（--json）或 .md 文件（--md）
python local-vision/scripts/analyze_image.py --image 图标.png --prompt-text "描述这个图标" --json
```


## 配置

修改 `config.json` 可调整：

- `api_base` — LM Studio API 地址
- `model` — 模型名
- `timeout` — 请求超时（秒）
- `system_prompt` — 系统提示词文件路径
- `prompts` — 用户提示词文件路径映射

复制到其他项目时，只需修改 `api_base` 和 `model`。

## 注意事项

- 图片数据全程不离开本地网络
- 单张推理约 1~8 秒，调用 Bash 时需设足够 timeout
- 批量处理时已有 `.json` 的图片自动跳过，适合增量
