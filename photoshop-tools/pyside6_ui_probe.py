"""PySide6 UI probe for high-polling mouse/title-bar drag validation.

This is intentionally independent from gui_app.pyw. It contains no exporter
logic and uses the default Windows title bar; do not add custom drag handling
here, because the purpose is to test Qt's native window behavior.
"""

from __future__ import annotations

import sys
from datetime import datetime

from PySide6.QtCore import Qt
from PySide6.QtGui import QFont
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QFormLayout,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMainWindow,
    QPlainTextEdit,
    QProgressBar,
    QPushButton,
    QTabWidget,
    QVBoxLayout,
    QWidget,
)


class UiProbeWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("PSD 导出工具 - PySide6 验证")
        self.resize(900, 720)
        self.setMinimumSize(820, 620)

        # Keep the normal Windows non-client area/title bar. The probe must
        # exercise the OS title-bar drag path, not a custom Qt drag routine.
        self._build_ui()
        self._log("验证窗口已启动")
        self._log("测试：切换任意页签后，拖动标题栏观察鼠标延迟/惯性")

    def _build_ui(self) -> None:
        root = QWidget(self)
        self.setCentralWidget(root)
        root_layout = QVBoxLayout(root)
        root_layout.setContentsMargins(12, 12, 12, 12)
        root_layout.setSpacing(8)

        version_row = QHBoxLayout()
        version_row.addWidget(QLabel("美术资源版本号"))
        version_row.addWidget(QLineEdit("v2"))
        version_row.addStretch(1)
        root_layout.addLayout(version_row)

        self.tabs = QTabWidget()
        self.tabs.currentChanged.connect(self._on_tab_changed)
        for name, builder in (
            ("通用 PSD 导出", self._build_generic_page),
            ("LuckyDogPub 总 PSD", self._build_lucky_page),
            ("LuckyDogPub 装扮一体化", self._build_wearable_page),
            ("普通道具图标", self._build_item_icon_page),
            ("狗皮肤图标", self._build_dog_icon_page),
        ):
            page = QWidget()
            builder(page)
            self.tabs.addTab(page, name)
        root_layout.addWidget(self.tabs, 1)

        bottom = QGroupBox()
        bottom_layout = QVBoxLayout(bottom)
        run_row = QHBoxLayout()
        self.progress = QProgressBar()
        self.progress.setRange(0, 100)
        self.progress.setValue(0)
        run_row.addWidget(self.progress, 1)
        self.run_button = QPushButton("运行当前页签")
        self.run_button.clicked.connect(self._simulate_run)
        run_row.addWidget(self.run_button)
        clear_button = QPushButton("清空日志")
        clear_button.clicked.connect(self._clear_log)
        run_row.addWidget(clear_button)
        bottom_layout.addLayout(run_row)

        self.log = QPlainTextEdit()
        self.log.setReadOnly(True)
        self.log.setMinimumHeight(170)
        bottom_layout.addWidget(self.log)
        root_layout.addWidget(bottom)

    @staticmethod
    def _base_form_layout(title: str) -> QFormLayout:
        layout = QFormLayout()
        heading = QLabel(title)
        heading.setFont(QFont("Microsoft YaHei UI", 15, QFont.Weight.Bold))
        layout.addRow(heading)
        layout.addRow("PSD 文件", QLineEdit())
        layout.addRow("输出目录", QLineEdit())
        layout.addRow("裁剪空白区域", QCheckBox())
        return layout

    def _build_generic_page(self, page: QWidget) -> None:
        page.setLayout(self._base_form_layout("任意 PSD 文件"))
        page.layout().addRow("合成渲染", QCheckBox())
        page.layout().addRow("导出组", QLineEdit())

    def _build_lucky_page(self, page: QWidget) -> None:
        page.setLayout(self._base_form_layout("项目主 PSD 资源导出"))
        page.layout().addRow("合成渲染", QCheckBox())
        page.layout().addRow("导出组", QLineEdit())
        page.layout().addRow("说明", QLabel("layer_index.json 验证页"))

    def _build_wearable_page(self, page: QWidget) -> None:
        layout = QVBoxLayout(page)
        form = QFormLayout()
        form.addRow(QLabel("装扮资源、坐标与图标一体化导出"))
        form.addRow("总 PSD", QLineEdit())
        form.addRow("输出根目录", QLineEdit())
        form.addRow("图标文件夹", QLineEdit("ItemIcon"))
        form.addRow("裁剪空白", QCheckBox())
        layout.addLayout(form)
        settings = QGridLayout()
        for column, label, value in (
            (0, "画布尺寸", "256"),
            (1, "内容尺寸", "240"),
            (2, "边框留白", "16"),
        ):
            settings.addWidget(QLabel(label), 0, column)
            settings.addWidget(QLineEdit(value), 1, column)
        layout.addLayout(settings)
        layout.addWidget(QLabel("短边规则名单"))
        layout.addWidget(QPlainTextEdit("Background\nTable"))
        layout.addWidget(QLabel("每行一个组名、图层名或路径"))

    def _build_item_icon_page(self, page: QWidget) -> None:
        layout = QFormLayout(page)
        layout.addRow(QLabel("普通道具图标生成"))
        layout.addRow("总 PSD", QLineEdit())
        layout.addRow("道具表", QLineEdit())
        layout.addRow("输出目录", QLineEdit())
        layout.addRow("画布尺寸", QLineEdit("256"))
        layout.addRow("内容尺寸", QLineEdit("240"))
        layout.addRow("短边缩放组", QLineEdit("Background,Table"))

    def _build_dog_icon_page(self, page: QWidget) -> None:
        layout = QFormLayout(page)
        layout.addRow(QLabel("狗皮肤图标生成"))
        layout.addRow("总 PSD", QLineEdit())
        layout.addRow("道具表", QLineEdit())
        layout.addRow("狗皮肤表", QLineEdit())
        layout.addRow("输出目录", QLineEdit())
        layout.addRow("画布尺寸", QLineEdit("256"))
        layout.addRow("内容尺寸", QLineEdit("240"))
        layout.addRow("短边缩放组", QLineEdit("Background,Table"))

    def _on_tab_changed(self, index: int) -> None:
        if hasattr(self, "log"):
            self._log(f"切换页签：{self.tabs.tabText(index)}")

    def _simulate_run(self) -> None:
        self.progress.setValue(100)
        self._log(f"模拟运行完成：{datetime.now().strftime('%H:%M:%S')}")

    def _clear_log(self) -> None:
        self.log.clear()

    def _log(self, message: str) -> None:
        self.log.appendPlainText(message)


def main() -> int:
    app = QApplication(sys.argv)
    app.setApplicationName("PSD Export Tool PySide6 Probe")
    window = UiProbeWindow()
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
