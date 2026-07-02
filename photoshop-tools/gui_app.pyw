"""
PSD 导出工具 GUI
CustomTkinter 界面，支持文件选择、多套配置、历史记录。

启动: python gui_app.pyw  (或双击 gui_app.bat)
"""

import json, os, sys, subprocess, threading
from datetime import datetime
import customtkinter as ctk
from tkinter import filedialog

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPTS_DIR = os.path.join(SCRIPT_DIR, "scripts")
CONFIG_FILE = os.path.join(SCRIPT_DIR, "gui_config_history.json")

# ─── 读取 PSD 组结构 ──────────────────────────────────────

def read_psd_groups(psd_path: str) -> list:
    """读取 PSD 的顶层组及其子组结构，返回 [(name, is_group, depth, visible)]"""
    try:
        from psd_tools import PSDImage
        psd = PSDImage.open(psd_path)
    except:
        return []

    result = []
    def walk(layers, depth=0, parent_skip=False):
        for l in layers:
            name = l.name
            is_skip = name.startswith("_") or parent_skip
            if l.is_group():
                result.append((name, True, depth, not is_skip))
                walk(l, depth + 1, is_skip)
        for l in layers:
            if not l.is_group():
                result.append((l.name, False, depth, not (l.name.startswith("_") or parent_skip)))

    walk(psd)
    return result


# ─── 配置历史管理 ──────────────────────────────────────────

def load_history():
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, encoding="utf-8") as f:
                return json.load(f)
        except:
            pass
    return []


def save_history(history: list):
    with open(CONFIG_FILE, "w", encoding="utf-8") as f:
        json.dump(history, f, ensure_ascii=False, indent=2)


# ─── 辅助 ──────────────────────────────────────────────────

def _section_label(parent, text, row, col=0, colspan=1):
    lbl = ctk.CTkLabel(parent, text=text, font=ctk.CTkFont(weight="bold", size=13))
    lbl.grid(row=row, column=col, columnspan=colspan, padx=12, pady=(14, 4), sticky="w")
    return lbl


def _file_row(parent, row, var, title, directory=False):
    frame = ctk.CTkFrame(parent, fg_color="transparent")
    frame.grid(row=row, column=0, padx=12, pady=2, sticky="ew", columnspan=2)
    frame.grid_columnconfigure(0, weight=1)
    entry = ctk.CTkEntry(frame, textvariable=var)
    entry.grid(row=0, column=0, sticky="ew", padx=(0, 6))

    def picker():
        if directory:
            path = filedialog.askdirectory(title=title)
        else:
            path = filedialog.askopenfilename(title=title, filetypes=[("支持的文件", "*.psd;*.json"), ("PSD", "*.psd"), ("JSON", "*.json")])
        if path:
            var.set(path)

    ctk.CTkButton(frame, text="...", width=36, command=picker).grid(row=0, column=1)


def _checkbox(parent, row, text, var):
    cb = ctk.CTkCheckBox(parent, text=text, variable=var)
    cb.grid(row=row, column=0, padx=12, pady=2, sticky="w")
    return cb


# ─── 主窗口 ─────────────────────────────────────────────────

