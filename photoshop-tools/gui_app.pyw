"""
PSD 导出工具 GUI

三个独立页签对应三种常用工作流：
- 通用 PSD 导出
- LuckyDogPub 总 PSD 导出
- 普通道具图标生成
- 狗皮肤图标生成

启动: python gui_app.pyw  (或双击 gui_app.bat)
"""

import json
import os
import subprocess
import threading
import traceback
from datetime import datetime
from tkinter import filedialog

import customtkinter as ctk


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPTS_DIR = os.path.join(SCRIPT_DIR, "scripts")
STATE_FILE = os.path.join(SCRIPT_DIR, "gui_state.json")

DEFAULT_MAIN_PSD = os.path.join(SCRIPT_DIR, "LuckyDogPub-Test02.psd")
DEFAULT_ITEM_JSON = os.path.join(SCRIPT_DIR, "tbitem.json")
DEFAULT_DOG_JSON = os.path.join(SCRIPT_DIR, "tbdogskin.json")
DEFAULT_OUTPUT = os.path.join(SCRIPT_DIR, "output")
DEFAULT_ICON_OUTPUT = os.path.join(SCRIPT_DIR, "output", "UI", "ItemIcon")


def load_state() -> dict:
    if not os.path.exists(STATE_FILE):
        return {}
    try:
        with open(STATE_FILE, encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def save_state(state: dict) -> None:
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=2)


def split_csv(text: str) -> list[str]:
    return [x.strip() for x in (text or "").split(",") if x.strip()]


def parse_int(value: str, default: int) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def read_psd_groups(psd_path: str) -> list[dict]:
    try:
        from psd_tools import PSDImage
        psd = PSDImage.open(psd_path)
    except Exception:
        return []

    result: list[dict] = []

    def walk(layers, depth=0, parent_path="", parent_skip=False):
        for layer in layers:
            name = layer.name
            path = f"{parent_path}/{name}" if parent_path else name
            skip = parent_skip or name.startswith("_")
            if layer.is_group():
                result.append({
                    "name": name,
                    "path": path,
                    "depth": depth,
                    "is_group": True,
                    "visible": not skip,
                })
                walk(layer, depth + 1, path, skip)
        for layer in layers:
            if not layer.is_group():
                name = layer.name
                path = f"{parent_path}/{name}" if parent_path else name
                skip = parent_skip or name.startswith("_")
                result.append({
                    "name": name,
                    "path": path,
                    "depth": depth,
                    "is_group": False,
                    "visible": not skip,
                })

    walk(psd)
    return result


