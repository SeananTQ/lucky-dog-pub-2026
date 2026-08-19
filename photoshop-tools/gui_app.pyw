"""PSD 导出工具 GUI（PySide6）。

多个独立页签对应常用工作流：
- 通用 PSD 导出
- LuckyDogPub 总 PSD 导出
- LuckyDogPub 装扮资源一体化导出
- 普通道具图标生成
- 狗皮肤图标生成

导出脚本和 gui_state.json 格式保持不变。窗口使用 Windows 原生标题栏，
不实现自定义鼠标拖拽逻辑。
"""

from __future__ import annotations

import json
import os
import subprocess
import threading
import traceback
from datetime import datetime

from PySide6.QtCore import QObject, Qt, Signal
from PySide6.QtGui import QFont
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QDialog,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMainWindow,
    QPlainTextEdit,
    QProgressBar,
    QPushButton,
    QTabWidget,
    QTreeWidget,
    QTreeWidgetItem,
    QVBoxLayout,
    QWidget,
)


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


class WorkerSignals(QObject):
    log = Signal(str)
    progress = Signal(int)
    finished = Signal()


class App(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("PSD 导出工具")
        self.resize(900, 720)
        self.setMinimumSize(820, 620)

        self.app_state = load_state()
        self.fields: dict[str, QWidget] = {}
        self.textboxes: dict[str, QPlainTextEdit] = {}
        self.group_labels: dict[str, QLabel] = {}
        self.excluded_groups: dict[str, list[str]] = {
            "generic": self.app_state.get("generic", {}).get("excluded_groups", []),
            "lucky": self.app_state.get("lucky", {}).get("excluded_groups", []),
        }
        self.running = False
        self.restoring_state = False
        self.signals = WorkerSignals()
        self.signals.log.connect(self._log)
        self.signals.progress.connect(self.progress_set)
        self.signals.finished.connect(self._finish_run)

        self._build_ui()
        self._restore_state()

    # UI

    def _build_ui(self):
        root = QWidget(self)
        self.setCentralWidget(root)
        root_layout = QVBoxLayout(root)
        root_layout.setContentsMargins(12, 12, 12, 12)
        root_layout.setSpacing(8)

        version_row = QHBoxLayout()
        version_label = QLabel("美术资源版本号")
        version_label.setMinimumWidth(110)
        version_row.addWidget(version_label)
        version_row.addWidget(self._line_edit("asset_version", ""), 0)
        version_row.addStretch(1)
        root_layout.addLayout(version_row)

        self.tabs = QTabWidget()
        self.tabs.addTab(self._build_generic_tab(), "通用 PSD 导出")
        self.tabs.addTab(self._build_lucky_tab(), "LuckyDogPub 总 PSD")
        self.tabs.addTab(self._build_wearable_tab(), "LuckyDogPub 装扮一体化")
        self.tabs.addTab(self._build_item_icons_tab(), "普通道具图标")
        self.tabs.addTab(self._build_dog_icons_tab(), "狗皮肤图标")
        root_layout.addWidget(self.tabs, 1)

        bottom = QFrame()
        bottom.setFrameShape(QFrame.Shape.StyledPanel)
        bottom_layout = QVBoxLayout(bottom)
        bottom_layout.setContentsMargins(10, 10, 10, 10)
        bottom_layout.setSpacing(6)

        run_row = QHBoxLayout()
        self.progress = QProgressBar()
        self.progress.setRange(0, 100)
        self.progress.setValue(0)
        run_row.addWidget(self.progress, 1)
        self.run_button = QPushButton("运行当前页签")
        self.run_button.setMinimumWidth(130)
        self.run_button.clicked.connect(self._start_current_tab)
        run_row.addWidget(self.run_button)
        clear_button = QPushButton("清空日志")
        clear_button.setMinimumWidth(90)
        clear_button.clicked.connect(self._clear_log)
        run_row.addWidget(clear_button)
        bottom_layout.addLayout(run_row)

        self.log_text = QPlainTextEdit()
        self.log_text.setReadOnly(True)
        self.log_text.setMinimumHeight(170)
        bottom_layout.addWidget(self.log_text, 1)
        root_layout.addWidget(bottom, 0)

    def _page(self) -> tuple[QWidget, QVBoxLayout]:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(12, 8, 12, 8)
        layout.setSpacing(4)
        return page, layout

    def _section(self, layout: QVBoxLayout, text: str) -> None:
        label = QLabel(text)
        label.setFont(QFont("Microsoft YaHei UI", 15, QFont.Weight.Bold))
        label.setContentsMargins(0, 6, 0, 2)
        layout.addWidget(label)

    def _build_generic_tab(self) -> QWidget:
        page, layout = self._page()
        self._section(layout, "任意 PSD 文件")
        self._file_row(layout, "generic.psd", "PSD 文件", "选择 PSD 文件")
        self._file_row(layout, "generic.out", "输出目录", "选择输出目录", directory=True)
        layout.addWidget(self._check_box("generic.crop", "裁剪空白区域", True))
        layout.addWidget(self._check_box("generic.composite", "使用合成渲染（慢，仅需图层效果时开启）", False))
        self._group_row(layout, "generic")
        layout.addStretch(1)
        return page

    def _build_lucky_tab(self) -> QWidget:
        page, layout = self._page()
        self._section(layout, "项目主 PSD 资源导出")
        self._file_row(layout, "lucky.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        self._file_row(layout, "lucky.out", "输出目录", "选择输出目录", directory=True)
        layout.addWidget(self._check_box("lucky.crop", "裁剪空白区域", True))
        layout.addWidget(self._check_box("lucky.composite", "使用合成渲染（慢12倍，仅需图层效果时开启）", False))
        self._group_row(layout, "lucky")
        note = QLabel("选择部分组时，只限制 PNG 输出；layer_index_版本号.json 仍记录完整 PSD（未填版本号时为 layer_index.json）。")
        note.setWordWrap(True)
        layout.addWidget(note)
        layout.addStretch(1)
        return page

    def _build_wearable_tab(self) -> QWidget:
        page, layout = self._page()
        self._section(layout, "装扮资源、坐标与图标一体化导出")
        self._file_row(layout, "wearable.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        self._file_row(layout, "wearable.out", "输出根目录", "选择资源输出根目录", directory=True)

        folder_row = QHBoxLayout()
        folder_label = QLabel("图标文件夹")
        folder_label.setMinimumWidth(86)
        folder_row.addWidget(folder_label)
        icon_folder = self._line_edit("wearable.icon_folder", "ItemIcon")
        icon_folder.setMaximumWidth(110)
        folder_row.addWidget(icon_folder)
        folder_row.addWidget(QLabel("版本号\\图标文件夹\\图标名.png；此页签不导出狗皮肤"))
        folder_row.addStretch(1)
        layout.addLayout(folder_row)

        layout.addWidget(self._check_box("wearable.crop", "裁剪装扮图层空白区域", True))
        settings = QGridLayout()
        settings.setHorizontalSpacing(12)
        for col, (key, label, default) in enumerate((
            ("wearable.canvas", "画布尺寸", "256"),
            ("wearable.content", "内容尺寸", "240"),
            ("wearable.margin", "边框留白", "16"),
        )):
            settings.addWidget(QLabel(label), 0, col)
            settings.addWidget(self._line_edit(key, default), 1, col)
        layout.addLayout(settings)

        layout.addWidget(QLabel("短边规则名单"))
        short_box = QPlainTextEdit()
        short_box.setPlainText("Background\nTable")
        short_box.setMaximumHeight(92)
        short_box.textChanged.connect(self._save_current_state)
        self.textboxes["wearable.short_rules"] = short_box
        layout.addWidget(short_box)
        note = QLabel("每行一个组名、图层名或路径；支持 # 注释，也兼容逗号分隔。命中后按短边缩放。")
        note.setWordWrap(True)
        layout.addWidget(note)
        layout.addStretch(1)
        return page

    def _build_item_icons_tab(self) -> QWidget:
        page, layout = self._page()
        self._section(layout, "普通道具图标生成")
        self._file_row(layout, "item_icons.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        self._file_row(layout, "item_icons.item_json", "道具表", "选择 tbitem.json")
        self._file_row(layout, "item_icons.out", "输出目录", "选择图标输出目录", directory=True)
        self._icon_settings(layout, "item_icons")
        layout.addStretch(1)
        return page

    def _build_dog_icons_tab(self) -> QWidget:
        page, layout = self._page()
        self._section(layout, "狗皮肤图标生成")
        self._file_row(layout, "dog_icons.psd", "总 PSD", "选择 LuckyDogPub 总 PSD")
        self._file_row(layout, "dog_icons.item_json", "道具表", "选择 tbitem.json")
        self._file_row(layout, "dog_icons.dog_json", "狗皮肤表", "选择 tbdogskin.json")
        self._file_row(layout, "dog_icons.out", "输出目录", "选择图标输出目录", directory=True)
        self._icon_settings(layout, "dog_icons")
        layout.addStretch(1)
        return page

    def _icon_settings(self, layout: QVBoxLayout, prefix: str) -> None:
        settings = QGridLayout()
        settings.setHorizontalSpacing(12)
        fields = (
            (f"{prefix}.canvas", "画布尺寸", "256"),
            (f"{prefix}.content", "内容尺寸", "240"),
            (f"{prefix}.margin", "边框留白", "16"),
            (f"{prefix}.short_groups", "短边缩放组", "Background,Table"),
        )
        for col, (key, label, default) in enumerate(fields):
            settings.addWidget(QLabel(label), 0, col)
            settings.addWidget(self._line_edit(key, default), 1, col)
        layout.addLayout(settings)

    def _file_row(self, layout: QVBoxLayout, key: str, label: str, title: str, directory: bool = False) -> None:
        row = QHBoxLayout()
        text_label = QLabel(label)
        text_label.setMinimumWidth(86)
        row.addWidget(text_label)
        edit = self._line_edit(key, "")
        row.addWidget(edit, 1)
        button = QPushButton("...")
        button.setMaximumWidth(38)
        button.clicked.connect(lambda: self._pick_path(key, title, directory))
        row.addWidget(button)
        layout.addLayout(row)

    def _group_row(self, layout: QVBoxLayout, mode: str) -> None:
        row = QHBoxLayout()
        label = QLabel(self._group_label_text(mode))
        self.group_labels[mode] = label
        row.addWidget(label, 1)
        button = QPushButton("选择导出组")
        button.setMinimumWidth(110)
        button.clicked.connect(lambda: self._open_group_picker(mode))
        row.addWidget(button)
        layout.addLayout(row)

    def _line_edit(self, key: str, default: str) -> QLineEdit:
        if key in self.fields:
            return self.fields[key]  # type: ignore[return-value]
        edit = QLineEdit(str(default))
        if key == "asset_version":
            edit.setPlaceholderText("例如 v1、v2 或 2026-08")
            edit.setMaximumWidth(180)
        edit.textChanged.connect(self._save_current_state)
        self.fields[key] = edit
        return edit

    def _check_box(self, key: str, label: str, default: bool) -> QCheckBox:
        if key in self.fields:
            return self.fields[key]  # type: ignore[return-value]
        box = QCheckBox(label)
        box.setChecked(default)
        box.stateChanged.connect(self._save_current_state)
        self.fields[key] = box
        return box

    # State

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
            "wearable": {
                "psd": DEFAULT_MAIN_PSD,
                "out": DEFAULT_OUTPUT,
                "icon_folder": "ItemIcon",
                "crop": True,
                "canvas": "256",
                "content": "240",
                "margin": "16",
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
                widget = self.fields.get(key)
                if widget is None:
                    continue
                value = saved.get(name, default)
                value = default if value in ("", None) else value
                if isinstance(widget, QCheckBox):
                    widget.setChecked(bool(value))
                else:
                    widget.setText(str(value))

        version = self.app_state.get("asset_version", self.app_state.get("version", ""))
        self.fields["asset_version"].setText("" if version is None else str(version))  # type: ignore[union-attr]

        short_rules = self.app_state.get("wearable", {}).get("short_rules", "Background\nTable")
        self.textboxes["wearable.short_rules"].setPlainText(str(short_rules or ""))

        for mode, label in self.group_labels.items():
            label.setText(self._group_label_text(mode))
        self.restoring_state = False
        self._save_current_state()

    def _collect_state(self) -> dict:
        return {
            "asset_version": self._get("asset_version"),
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
            "wearable": {
                "psd": self._get("wearable.psd"),
                "out": self._get("wearable.out"),
                "icon_folder": self._get("wearable.icon_folder"),
                "crop": self._get_bool("wearable.crop"),
                "canvas": self._get("wearable.canvas"),
                "content": self._get("wearable.content"),
                "margin": self._get("wearable.margin"),
                "short_rules": self._get_textbox("wearable.short_rules"),
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

    def _save_current_state(self) -> None:
        if not self.fields or self.restoring_state:
            return
        try:
            self.app_state = self._collect_state()
            save_state(self.app_state)
        except Exception:
            pass

    def _get(self, key: str) -> str:
        widget = self.fields[key]
        if isinstance(widget, QLineEdit):
            return widget.text()
        return ""

    def _get_bool(self, key: str) -> bool:
        widget = self.fields[key]
        return isinstance(widget, QCheckBox) and widget.isChecked()

    def _get_textbox(self, key: str) -> str:
        return self.textboxes[key].toPlainText()

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

        dlg = QDialog(self)
        dlg.setWindowTitle("选择导出组")
        dlg.resize(520, 600)
        dlg.setModal(True)
        dialog_layout = QVBoxLayout(dlg)

        top_count = sum(1 for g in groups if g["is_group"] and g["depth"] == 0)
        layer_count = sum(1 for g in groups if not g["is_group"])
        title = QLabel(f"PSD 组结构 ({top_count} 个顶层组, {layer_count} 个图层)")
        title.setFont(QFont("Microsoft YaHei UI", 14, QFont.Weight.Bold))
        dialog_layout.addWidget(title)
        dialog_layout.addWidget(QLabel("取消勾选 = 本次不输出该组 PNG"))

        tree = QTreeWidget()
        tree.setHeaderHidden(True)
        tree.setUniformRowHeights(True)
        dialog_layout.addWidget(tree, 1)

        excluded = set(self.excluded_groups[mode])
        path_to_item: dict[str, QTreeWidgetItem] = {}
        descendants: dict[str, list[str]] = {}
        group_items = [item for item in groups if item["is_group"]]
        for item in group_items:
            path = item["path"]
            descendants[path] = [
                child["path"]
                for child in group_items
                if child["path"].startswith(path + "/")
            ]

        stack: list[QTreeWidgetItem] = []
        for item in group_items:
            depth = item["depth"]
            stack = stack[:depth]
            parent = stack[-1] if depth > 0 and stack else tree.invisibleRootItem()
            node = QTreeWidgetItem(parent, [item["name"]])
            node.setData(0, Qt.ItemDataRole.UserRole, item["path"])
            checked = item["path"] not in excluded and item["name"] not in excluded
            node.setCheckState(0, Qt.CheckState.Checked if checked else Qt.CheckState.Unchecked)
            path_to_item[item["path"]] = node
            stack.append(node)

        picker_guard = {"value": False}

        def on_item_changed(item: QTreeWidgetItem, _column: int):
            if picker_guard["value"]:
                return
            path = item.data(0, Qt.ItemDataRole.UserRole)
            if not path:
                return
            picker_guard["value"] = True
            state = item.checkState(0)
            for child_path in descendants.get(path, []):
                child = path_to_item.get(child_path)
                if child is not None:
                    child.setCheckState(0, state)
            picker_guard["value"] = False

        tree.itemChanged.connect(on_item_changed)

        buttons = QHBoxLayout()
        all_button = QPushButton("全选")
        none_button = QPushButton("全不选")
        apply_button = QPushButton("确定")
        buttons.addWidget(all_button)
        buttons.addWidget(none_button)
        buttons.addStretch(1)
        buttons.addWidget(apply_button)
        dialog_layout.addLayout(buttons)

        def set_all(state: Qt.CheckState):
            picker_guard["value"] = True
            for item in path_to_item.values():
                item.setCheckState(0, state)
            picker_guard["value"] = False

        all_button.clicked.connect(lambda: set_all(Qt.CheckState.Checked))
        none_button.clicked.connect(lambda: set_all(Qt.CheckState.Unchecked))

        def apply():
            checked_paths = {
                path for path, item in path_to_item.items()
                if item.checkState(0) == Qt.CheckState.Checked
            }
            excluded_paths = []
            for path, item in path_to_item.items():
                if item.checkState(0) == Qt.CheckState.Checked:
                    continue
                has_checked_child = any(child.startswith(path + "/") for child in checked_paths)
                if not has_checked_child:
                    excluded_paths.append(path)
            self.excluded_groups[mode] = excluded_paths
            self.group_labels[mode].setText(self._group_label_text(mode))
            self._save_current_state()
            dlg.accept()

        apply_button.clicked.connect(apply)
        dlg.exec()

    def _group_label_text(self, mode: str) -> str:
        count = len(self.excluded_groups.get(mode, []))
        return "导出全部组" if count == 0 else f"跳过 {count} 个组"

    # Running

    def _start_current_tab(self):
        if self.running:
            return

        tab_name = self.tabs.tabText(self.tabs.currentIndex())
        if tab_name == "通用 PSD 导出":
            job = "generic"
        elif tab_name == "LuckyDogPub 总 PSD":
            job = "lucky"
        elif tab_name == "LuckyDogPub 装扮一体化":
            job = "wearable"
        elif tab_name == "普通道具图标":
            job = "item_icons"
        else:
            job = "dog_icons"

        self._save_current_state()
        job_config = self._snapshot_job(job)
        self.running = True
        self.progress_set(0)
        self.run_button.setEnabled(False)
        self.run_button.setText("运行中...")
        threading.Thread(target=lambda: self._run_job(job, job_config), daemon=True).start()

    def _snapshot_job(self, job: str) -> dict:
        if job in ("generic", "lucky"):
            return {
                "psd": self._get(f"{job}.psd"),
                "out": self._get(f"{job}.out"),
                "version": self._get("asset_version").strip(),
                "crop": self._get_bool(f"{job}.crop"),
                "composite": self._get_bool(f"{job}.composite"),
                "excluded": list(self.excluded_groups[job]),
            }
        if job == "wearable":
            return {
                "psd": self._get("wearable.psd"),
                "out": self._get("wearable.out"),
                "version": self._get("asset_version").strip(),
                "icon_folder": self._get("wearable.icon_folder").strip(),
                "crop": self._get_bool("wearable.crop"),
                "canvas": self._get("wearable.canvas"),
                "content": self._get("wearable.content"),
                "margin": self._get("wearable.margin"),
                "short_rules": self._get_textbox("wearable.short_rules"),
            }
        is_dog = job == "dog_icons"
        return {
            "psd": self._get(f"{job}.psd"),
            "item_json": self._get(f"{job}.item_json"),
            "dog_json": self._get("dog_icons.dog_json") if is_dog else "",
            "out": self._get(f"{job}.out"),
            "canvas": self._get(f"{job}.canvas"),
            "content": self._get(f"{job}.content"),
            "margin": self._get(f"{job}.margin"),
            "short_groups": split_csv(self._get(f"{job}.short_groups")),
            "is_dog": is_dog,
        }

    def _run_job(self, job: str, config: dict):
        try:
            if job in ("generic", "lucky"):
                self._run_layer_export(job, config)
            elif job == "wearable":
                self._run_wearable_export(config)
            else:
                self._run_icon_export(job, config)
            self.signals.progress.emit(100)
            self.signals.log.emit("\n--- 完成 ---")
        except Exception:
            self.signals.log.emit(traceback.format_exc())
        finally:
            self.signals.finished.emit()

    def _run_layer_export(self, mode: str, config: dict):
        psd = config["psd"]
        if not os.path.exists(psd):
            raise FileNotFoundError(f"PSD 文件不存在: {psd}")

        tmp_cfg = os.path.join(SCRIPT_DIR, f"_tmp_{mode}_export_all.json")
        with open(tmp_cfg, "w", encoding="utf-8") as f:
            json.dump({
                "psd路径": psd,
                "输出目录": config["out"],
                "美术资源版本号": config["version"],
                "裁剪空白": config["crop"],
                "使用合成渲染": config["composite"],
                "排除组": ",".join(config["excluded"]),
            }, f, ensure_ascii=False)

        title = "通用 PSD 导出" if mode == "generic" else "LuckyDogPub 总 PSD 导出"
        self.signals.log.emit(f"\n--- {title} ---")
        try:
            self._run_script("export_all_layers.py", ["--config", tmp_cfg])
            self.signals.progress.emit(85)
        finally:
            self._remove_tmp(tmp_cfg)

    def _run_wearable_export(self, config: dict):
        psd = config["psd"]
        if not os.path.exists(psd):
            raise FileNotFoundError(f"PSD 文件不存在: {psd}")
        if not config["version"]:
            raise ValueError("请先填写美术资源版本号")

        tmp_cfg = os.path.join(SCRIPT_DIR, "_tmp_wearable_export.json")
        with open(tmp_cfg, "w", encoding="utf-8") as f:
            json.dump({
                "psd路径": psd,
                "输出根目录": config["out"],
                "美术资源版本号": config["version"],
                "道具图标文件夹": config["icon_folder"],
                "裁剪空白": config["crop"],
                "画布尺寸": parse_int(config["canvas"], 256),
                "内容尺寸": parse_int(config["content"], 240),
                "边框留白": parse_int(config["margin"], 16),
                "短边规则名单": config["short_rules"],
            }, f, ensure_ascii=False)

        self.signals.log.emit("\n--- LuckyDogPub 装扮资源一体化 ---")
        try:
            self._run_script("export_wearable_assets.py", ["--config", tmp_cfg])
            self.signals.progress.emit(85)
        finally:
            self._remove_tmp(tmp_cfg)

    def _run_icon_export(self, mode: str, config: dict):
        psd = config["psd"]
        item_json = config["item_json"]
        if not os.path.exists(psd):
            raise FileNotFoundError(f"PSD 文件不存在: {psd}")
        if not os.path.exists(item_json):
            raise FileNotFoundError(f"道具表不存在: {item_json}")

        is_dog_mode = config["is_dog"]
        tmp_cfg = os.path.join(SCRIPT_DIR, f"_tmp_{mode}_export.json")
        with open(tmp_cfg, "w", encoding="utf-8") as f:
            json.dump({
                "psd路径": psd,
                "道具表路径": item_json,
                "狗皮肤表路径": config["dog_json"],
                "输出目录": config["out"],
                "画布尺寸": parse_int(config["canvas"], 256),
                "内容尺寸": parse_int(config["content"], 240),
                "边框留白": parse_int(config["margin"], 16),
                "短边缩放组": config["short_groups"],
                "导出普通道具": not is_dog_mode,
                "导出狗皮肤": is_dog_mode,
            }, f, ensure_ascii=False)

        title = "狗皮肤图标生成" if is_dog_mode else "普通道具图标生成"
        self.signals.log.emit(f"\n--- {title} ---")
        try:
            self._run_script("export_icons.py", ["--config", tmp_cfg])
            self.signals.progress.emit(85)
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
                    self.signals.log.emit(line)
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
        self.run_button.setEnabled(True)
        self.run_button.setText("运行当前页签")

    def closeEvent(self, event):
        self._save_current_state()
        event.accept()

    # Misc

    def _pick_path(self, key: str, title: str, directory: bool):
        current = self._get(key)
        initial = self._initial_dir(current)
        if directory:
            path = QFileDialog.getExistingDirectory(self, title, initial)
        else:
            path, _ = QFileDialog.getOpenFileName(
                self,
                title,
                initial,
                "PSD / JSON (*.psd *.json);;PSD (*.psd);;JSON (*.json);;所有文件 (*)",
            )
        if path:
            self.fields[key].setText(path)  # type: ignore[union-attr]
            self._save_current_state()

    def _initial_dir(self, current: str) -> str:
        if current and os.path.isdir(current):
            return current
        if current and os.path.isdir(os.path.dirname(current)):
            return os.path.dirname(current)
        return SCRIPT_DIR

    def _log(self, msg: str):
        self.log_text.appendPlainText(msg)
        scrollbar = self.log_text.verticalScrollBar()
        scrollbar.setValue(scrollbar.maximum())

    def progress_set(self, value: int):
        self.progress.setValue(max(0, min(100, int(value))))

    def _clear_log(self):
        self.log_text.clear()


def main() -> int:
    app = QApplication([])
    app.setApplicationName("PSD 导出工具")
    window = App()
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
