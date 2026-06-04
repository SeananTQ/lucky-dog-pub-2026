#!/usr/bin/env python3
"""Analyze local images using LM Studio vision model. Outputs structured JSON."""

import sys
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

import argparse
import base64
import json
import re
import time
from pathlib import Path
from urllib import request as urlreq
from urllib.error import URLError

IMAGE_EXTENSIONS = {'.png', '.jpg', '.jpeg', '.webp', '.bmp', '.gif'}

ROOT = Path(__file__).resolve().parent.parent


def load_config():
    path = ROOT / "config.json"
    if not path.exists():
        die({"error": f"config.json not found at {path}"})
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def die(obj):
    print(json.dumps(obj, ensure_ascii=False))
    sys.exit(1)


def resolve_model(config):
    model = config.get("model", "").strip()
    if model:
        return model
    try:
        req = urlreq.Request(f"{config['api_base']}/models",
                             headers={"Authorization": "Bearer not-needed"})
        resp = urlreq.urlopen(req, timeout=10)
        data = json.loads(resp.read().decode("utf-8"))
        models = data.get("data", [])
        if not models:
            die({"error": "No models loaded in LM Studio"})
        return models[0]["id"]
    except Exception as e:
        die({"error": f"Failed to get model list: {e}"})


def load_prompt(config, field, prompt_name=None):
    if field == "system_prompt":
        rel = config.get("system_prompt")
    else:
        mapping = config.get("prompts", {})
        rel = mapping.get(prompt_name or "default")
    if not rel:
        return ""
    path = ROOT / rel
    if not path.exists():
        die({"error": f"Prompt file not found: {path}"})
    with open(path, encoding="utf-8") as f:
        return f.read().strip()


def encode_image(image_path):
    with open(image_path, "rb") as f:
        data = base64.b64encode(f.read()).decode("utf-8")
    ext = Path(image_path).suffix.lower().lstrip(".")
    if ext == "jpg":
        ext = "jpeg"
    return f"data:image/{ext};base64,{data}"


def try_parse_json(text):
    """Try to extract JSON from model response. Returns dict or None."""
    m = re.search(r"```(?:json)?\s*\n?(.*?)```", text, re.DOTALL)
    if m:
        candidate = m.group(1).strip()
    else:
        candidate = text.strip()
    candidate = candidate.lstrip("```json").lstrip("```").strip()
    try:
        return json.loads(candidate)
    except json.JSONDecodeError:
        return None


def analyze_image(image_path, config, prompt_name=None, system_text=None, prompt_text=None):
    data_uri = encode_image(image_path)
    user_prompt = prompt_text if prompt_text else load_prompt(config, "prompts", prompt_name)
    system_prompt = system_text if system_text else load_prompt(config, "system_prompt")

    messages = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.append({
        "role": "user",
        "content": [
            {"type": "text", "text": user_prompt},
            {"type": "image_url", "image_url": {"url": data_uri}}
        ]
    })

    payload = {
        "model": resolve_model(config),
        "messages": messages,
        "max_tokens": config.get("max_tokens", 4096),
        "temperature": 0.1,
    }

    req = urlreq.Request(
        f"{config['api_base']}/chat/completions",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Authorization": "Bearer not-needed"
        }
    )

    try:
        resp = urlreq.urlopen(req, timeout=config.get("timeout", 300))
        result = json.loads(resp.read().decode("utf-8"))
        text = result["choices"][0]["message"]["content"].strip()
    except URLError as e:
        text = f"[API Error] {e.reason}"
    except Exception as e:
        text = f"[Error] {e}"

    # 如果模型返回了 JSON，合并到顶层，避免 JSON 套 JSON
    parsed = try_parse_json(text)
    if parsed is not None:
        ordered = {"file": Path(image_path).name}
        ordered.update(parsed)
        return ordered

    return {
        "file": Path(image_path).name,
        "description": text,
    }


