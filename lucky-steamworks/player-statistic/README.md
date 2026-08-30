# Lucky Dog Rise 玩家统计分析工具

这是一个独立的 .NET 8 工具，不依赖或扩展项目中的现有编辑器。它把 Steam Web API 的累计 JSON 保存为可审计快照，再自动计算策划指标、检查指标关系，并用 WPF 表格和历史折线展示结果。

## 项目组成

- `LuckyStats.Core`：从 `tbplayerstatistic.json` 动态读取全部平台统计定义、解析 Steam JSON，并执行留存、履历、筹码账本等派生公式与关系校验。
- `LuckyStats.Infrastructure`：Steam Web API、SQLite 快照、CSV/JSON 导出。
- `LuckyStats.Desktop`：Windows WPF 图形界面。
- `LuckyStats.Cli`：供 Codex、脚本和自动化任务调用的纯 JSON 命令行接口。
- `LuckyStats.SmokeTests`：不访问 Steam 的固定样本测试。

## 启动

```powershell
dotnet run --project .\src\LuckyStats.Desktop\LuckyStats.Desktop.csproj
```

程序会向上定位工作区，并直接读取：

- `lucky-dog-rise/Data/Json/tbplayerstatistic.json`
- `.local-build/player-statistic-4972240-webapi.txt`
- `lucky-dog-rise/Build/Developer/steam-account-allowlist.json` 中 `enabled=true` 的开发帐号

Web API Key 只通过 `x-webapi-key` 请求头发送，不写入 SQLite、导出文件、界面或命令行输出。

## CLI

```powershell
dotnet run --project .\src\LuckyStats.Cli\LuckyStats.Cli.csproj -- sync
dotnet run --project .\src\LuckyStats.Cli\LuckyStats.Cli.csproj -- report
dotnet run --project .\src\LuckyStats.Cli\LuckyStats.Cli.csproj -- user --steamid 7656119xxxxxxxxxx
dotnet run --project .\src\LuckyStats.Cli\LuckyStats.Cli.csproj -- history --stat STAT_PT1_COHORT_MEMBER
dotnet run --project .\src\LuckyStats.Cli\LuckyStats.Cli.csproj -- export
```

成功命令的 stdout 是稳定 JSON，诊断写入 stderr。自动化无需重新在 Excel 中实现公式。

## 留存口径

工具不会用“当前回访人数 ÷ 当前 cohort”冒充留存率。24/72/168 小时留存分母来自本地历史中，截止时点之前最近一次快照的净 cohort；尚未积累足够早的快照时显示“数据不足”。因此第一份同步是统计基线，之后应定期同步。

## 本地数据

默认数据库与导出位于：

```text
%LOCALAPPDATA%\LuckyDogRise\PlayerStatisticAnalyzer\
```

可通过 `LUCKY_STATS_DEFINITIONS`、`LUCKY_STATS_KEY_FILE`、`LUCKY_STATS_DATABASE`、`LUCKY_STATS_EXPORT_DIR` 覆盖路径。
