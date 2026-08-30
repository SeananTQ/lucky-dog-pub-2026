using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using LuckyStats.Core;
using LuckyStats.Infrastructure;

namespace LuckyStats.Desktop;

public partial class MainWindow : Window
{
    private StatisticsApplication? _application;
    private bool _busy;

    public ObservableCollection<MetricViewRow> Metrics { get; } = [];
    public ObservableCollection<CheckViewRow> Checks { get; } = [];
    public ObservableCollection<FactViewRow> Facts { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "正在初始化本地数据库…");
            _application = await StatisticsApplication.CreateAsync();
            DefinitionsPathText.Text = _application.Paths.DefinitionsFile;
            DatabasePathText.Text = _application.Paths.DatabaseFile;
            ExportPathText.Text = _application.Paths.ExportDirectory;
            AllowlistPathText.Text = _application.Paths.DeveloperAllowlistFile;
            HistoryStatCombo.ItemsSource = _application.Definitions
                .Select(x => new StatChoice(x.ApiName, $"{x.DisplayName} · {x.ApiName}"))
                .ToArray();
            HistoryStatCombo.SelectedIndex = 0;
            await LoadLatestReportAsync();
        }
        catch (Exception exception)
        {
            ShowError("初始化失败", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("正在读取本地快照…", LoadLatestReportAsync);
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("正在查询 Steam 全局统计和开发帐号…", async () =>
        {
            var result = await RequireApplication().SyncGlobalAsync();
            BindReport(result.Report);
            StatusText.Text = $"已保存 Steam 快照批次 #{result.BatchId}，并完成分析。";
            await ReloadHistoryAsync();
        });
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("正在导出分析结果…", async () =>
        {
            var output = await RequireApplication().ExportLatestAsync();
            StatusText.Text = $"已导出：{output}";
            Process.Start(new ProcessStartInfo("explorer.exe", output) { UseShellExecute = true });
        });
    }

    private async void OnQueryUserClick(object sender, RoutedEventArgs e)
    {
        var steamId = SteamIdText.Text.Trim();
        await RunBusyAsync($"正在查询玩家 {steamId}…", async () =>
        {
            var result = await RequireApplication().QueryUserAsync(steamId);
            UserGrid.ItemsSource = RequireApplication().Definitions.Select(definition => new UserFactViewRow(
                definition.DisplayName,
                definition.ApiName,
                result.Response.Values.GetValueOrDefault(definition.ApiName),
                definition.Unit == StatisticUnit.Flag
                    ? (result.Response.Values.GetValueOrDefault(definition.ApiName) > 0 ? "是" : "否")
                    : string.Empty)).ToArray();
            StatusText.Text = $"玩家 {steamId} 查询完成，已保存快照批次 #{result.BatchId}。";
        });
    }

    private async void OnHistoryStatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_application is null || _busy)
            return;
        await RunBusyAsync("正在读取历史曲线…", ReloadHistoryAsync);
    }

    private async Task LoadLatestReportAsync()
    {
        var report = await RequireApplication().GetLatestReportAsync();
        if (report is null)
        {
            Metrics.Clear();
            Checks.Clear();
            Facts.Clear();
            StatusText.Text = "尚无本地全局快照。点击“从 Steam 同步”创建第一份基线。";
            CapturedAtText.Text = string.Empty;
            return;
        }

        BindReport(report);
        StatusText.Text = "已读取最新本地快照。不会在读取本地数据时访问 Steam。";
        await ReloadHistoryAsync();
    }

    private void BindReport(AnalysisReport report)
    {
        Replace(Metrics, report.Metrics.Select(x => new MetricViewRow(
            x.Group, x.DisplayName, x.Value, x.Formula, x.Status)));
        Replace(Checks, report.Checks.Select(x => new CheckViewRow(
            SeverityLabel(x.Severity), x.Rule, x.Message)));
        Replace(Facts, report.Facts.Select(x => new FactViewRow(
            x.DisplayName, x.ApiName, x.Unit, x.GlobalValue, x.ExcludedValue, x.AnalyzedValue)));
        CapturedAtText.Text = $"快照时间：{report.CapturedAtUtc.LocalDateTime:G}";
    }

    private async Task ReloadHistoryAsync()
    {
        if (HistoryStatCombo.SelectedItem is not StatChoice choice)
            return;
        var points = await RequireApplication().GetHistoryAsync(choice.ApiName);
        HistoryChart.Points = points.Select(x => new ChartPoint(x.CapturedAtUtc, x.AnalyzedValue)).ToArray();
        HistoryGrid.ItemsSource = points.Select(x => new HistoryViewRow(
            x.CapturedAtUtc.LocalDateTime,
            x.GlobalValue,
            x.ExcludedValue,
            x.AnalyzedValue)).ToArray();
    }

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        if (_busy)
            return;
        try
        {
            SetBusy(true, message);
            await action();
        }
        catch (Exception exception)
        {
            ShowError("操作失败", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        SyncButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        if (message is not null)
            StatusText.Text = message;
    }

    private StatisticsApplication RequireApplication() =>
        _application ?? throw new InvalidOperationException("程序尚未初始化完成。");

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = exception.Message;
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        destination.Clear();
        foreach (var item in source)
            destination.Add(item);
    }

    private static string SeverityLabel(CheckSeverity severity) => severity switch
    {
        CheckSeverity.Error => "错误",
        CheckSeverity.Warning => "警告",
        _ => "正常"
    };

    private void OnClosed(object? sender, EventArgs e) => _application?.Dispose();
}

public sealed record MetricViewRow(string 分组, string 指标, string 结果, string 公式与依据, string 状态);
public sealed record CheckViewRow(string 级别, string 规则, string 说明);
public sealed record FactViewRow(string 名称, string APIName, string 单位, long Steam全局值, long 排除帐号值, long 分析净值);
public sealed record UserFactViewRow(string 名称, string APIName, long 数值, string Flag含义);
public sealed record HistoryViewRow(DateTime 快照时间, long Steam全局值, long 排除帐号值, long 分析净值);
public sealed record StatChoice(string ApiName, string DisplayLabel);