def is_error_result(result):
    """Check if the model returned an empty or error response."""
    desc = result.get("description", "")
    if not desc:
        # 对于 JSON 返回，把所有字段拼起来检查
        desc = " ".join(str(v) for v in result.values() if isinstance(v, str) and v != result.get("file"))
    if not desc:
        return True
    if desc.startswith("[Error") or desc.startswith("[API Error"):
        return True
    if "我无法" in desc or "我不能" in desc or "我没有" in desc:
        return True
    return False


def save_error_file(image_path, result):
    """Save an error log as .txt next to the image."""
    out = Path(image_path).with_suffix(".txt")
    with open(out, "w", encoding="utf-8") as f:
        f.write(f"File: {result.get('file', Path(image_path).name)}\n")
        f.write(f"Error: {result.get('description', 'Empty response')}\n")
    print(f"  ERROR - saved: {out}")


def single(image_path, config, prompt_name=None, system_text=None, prompt_text=None, save_json=False, save_md=False):
    result = analyze_image(image_path, config, prompt_name, system_text, prompt_text)
    if is_error_result(result):
        save_error_file(image_path, result)
        return
    if save_json:
        out = Path(image_path).with_suffix(".json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(f"Saved: {out}")
    elif save_md:
        out = Path(image_path).with_suffix(".md")
        with open(out, "w", encoding="utf-8") as f:
            f.write(f"# {result['file']}\n\n")
            f.write(json.dumps(result, ensure_ascii=False, indent=2) if "description" in result
                    else "\n".join(f"{k}: {v}" for k, v in result.items() if k != "file"))
            f.write("\n")
        print(f"Saved: {out}")
    else:
        print(json.dumps(result, ensure_ascii=False, indent=2))


def batch(dir_path, config, prompt_name=None, system_text=None, prompt_text=None):
    dir_path = Path(dir_path)
    if not dir_path.is_dir():
        die({"error": f"Directory not found: {dir_path}"})
    images = sorted([p for p in dir_path.rglob("*") if p.suffix.lower() in IMAGE_EXTENSIONS])
    if not images:
        die({"error": f"No images found in {dir_path}"})

    processed = 0
    limit = config.get("batch_limit", 0)
    delay = config.get("batch_delay", 0) / 1000.0

    for img in images:
        out_json = img.with_suffix(".json")
        out_md = img.with_suffix(".md")
        out_txt = img.with_suffix(".txt")
        if out_json.exists() or out_md.exists() or out_txt.exists():
            continue
        if limit > 0 and processed >= limit:
            print(f"  -- 已达单批上限 {limit} 张，剩余下次继续 --")
            break
        processed += 1
        print(f"  ({processed}) {img.name}... ", end="", flush=True)
        start = time.time()
        r = analyze_image(str(img), config, prompt_name, system_text, prompt_text)
        elapsed = time.time() - start
        if is_error_result(r):
            save_error_file(str(img), r)
        else:
            with open(out_json, "w", encoding="utf-8") as f:
                json.dump(r, f, ensure_ascii=False, indent=2)
            print(f"done ({elapsed:.1f}s)")
        if delay > 0:
            time.sleep(delay)


def main():
    p = argparse.ArgumentParser(description="Analyze images using local LM Studio vision model")
    p.add_argument("--image", help="Path to a single image file")
    p.add_argument("--dir", help="Path to a directory of images")
    p.add_argument("--prompt", default="default", help="Prompt key (ignored if --prompt-text is set)")
    p.add_argument("--system", help="Override system prompt text (inline, not from file)")
    p.add_argument("--prompt-text", help="Override user prompt text (inline, not from file)")
    p.add_argument("--json", action="store_true", help="Save result as .json next to image")
    p.add_argument("--md", action="store_true", help="Save result as .md next to image")
    args = p.parse_args()

    if not args.image and not args.dir:
        p.print_help()
        print("\nError: specify --image or --dir")
        sys.exit(1)

    if args.json and args.md:
        die({"error": "Cannot use --json and --md together"})

    config = load_config()

    if args.image:
        single(args.image, config, args.prompt, args.system, args.prompt_text, args.json, args.md)
    elif args.dir:
        batch(args.dir, config, args.prompt, args.system, args.prompt_text)


if __name__ == "__main__":
    main()