class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("PSD 导出工具")
        self.geometry("640x800")
        self.resizable(True, True)

        self.history = load_history()

        ctk.set_appearance_mode("system")
        ctk.set_default_color_theme("blue")

        self._build_ui()
        self._refresh_profile_list()

    # ── UI 布局 ──────────────────────────────────────────

    def _build_ui(self):
        self.grid_columnconfigure(0, weight=1)
        self.grid_columnconfigure(1, weight=1)
        row = 0

        # ═══ 配置文件 ═══
        _section_label(self, "配置管理", row); row += 1

        self.profile_var = ctk.StringVar()
        combo_frame = ctk.CTkFrame(self, fg_color="transparent")
        combo_frame.grid(row=row, column=0, columnspan=2, padx=12, pady=2, sticky="ew")
        combo_frame.grid_columnconfigure(0, weight=1)
        self.profile_combo = ctk.CTkOptionMenu(
            combo_frame, values=["新建配置..."], variable=self.profile_var,
            command=self._on_profile_change)
        self.profile_combo.grid(row=0, column=0, sticky="ew", padx=(0, 6))

        btn_row = ctk.CTkFrame(self, fg_color="transparent")
        btn_row.grid(row=row + 1, column=0, columnspan=2, padx=12, pady=4, sticky="ew")
        ctk.CTkButton(btn_row, text="保存配置", width=80, command=self._save_profile).pack(side="left", padx=(0, 6))
        ctk.CTkButton(btn_row, text="删除配置", width=80, command=self._delete_profile).pack(side="left")
        row += 2

        # ═══ PSD 文件 ═══
        _section_label(self, "PSD 文件", row); row += 1
        self.psd_var = ctk.StringVar()
        _file_row(self, row, self.psd_var, "选择 PSD 文件")
        row += 1

        # ═══ 全量图层导出 ═══
        _section_label(self, "全量图层导出", row); row += 1
        self.out_all_var = ctk.StringVar(value="output")
        _file_row(self, row, self.out_all_var, "选择输出目录", directory=True)
        row += 1

        self.crop_var = ctk.BooleanVar(value=True)
        self.composite_var = ctk.BooleanVar(value=False)
        _checkbox(self, row, "裁剪空白区域", self.crop_var); row += 1
        _checkbox(self, row, "使用合成渲染（慢12倍，仅需图层效果时开启）", self.composite_var); row += 1

        self.excluded_groups = []
        self.group_label_var = ctk.StringVar(value="未加载 PSD")
        group_frame = ctk.CTkFrame(self, fg_color="transparent")
        group_frame.grid(row=row, column=0, columnspan=2, padx=12, pady=2, sticky="ew")
        ctk.CTkLabel(group_frame, textvariable=self.group_label_var, font=ctk.CTkFont(size=11)).pack(side="left")
        ctk.CTkButton(group_frame, text="选择导出组...", width=110, command=self._open_group_picker).pack(side="right")
        row += 1

        # ═══ 道具图标导出 ═══
        _section_label(self, "道具图标导出", row); row += 1

        self.item_json_var = ctk.StringVar()
        _file_row(self, row, self.item_json_var, "选择道具表 (tbitem.json)")
        row += 1

        self.dog_json_var = ctk.StringVar()
        _file_row(self, row, self.dog_json_var, "选择狗皮肤表 (tbdogskin.json)")
        row += 1

        self.out_icons_var = ctk.StringVar(value="output")
        _file_row(self, row, self.out_icons_var, "选择输出目录", directory=True)
        row += 1

        # 图标尺寸参数
        size_frame = ctk.CTkFrame(self, fg_color="transparent")
        size_frame.grid(row=row, column=0, columnspan=2, padx=12, pady=(4, 4), sticky="ew")
        size_frame.grid_columnconfigure((0, 1, 2, 3), weight=1)

        self.canvas_var = ctk.StringVar(value="256")
        self.content_var = ctk.StringVar(value="240")
        self.margin_var = ctk.StringVar(value="16")
        self.short_types_var = ctk.StringVar(value="6,7")

        for i, (label, var) in enumerate([
            ("画布尺寸", self.canvas_var), ("内容尺寸", self.content_var),
            ("边框留白", self.margin_var), ("短边缩放组", self.short_types_var),
        ]):
            ctk.CTkLabel(size_frame, text=label, font=ctk.CTkFont(size=11)).grid(row=0, column=i, padx=(0, 4), sticky="w")
            ctk.CTkEntry(size_frame, width=72, textvariable=var).grid(row=1, column=i, padx=(0, 8), sticky="w")
        row += 1

        self.dog_export_var = ctk.BooleanVar(value=True)
        _checkbox(self, row, "导出狗皮肤图标", self.dog_export_var); row += 1

        # ═══ 执行 ═══
        ctk.CTkFrame(self, height=1, fg_color="gray30").grid(row=row, column=0, columnspan=2, padx=12, pady=(8, 8), sticky="ew")
        row += 1

        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.grid(row=row, column=0, columnspan=2, padx=12, pady=(0, 4), sticky="ew")
        self.progress = ctk.CTkProgressBar(btn_frame)
        self.progress.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.progress.set(0)
        self.export_btn = ctk.CTkButton(btn_frame, text="一键导出", width=100, command=self._start_export)
        self.export_btn.pack(side="right")
        row += 1

        # 日志
        self.log_text = ctk.CTkTextbox(self, height=200)
        self.log_text.grid(row=row, column=0, columnspan=2, padx=12, pady=(4, 12), sticky="nsew")
        self.grid_rowconfigure(row, weight=1)

    # ── 配置操作 ───────────────────────────────────────

    def _refresh_profile_list(self):
        names = [h.get("name", "未命名") for h in self.history] or ["新建配置..."]
        self.profile_combo.configure(values=names)

    def _on_profile_change(self, *args):
        name = self.profile_var.get()
        if not name or name == "新建配置...":
            return
        for h in self.history:
            if h.get("name") == name:
                self._load_config(h)
                return

    def _save_profile(self):
        name = self.profile_var.get()
        if not name or name == "新建配置...":
            name = datetime.now().strftime("%Y-%m-%d %H:%M")
        cfg = self._dump_config()
        cfg["name"] = name
        self.history = [h for h in self.history if h.get("name") != name]
        self.history.insert(0, cfg)
        save_history(self.history)
        self._refresh_profile_list()
        self.profile_var.set(name)
        self._log(f"已保存: {name}")

    def _delete_profile(self):
        name = self.profile_var.get()
        if not name or name == "新建配置...":
            return
        self.history = [h for h in self.history if h.get("name") != name]
        save_history(self.history)
        self._refresh_profile_list()
        self._log(f"已删除: {name}")

    def _load_config(self, cfg: dict):
        self.psd_var.set(cfg.get("psd_path", ""))
        self.out_all_var.set(cfg.get("out_all", "output"))
        self.crop_var.set(cfg.get("crop_blank", True))
        self.composite_var.set(cfg.get("use_composite", False))
        self.item_json_var.set(cfg.get("item_json", ""))
        self.dog_json_var.set(cfg.get("dog_json", ""))
        self.out_icons_var.set(cfg.get("out_icons", "output"))
        self.canvas_var.set(str(cfg.get("canvas_size", 256)))
        self.content_var.set(str(cfg.get("content_size", 240)))
        self.margin_var.set(str(cfg.get("margin", 16)))
        self.short_types_var.set(",".join(str(x) for x in cfg.get("short_types", [6, 7])))
        self.dog_export_var.set(cfg.get("export_dogs", True))

    def _dump_config(self):
        exclude = ",".join(self._excluded_groups) if hasattr(self, '_excluded_groups') else ""
        return {
            "psd_path": self.psd_var.get(),
            "out_all": self.out_all_var.get(),
            "crop_blank": self.crop_var.get(),
            "use_composite": self.composite_var.get(),
            "exclude_groups": exclude,
            "item_json": self.item_json_var.get(),
            "dog_json": self.dog_json_var.get(),
            "out_icons": self.out_icons_var.get(),
            "canvas_size": int(self.canvas_var.get() or 256),
            "content_size": int(self.content_var.get() or 240),
            "margin": int(self.margin_var.get() or 16),
            "short_types": [int(x.strip()) for x in self.short_types_var.get().split(",") if x.strip().isdigit()],
            "export_dogs": self.dog_export_var.get(),
            "saved_at": datetime.now().isoformat(),
        }

    def _log(self, msg: str):
        self.log_text.insert("end", msg + "\n")
        self.log_text.see("end")

    # ── 组选择弹窗 ────────────────────────────────────

    def _open_group_picker(self):
        psd = self.psd_var.get()
        if not psd or not os.path.exists(psd):
            self._log("请先选择 PSD 文件")
            return

        groups = read_psd_groups(psd)
        if not groups:
            self._log("无法读取 PSD 组结构")
            return

        # 统计顶层组
        top_groups = [g for g in groups if g[1] and g[2] == 0]  # (name, True, depth=0, .)
        total_layers = len([g for g in groups if not g[1]])  # non-group layers

        # 弹窗
        dlg = ctk.CTkToplevel(self)
        dlg.title("选择导出组")
        dlg.geometry("420x520")
        dlg.transient(self)
        dlg.grab_set()

        ctk.CTkLabel(dlg, text=f"PSD 组结构 ({len(top_groups)} 个组, {total_layers} 个图层)",
                     font=ctk.CTkFont(weight="bold")).pack(padx=12, pady=(12, 4))
        ctk.CTkLabel(dlg, text="取消勾选 = 跳过不导出", font=ctk.CTkFont(size=11)).pack(padx=12)

        # 滚动区域
        scroll = ctk.CTkScrollableFrame(dlg, height=380)
        scroll.pack(fill="both", expand=True, padx=12, pady=8)

        check_vars = {}  # name -> BooleanVar
        is_excluded = set(self.excluded_groups)

        def toggle_group(name, var, child_names):
            """切换组时，联动所有子项"""
            for cn in child_names:
                if cn in check_vars:
                    check_vars[cn].set(var.get())

        # 构建 checkboxes — 只显示组，缩进表示层级
        idx = 0
        for name, is_group, depth, _ in groups:
            if not is_group:
                continue
            var = ctk.BooleanVar(value=name not in is_excluded)
            check_vars[name] = var

            # 找出这个组的所有后代
            descendants = set()
            for j in range(idx + 1, len(groups)):
                d_name, d_is_group, d_depth, _ = groups[j]
                if d_depth <= depth:
                    break
                descendants.add(d_name)

            cb = ctk.CTkCheckBox(
                scroll, text=("  " * depth) + name, variable=var,
                command=lambda n=name, v=var, d=descendants: toggle_group(n, v, d))
            cb.pack(anchor="w", pady=1)
            idx += 1

        # 按钮行
        btn_row = ctk.CTkFrame(dlg, fg_color="transparent")
        btn_row.pack(fill="x", padx=12, pady=(8, 12))

        def apply_selection():
            self.excluded_groups = [n for n, v in check_vars.items() if not v.get()]
            self.group_label_var.set(f"跳过 {len(self.excluded_groups)} 个组"
                                     if self.excluded_groups else "全部导出")
            dlg.destroy()

        ctk.CTkButton(btn_row, text="全选", width=70, command=lambda: [v.set(True) for v in check_vars.values()]).pack(side="left", padx=(0, 6))
        ctk.CTkButton(btn_row, text="全不选", width=70, command=lambda: [v.set(False) for v in check_vars.values()]).pack(side="left")
        ctk.CTkButton(btn_row, text="确定", width=70, command=apply_selection).pack(side="right")

    # ── 导出 ──────────────────────────────────────────

    def _start_export(self):
        self.export_btn.configure(state="disabled", text="导出中...")
        self.progress.set(0)
        threading.Thread(target=self._do_export, daemon=True).start()

    def _do_export(self):
        tmp_cfg = tmp_cfg2 = None
        try:
            psd = self.psd_var.get()
            if not psd or not os.path.exists(psd):
                self._after_log("错误: PSD 文件不存在")
                return

            # ── 全量图层导出 ──
            tmp_cfg = os.path.join(SCRIPT_DIR, "_tmp_export_all.json")
            os.makedirs(os.path.dirname(tmp_cfg), exist_ok=True)
            exclude_str = ",".join(getattr(self, "_excluded_groups", []))
            with open(tmp_cfg, "w", encoding="utf-8") as f:
                json.dump({
                    "psd路径": psd,
                    "输出目录": self.out_all_var.get(),
                    "裁剪空白": self.crop_var.get(),
                    "使用合成渲染": self.composite_var.get(),
                    "排除组": exclude_str,
                }, f, ensure_ascii=False)

            self._after_log(f"--- 全量图层导出 ---")
            self._run_script("export_all_layers.py", ["--config", tmp_cfg])
            self._after_progress(0.4)

            # ── 道具图标导出 ──
            item_json = self.item_json_var.get()
            if os.path.exists(item_json):
                self._after_log(f"\n--- 道具图标导出 ---")
                tmp_cfg2 = os.path.join(SCRIPT_DIR, "_tmp_export_icons.json")
                with open(tmp_cfg2, "w", encoding="utf-8") as f:
                    json.dump({
                        "psd路径": psd,
                        "道具表路径": item_json,
                        "狗皮肤表路径": self.dog_json_var.get() if self.dog_export_var.get() else "",
                        "输出目录": self.out_icons_var.get(),
                        "画布尺寸": int(self.canvas_var.get() or 256),
                        "内容尺寸": int(self.content_var.get() or 240),
                        "边框留白": int(self.margin_var.get() or 16),
                        "短边缩放组": [int(x.strip()) for x in self.short_types_var.get().split(",") if x.strip().isdigit()],
                    }, f, ensure_ascii=False)
                self._run_script("export_icons.py", [tmp_cfg2])
                self._after_progress(0.8)

            # 清理临时文件
            for tf in (tmp_cfg, tmp_cfg2):
                if tf:
                    try: os.remove(tf)
                    except: pass

            self._after_log("\n--- 全部完成 ---")
            self._after_progress(1.0)
        except Exception as e:
            import traceback
            self._after_log(traceback.format_exc())
        finally:
            self._after_enable_btn()

    def _run_script(self, script_name: str, args: list):
        """运行 scripts/ 下的脚本，实时输出日志"""
        script_path = os.path.join(SCRIPTS_DIR, script_name)
        cmd = ["python", script_path] + args
        try:
            proc = subprocess.Popen(
                cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, encoding="utf-8", errors="replace", cwd=SCRIPT_DIR)
            for line in proc.stdout:
                line = line.strip()
                if line:
                    self._after_log(line)
            proc.wait()
        except Exception as e:
            self._after_log(f"运行失败: {e}")

    def _after_log(self, msg):
        self.after(0, lambda: self._log(msg))

    def _after_progress(self, val):
        self.after(0, lambda: self.progress.set(val))

    def _after_enable_btn(self):
        self.after(0, lambda: self.export_btn.configure(state="normal", text="一键导出"))


def main():
    app = App()
    if app.history:
        app.profile_var.set(app.history[0].get("name", ""))
    app.mainloop()


if __name__ == "__main__":
    main()