class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("PSD 导出工具")
        self.geometry("900x720")
        self.minsize(820, 620)

        ctk.set_appearance_mode("system")
        ctk.set_default_color_theme("blue")

        self.app_state = load_state()
        self.vars: dict[str, ctk.Variable] = {}
        self.excluded_groups: dict[str, list[str]] = {
            "generic": self.app_state.get("generic", {}).get("excluded_groups", []),
            "lucky": self.app_state.get("lucky", {}).get("excluded_groups", []),
        }
        self.group_labels: dict[str, ctk.StringVar] = {}
        self.running = False
        self.restoring_state = False

        self._build_ui()
        self._restore_state()

    # UI

    def _build_ui(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.tabs = ctk.CTkTabview(self)
        self.tabs.grid(row=0, column=0, padx=12, pady=(12, 8), sticky="nsew")
        self.tabs.add("通用 PSD 导出")
        self.tabs.add("LuckyDogPub 总 PSD")
        self.tabs.add("普通道具图标")
        self.tabs.add("狗皮肤图标")

        self._build_generic_tab(self.tabs.tab("通用 PSD 导出"))
        self._build_lucky_tab(self.tabs.tab("LuckyDogPub 总 PSD"))
        self._build_item_icons_tab(self.tabs.tab("普通道具图标"))
        self._build_dog_icons_tab(self.tabs.tab("狗皮肤图标"))

        bottom = ctk.CTkFrame(self)
        bottom.grid(row=1, column=0, padx=12, pady=(0, 12), sticky="nsew")
        bottom.grid_columnconfigure(0, weight=1)
        bottom.grid_rowconfigure(1, weight=1)

        run_row = ctk.CTkFrame(bottom, fg_color="transparent")
        run_row.grid(row=0, column=0, padx=10, pady=(10, 6), sticky="ew")
        run_row.grid_columnconfigure(0, weight=1)

        self.progress = ctk.CTkProgressBar(run_row)
        self.progress.grid(row=0, column=0, sticky="ew", padx=(0, 8))
        self.progress.set(0)
        self.run_button = ctk.CTkButton(run_row, text="运行当前页签", width=130, command=self._start_current_tab)
        self.run_button.grid(row=0, column=1, padx=(0, 8))
        ctk.CTkButton(run_row, text="清空日志", width=90, command=self._clear_log).grid(row=0, column=2)

        self.log_text = ctk.CTkTextbox(bottom, height=170)
        self.log_text.grid(row=1, column=0, padx=10, pady=(0, 10), sticky="nsew")

    def _build_generic_tab(self, tab):
        tab.grid_columnconfigure(0, weight=1)
        row = 0
        self._section(tab, "任意 PSD 文件", row); row += 1
        row = self._file_row(tab, row, "generic.psd", "PSD 文件", "选择 PSD 文件")
        row = self._file_row(tab, row, "generic.out", "输出目录", "选择输出目录", directory=True)
        row = self._option_row(tab, row, "generic.crop", "裁剪空白区域", True)
        row = self._option_row(tab, row, "generic.composite", "使用合成渲染（慢，仅需图层效果时开启）", False)
        row = self._group_row(tab, row, "generic")

    def _build_lucky_tab(self, tab):
        tab.grid_columnconfigure(0, weight=1)
        row = 0
        self._section(tab, "项目主 PSD 资源导出", row); row += 1
        row = self._file_row(tab, row, "lucky.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        row = self._file_row(tab, row, "lucky.out", "输出目录", "选择输出目录", directory=True)
        row = self._option_row(tab, row, "lucky.crop", "裁剪空白区域", True)
        row = self._option_row(tab, row, "lucky.composite", "使用合成渲染（慢12倍，仅需图层效果时开启）", False)
        row = self._group_row(tab, row, "lucky")

        note = ctk.CTkLabel(
            tab,
            text="选择部分组时，只限制 PNG 输出；layer_index.json 仍记录完整 PSD。",
            anchor="w",
            text_color=("gray30", "gray75"),
        )
        note.grid(row=row, column=0, padx=12, pady=(8, 0), sticky="ew")

    def _build_item_icons_tab(self, tab):
        tab.grid_columnconfigure(0, weight=1)
        row = 0
        self._section(tab, "普通道具图标生成", row); row += 1
        row = self._file_row(tab, row, "item_icons.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        row = self._file_row(tab, row, "item_icons.item_json", "道具表", "选择 tbitem.json")
        row = self._file_row(tab, row, "item_icons.out", "输出目录", "选择图标输出目录", directory=True)
        self._icon_settings_grid(tab, row, "item_icons")

    def _build_dog_icons_tab(self, tab):
        tab.grid_columnconfigure(0, weight=1)
        row = 0
        self._section(tab, "狗皮肤图标生成", row); row += 1
        row = self._file_row(tab, row, "dog_icons.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        row = self._file_row(tab, row, "dog_icons.item_json", "道具表", "选择 tbitem.json")
        row = self._file_row(tab, row, "dog_icons.dog_json", "狗皮肤表", "选择 tbdogskin.json")
        row = self._file_row(tab, row, "dog_icons.out", "输出目录", "选择图标输出目录", directory=True)
        self._icon_settings_grid(tab, row, "dog_icons")

    def _icon_settings_grid(self, parent, row, prefix):
        settings = ctk.CTkFrame(parent)
        settings.grid(row=row, column=0, padx=12, pady=(10, 4), sticky="ew")
        settings.grid_columnconfigure((0, 1, 2, 3), weight=1)
        fields = [
            (f"{prefix}.canvas", "画布尺寸", "256"),
            (f"{prefix}.content", "内容尺寸", "240"),
            (f"{prefix}.margin", "边框留白", "16"),
            (f"{prefix}.short_groups", "短边缩放组", "Background,Table"),
        ]
        for col, (key, label, default) in enumerate(fields):
            ctk.CTkLabel(settings, text=label, anchor="w").grid(row=0, column=col, padx=8, pady=(8, 2), sticky="ew")
            var = self._var(key, default)
            ctk.CTkEntry(settings, textvariable=var).grid(row=1, column=col, padx=8, pady=(0, 8), sticky="ew")

    def _section(self, parent, text, row):
        label = ctk.CTkLabel(parent, text=text, font=ctk.CTkFont(size=15, weight="bold"), anchor="w")
        label.grid(row=row, column=0, padx=12, pady=(14, 6), sticky="ew")

    def _file_row(self, parent, row, key, label, title, directory=False):
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.grid(row=row, column=0, padx=12, pady=4, sticky="ew")
        frame.grid_columnconfigure(1, weight=1)

        ctk.CTkLabel(frame, text=label, width=86, anchor="w").grid(row=0, column=0, padx=(0, 8), sticky="w")
        var = self._var(key, "")
        ctk.CTkEntry(frame, textvariable=var).grid(row=0, column=1, sticky="ew", padx=(0, 6))

        def pick():
            if directory:
                path = filedialog.askdirectory(title=title, initialdir=self._initial_dir(var.get()))
            else:
                path = filedialog.askopenfilename(
                    title=title,
                    initialdir=self._initial_dir(var.get()),
                    filetypes=[("PSD / JSON", "*.psd;*.json"), ("PSD", "*.psd"), ("JSON", "*.json"), ("所有文件", "*.*")],
                )
            if path:
                var.set(path)
                self._save_current_state()

        ctk.CTkButton(frame, text="...", width=38, command=pick).grid(row=0, column=2)
        return row + 1

    def _option_row(self, parent, row, key, label, default):
        var = self._var(key, default, bool_var=True)
        ctk.CTkCheckBox(parent, text=label, variable=var, command=self._save_current_state).grid(
            row=row, column=0, padx=12, pady=4, sticky="w")
        return row + 1

    def _group_row(self, parent, row, mode):
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.grid(row=row, column=0, padx=12, pady=(8, 4), sticky="ew")
        frame.grid_columnconfigure(0, weight=1)

        label_var = ctk.StringVar(value=self._group_label_text(mode))
        self.group_labels[mode] = label_var
        ctk.CTkLabel(frame, textvariable=label_var, anchor="w").grid(row=0, column=0, sticky="ew")
        ctk.CTkButton(frame, text="选择导出组", width=110, command=lambda: self._open_group_picker(mode)).grid(row=0, column=1)
        return row + 1

    # State

    def _var(self, key: str, default, bool_var=False):
        if key in self.vars:
            return self.vars[key]
        var = ctk.BooleanVar(value=default) if bool_var else ctk.StringVar(value=str(default))
        var.trace_add("write", lambda *_: self._save_current_state())
        self.vars[key] = var
        return var

    def _restore_state(self):
        self.restoring_state = True
        defaults = {
            "generic": {
                "psd": "",
                "out": DEFAULT_OUTPUT,
                "crop": True,
                "composite": False,
            },
            "lucky": {
                "psd": DEFAULT_MAIN_PSD,
                "out": DEFAULT_OUTPUT,
                "crop": True,
                "composite": False,
            },
            "item_icons": {
                "psd": DEFAULT_MAIN_PSD,
                "item_json": DEFAULT_ITEM_JSON,
                "out": DEFAULT_ICON_OUTPUT,
                "canvas": "256",
                "content": "240",
                "margin": "16",
                "short_groups": "Background,Table",
            },
            "dog_icons": {
                "psd": DEFAULT_MAIN_PSD,
                "item_json": DEFAULT_ITEM_JSON,
                "dog_json": DEFAULT_DOG_JSON,
                "out": DEFAULT_ICON_OUTPUT,
                "canvas": "256",
                "content": "240",
                "margin": "16",
                "short_groups": "Background,Table",
            },
        }

        if "icons" in self.app_state:
            old_icons = self.app_state["icons"]
            for section in ("item_icons", "dog_icons"):
                migrated = defaults[section].copy()
                migrated.update({k: v for k, v in old_icons.items() if k in migrated})
                defaults[section] = migrated

        for section, values in defaults.items():
            saved = self.app_state.get(section, {})
            for name, default in values.items():
                key = f"{section}.{name}"
                if key not in self.vars:
                    continue
                value = saved.get(name, default)
                self.vars[key].set(default if value in ("", None) else value)

        for mode, label in self.group_labels.items():
            label.set(self._group_label_text(mode))
        self.restoring_state = False
        self._save_current_state()

    def _collect_state(self) -> dict:
        return {
            "generic": {
                "psd": self._get("generic.psd"),
                "out": self._get("generic.out"),
                "crop": self._get_bool("generic.crop"),
                "composite": self._get_bool("generic.composite"),
                "excluded_groups": self.excluded_groups["generic"],
            },
            "lucky": {
                "psd": self._get("lucky.psd"),
                "out": self._get("lucky.out"),
                "crop": self._get_bool("lucky.crop"),
                "composite": self._get_bool("lucky.composite"),
                "excluded_groups": self.excluded_groups["lucky"],
            },
            "item_icons": {
                "psd": self._get("item_icons.psd"),
                "item_json": self._get("item_icons.item_json"),
                "out": self._get("item_icons.out"),
                "canvas": self._get("item_icons.canvas"),
                "content": self._get("item_icons.content"),
                "margin": self._get("item_icons.margin"),
                "short_groups": self._get("item_icons.short_groups"),
            },
            "dog_icons": {
                "psd": self._get("dog_icons.psd"),
                "item_json": self._get("dog_icons.item_json"),
                "dog_json": self._get("dog_icons.dog_json"),
                "out": self._get("dog_icons.out"),
                "canvas": self._get("dog_icons.canvas"),
                "content": self._get("dog_icons.content"),
                "margin": self._get("dog_icons.margin"),
                "short_groups": self._get("dog_icons.short_groups"),
            },
            "updated_at": datetime.now().isoformat(timespec="seconds"),
        }

    def _save_current_state(self):
        if not hasattr(self, "vars") or getattr(self, "restoring_state", False):
            return
        try:
            self.app_state = self._collect_state()
            save_state(self.app_state)
        except Exception:
            pass

    def _get(self, key: str) -> str:
        return self.vars[key].get()

    def _get_bool(self, key: str) -> bool:
        return bool(self.vars[key].get())

    # Group picker

    def _open_group_picker(self, mode: str):
        psd = self._get(f"{mode}.psd")
        if not psd or not os.path.exists(psd):
            self._log("请先选择有效的 PSD 文件")
            return

        groups = read_psd_groups(psd)
        if not groups:
            self._log("无法读取 PSD 组结构")
            return

        dlg = ctk.CTkToplevel(self)
        dlg.title("选择导出组")
        dlg.geometry("520x600")
        dlg.transient(self)
        dlg.grab_set()

        top_count = sum(1 for g in groups if g["is_group"] and g["depth"] == 0)
        layer_count = sum(1 for g in groups if not g["is_group"])
        ctk.CTkLabel(
            dlg,
            text=f"PSD 组结构 ({top_count} 个顶层组, {layer_count} 个图层)",
            font=ctk.CTkFont(size=14, weight="bold"),
        ).pack(padx=12, pady=(12, 4))
        ctk.CTkLabel(dlg, text="取消勾选 = 本次不输出该组 PNG").pack(padx=12, pady=(0, 8))

        scroll = ctk.CTkScrollableFrame(dlg, height=430)
        scroll.pack(fill="both", expand=True, padx=12, pady=4)

        excluded = set(self.excluded_groups[mode])
        check_vars: dict[str, ctk.BooleanVar] = {}

        def descendants_of(index: int, depth: int) -> list[str]:
            result = []
            for item in groups[index + 1:]:
                if item["depth"] <= depth:
                    break
                if item["is_group"]:
                    result.append(item["path"])
            return result

        def set_descendants(paths: list[str], value: bool):
            for path in paths:
                if path in check_vars:
                    check_vars[path].set(value)

        for index, item in enumerate(groups):
            if not item["is_group"]:
                continue
            path = item["path"]
            var = ctk.BooleanVar(value=path not in excluded and item["name"] not in excluded)
            check_vars[path] = var
            child_paths = descendants_of(index, item["depth"])
            text = f"{'  ' * item['depth']}{item['name']}"
            ctk.CTkCheckBox(
                scroll,
                text=text,
                variable=var,
                command=lambda v=var, children=child_paths: set_descendants(children, v.get()),
            ).pack(anchor="w", padx=4, pady=2)

        buttons = ctk.CTkFrame(dlg, fg_color="transparent")
        buttons.pack(fill="x", padx=12, pady=(8, 12))

        def apply():
            checked_paths = {path for path, var in check_vars.items() if var.get()}
            excluded_paths = []
            for path, var in check_vars.items():
                if var.get():
                    continue
                has_checked_child = any(child.startswith(path + "/") for child in checked_paths)
                if not has_checked_child:
                    excluded_paths.append(path)
            self.excluded_groups[mode] = excluded_paths
            self.group_labels[mode].set(self._group_label_text(mode))
            self._save_current_state()
            dlg.destroy()

        ctk.CTkButton(buttons, text="全选", width=80, command=lambda: [v.set(True) for v in check_vars.values()]).pack(side="left")
        ctk.CTkButton(buttons, text="全不选", width=80, command=lambda: [v.set(False) for v in check_vars.values()]).pack(side="left", padx=8)
        ctk.CTkButton(buttons, text="确定", width=90, command=apply).pack(side="right")

    def _group_label_text(self, mode: str) -> str:
        count = len(self.excluded_groups.get(mode, []))
        return "导出全部组" if count == 0 else f"跳过 {count} 个组"

    # Running

    def _start_current_tab(self):
        if self.running:
            return
        tab_name = self.tabs.get()
        if tab_name == "通用 PSD 导出":
            job = "generic"
        elif tab_name == "LuckyDogPub 总 PSD":
            job = "lucky"
        elif tab_name == "普通道具图标":
            job = "item_icons"
        else:
            job = "dog_icons"

        self._save_current_state()
        self.running = True
        self.progress.set(0)
        self.run_button.configure(state="disabled", text="运行中...")
        threading.Thread(target=lambda: self._run_job(job), daemon=True).start()

    def _run_job(self, job: str):
        try:
            if job in ("generic", "lucky"):
                self._run_layer_export(job)
            else:
                self._run_icon_export(job)
            self._after_progress(1)
            self._after_log("\n--- 完成 ---")
        except Exception:
            self._after_log(traceback.format_exc())
        finally:
            self.after(0, self._finish_run)

    def _run_layer_export(self, mode: str):
        psd = self._get(f"{mode}.psd")
        if not os.path.exists(psd):
            raise FileNotFoundError(f"PSD 文件不存在: {psd}")

        tmp_cfg = os.path.join(SCRIPT_DIR, f"_tmp_{mode}_export_all.json")
        with open(tmp_cfg, "w", encoding="utf-8") as f:
            json.dump({
                "psd路径": psd,
                "输出目录": self._get(f"{mode}.out"),
                "裁剪空白": self._get_bool(f"{mode}.crop"),
                "使用合成渲染": self._get_bool(f"{mode}.composite"),
                "排除组": ",".join(self.excluded_groups[mode]),
            }, f, ensure_ascii=False)

        title = "通用 PSD 导出" if mode == "generic" else "LuckyDogPub 总 PSD 导出"
        self._after_log(f"\n--- {title} ---")
        try:
            self._run_script("export_all_layers.py", ["--config", tmp_cfg])
            self._after_progress(0.85)
        finally:
            self._remove_tmp(tmp_cfg)

    def _run_icon_export(self, mode: str):
        psd = self._get(f"{mode}.psd")
        item_json = self._get(f"{mode}.item_json")
        if not os.path.exists(psd):
            raise FileNotFoundError(f"PSD 文件不存在: {psd}")
        if not os.path.exists(item_json):
            raise FileNotFoundError(f"道具表不存在: {item_json}")

        is_dog_mode = mode == "dog_icons"
        tmp_cfg = os.path.join(SCRIPT_DIR, f"_tmp_{mode}_export.json")
        with open(tmp_cfg, "w", encoding="utf-8") as f:
            json.dump({
                "psd路径": psd,
                "道具表路径": item_json,
                "狗皮肤表路径": self._get("dog_icons.dog_json") if is_dog_mode else "",
                "输出目录": self._get(f"{mode}.out"),
                "画布尺寸": parse_int(self._get(f"{mode}.canvas"), 256),
                "内容尺寸": parse_int(self._get(f"{mode}.content"), 240),
                "边框留白": parse_int(self._get(f"{mode}.margin"), 16),
                "短边缩放组": split_csv(self._get(f"{mode}.short_groups")),
                "导出普通道具": not is_dog_mode,
                "导出狗皮肤": is_dog_mode,
            }, f, ensure_ascii=False)

        title = "狗皮肤图标生成" if is_dog_mode else "普通道具图标生成"
        self._after_log(f"\n--- {title} ---")
        try:
            self._run_script("export_icons.py", ["--config", tmp_cfg])
            self._after_progress(0.85)
        finally:
            self._remove_tmp(tmp_cfg)

    def _run_script(self, script_name: str, args: list[str]):
        script_path = os.path.join(SCRIPTS_DIR, script_name)
        cmd = ["python", script_path] + args
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONUTF8"] = "1"
        proc = subprocess.Popen(
            cmd,
            cwd=SCRIPT_DIR,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if proc.stdout:
            for line in proc.stdout:
                line = line.strip()
                if line:
                    self._after_log(line)
        proc.wait()
        if proc.returncode != 0:
            raise RuntimeError(f"{script_name} 运行失败，退出码 {proc.returncode}")

    def _remove_tmp(self, path: str):
        try:
            os.remove(path)
        except OSError:
            pass

    def _finish_run(self):
        self.running = False
        self.run_button.configure(state="normal", text="运行当前页签")

    # Misc

    def _initial_dir(self, current: str) -> str:
        if current and os.path.isdir(current):
            return current
        if current and os.path.isdir(os.path.dirname(current)):
            return os.path.dirname(current)
        return SCRIPT_DIR

    def _log(self, msg: str):
        self.log_text.insert("end", msg + "\n")
        self.log_text.see("end")

    def _after_log(self, msg: str):
        self.after(0, lambda: self._log(msg))

    def _after_progress(self, value: float):
        self.after(0, lambda: self.progress.set(value))

    def _clear_log(self):
        self.log_text.delete("1.0", "end")


def main():
    app = App()
    app.mainloop()


if __name__ == "__main__":
    main()
