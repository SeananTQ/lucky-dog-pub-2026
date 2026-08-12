# Steamworks 官方文档本地快照

本目录保存项目实际使用的 Steamworks 官方页面快照，优先供本地检索，避免每次重复打开网页。

- `raw/`：从已登录页面提取的官方正文 HTML，不包含 Steamworks 顶部导航或帐号信息。
- `markdown/`：便于 `rg` 搜索的保真文本版本；复杂表格以制表符文本保留。
- `manifest.json`：来源网址、抓取时间和 SHA-256。

## 当前页面

- [Steam 库存架构](markdown/inventory-schema.md)
- [ISteamInventory 接口](markdown/ISteamInventory.md)

## 使用边界

官方快照与项目实测必须分开记录。Markdown 仅用于检索；发生歧义时以同目录 raw HTML 及 source 链接为准。
