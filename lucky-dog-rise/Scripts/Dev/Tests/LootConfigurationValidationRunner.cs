#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class LootConfigurationValidationRunner : Control
{
    [Export(PropertyHint.File, "*.json")] public string SnapshotPath { get; set; } = "";
    private RichTextLabel _output = null!;
    private Button _runButton = null!;
    private Label _status = null!;
    private bool _running;
    public string GetValidationText() => _output.GetParsedText();

    public override void _Ready()
    {
        GetWindow().Title = "盲盒配置校验";
        GetWindow().Transparent = false;
        GetWindow().AlwaysOnTop = false;
        GetWindow().Borderless = false;
        GetViewport().TransparentBg = false;
        var background = new ColorRect { Color = new Color(0.12f, 0.13f, 0.15f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var layout = new VBoxContainer();
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.OffsetLeft = 20;
        layout.OffsetTop = 20;
        layout.OffsetRight = -20;
        layout.OffsetBottom = -20;
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);
        var title = new Label { Text = "盲盒配置校验" };
        title.AddThemeFontSizeOverride("font_size", 26);
        layout.AddChild(title);
        layout.AddChild(new Label { Text = "CSV 回填 Excel → Luban 生成 → 更新游戏 JSON → 开始校验" });
        _runButton = new Button { Text = "开始校验", CustomMinimumSize = new Vector2(0, 44) };
        _runButton.Pressed += RunValidation;
        layout.AddChild(_runButton);
        _status = new Label { Text = "待校验 · 点击按钮后开始运算" };
        _status.AddThemeFontSizeOverride("font_size", 20);
        layout.AddChild(_status);
        _output = new RichTextLabel
        {
            BbcodeEnabled = true, SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _output.AddThemeColorOverride("default_color", new Color("dce1e8"));
        _output.AddThemeFontSizeOverride("normal_font_size", 18);
        layout.AddChild(_output);
        _output.AddText("校验将检查导出快照与最终游戏数据的一致性、奖池配置和实际抽样逻辑。\n\n结果支持选择与复制。");
        var hint = new Label { Text = "游戏 JSON 修改后，请停止并重新 F6，刷新 Luban 数据缓存。" };
        hint.AddThemeColorOverride("font_color", new Color("9ba7b6"));
        layout.AddChild(hint);
        // Explicit automation entry point only; interactive F6 waits for the button.
        if (OS.GetCmdlineUserArgs().Contains("--loot-validation-headless"))
            CallDeferred(nameof(RunValidation));
    }

    public async void RunValidation()
    {
        if (_running) return;
        _running = true;
        _runButton.Disabled = true;
        _runButton.Text = "正在校验…";
        _status.Text = "正在检查配置与导出快照…";
        _status.AddThemeColorOverride("font_color", new Color("a9c6e8"));
        _output.Clear();
        _output.AddText("正在校验，请稍候。\n结果将在完成后按分组显示。");
        // Allow the running state to render before any expensive work starts.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            var dataDirectory = ProjectSettings.GlobalizePath("res://Data/Json");
            var snapshotPath = Path.GetFullPath(Path.Combine(dataDirectory, "../../../lucky-item-loot-editor/output/LootValidationSnapshot.json"));
            if (!string.IsNullOrWhiteSpace(SnapshotPath))
                snapshotPath = ProjectSettings.GlobalizePath(SnapshotPath);
            var report = LootConfigurationValidator.Run(dataDirectory, snapshotPath);
            // Check safe boxes independently, even if the export snapshot is missing.
            // A skipped box never clears the failures reported by the preceding checks.
            try { await CheckRuntimeSampling(report); }
            catch (Exception exception) { report.Errors.Add($"游戏抽样校验异常：{exception.Message}"); }
            ShowReport(report);
            GD.Print("[LootValidation] " + report);
            if (OS.GetCmdlineUserArgs().Contains("--loot-validation-headless"))
                GetTree().Quit(report.Passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            _status.Text = "校验未完成";
            _status.AddThemeColorOverride("font_color", new Color("e7a7a7"));
            _output.Clear();
            _output.AddText(exception.Message);
            if (OS.GetCmdlineUserArgs().Contains("--loot-validation-headless")) GetTree().Quit(1);
        }
        finally
        {
            _running = false;
            _runButton.Disabled = false;
            _runButton.Text = "重新校验";
        }
    }

    private void ShowReport(LootConfigurationValidator.Report report)
    {
        _status.Text = report.Passed ? "校验通过" : $"校验失败 · {report.Errors.Count} 项需要处理";
        _status.AddThemeColorOverride("font_color", new Color(report.Passed ? "a7cdb5" : "e7a7a7"));
        _output.Clear();
        AddSection("需要处理", report.Errors, "e7a7a7", numbered: true);
        AddSection("抽样检查", report.Notes.Where(note => note.StartsWith("[抽样]")), "a7cdb5");
        AddSection("提示与跳过项", report.Notes.Where(note => note.StartsWith("[提示]") || note.StartsWith("[跳过抽样]")), "d8c394");
        AddSection("数据来源与检查范围", report.Notes.Where(note => !note.StartsWith("[")), "a9c6e8");
        _output.ScrollToLine(0);
    }

    private void AddSection(string title, IEnumerable<string> messages, string color, bool numbered = false)
    {
        var rows = messages.ToList();
        if (rows.Count == 0) return;
        _output.AppendText($"[font_size=21][color=#{color}]{title} · {rows.Count}[/color][/font_size]\n\n");
        for (var index = 0; index < rows.Count; index++)
        {
            _output.PushColor(new Color(color));
            _output.AddText(numbered ? $"{index + 1:00}  " : "•  ");
            _output.Pop();
            _output.AddText(rows[index].Replace("[抽样] ", "").Replace("[提示] ", "").Replace("[跳过抽样] ", "跳过：") + "\n\n");
        }
        _output.AddText("\n");
    }

    private async Task CheckRuntimeSampling(LootConfigurationValidator.Report report)
    {
        var tables = LubanData.Tables;
        var service = new BlindBoxService(testSeed: 20260918);
        foreach (var box in tables.TbBlindBox.DataList.Where(box => box.IsEnabled))
        {
            _status.Text = $"正在抽样校验 · 盲盒 {box.Id}";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var expectedAcquisition = box.BoxType switch
            {
                EBlindBoxType.Decoration or EBlindBoxType.NewbieDecoration => EAcquisitionType.DecorationBlindBox,
                EBlindBoxType.Refreshment => EAcquisitionType.RefreshmentBlindBox,
                EBlindBoxType.Event => EAcquisitionType.EventReward,
                _ => throw new InvalidDataException($"未知盲盒类型 {box.BoxType}"),
            };
            var rates = tables.TbBlindBoxRarityRate.DataList.Where(rate => rate.IsEnabled
                && rate.BlindBoxId == box.Id && rate.Weight > 0).ToList();
            var candidates = tables.TbBlindBoxItemWeight.DataList.Where(row => row.IsEnabled
                && row.BlindBoxId == box.Id && row.Weight > 0)
                .Select(row => (Item: tables.TbItem.GetOrDefault(row.ItemId), row.Weight))
                .Where(row => row.Item != null && row.Item.AcquisitionType == expectedAcquisition
                    && rates.Any(rate => rate.Rarity == row.Item.ItemRarity)).ToList();
            var validIds = candidates.Select(row => row.Item!.Id).ToHashSet();
            if (rates.Count == 0 || validIds.Count == 0
                || rates.Sum(rate => (long)rate.Weight) > int.MaxValue
                || rates.Any(rate => candidates.Where(row => row.Item!.ItemRarity == rate.Rarity)
                    .Sum(row => (long)row.Weight) is 0 or > int.MaxValue))
            {
                report.Notes.Add($"[跳过抽样] 盲盒 {box.Id}：奖池或稀有度配置不具备安全抽样条件，请先修复配置。");
                report.Errors.Add($"盲盒 {box.Id} 未完成游戏抽样检查。");
                continue;
            }
            var counts = new Dictionary<int, int>();
            for (var index = 0; index < 20000; index++)
            {
                var item = service.RollRewardForTesting(box, new BlindBoxRuntimeState());
                if (item == null || !validIds.Contains(item.Id))
                    throw new InvalidDataException($"盲盒 {box.Id} 抽到了非法奖励 {item?.Id}。");
                counts[item.Id] = counts.GetValueOrDefault(item.Id) + 1;
                if ((index + 1) % 1000 == 0)
                {
                    _status.Text = $"正在抽样校验 · 盲盒 {box.Id} · {index + 1:N0} / 20,000";
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            // Broad, deterministic statistical guard; tiny probabilities need not be observed.
            var totalRate = rates.Sum(rate => (double)rate.Weight);
            foreach (var row in candidates)
            {
                var item = row.Item!;
                var rarityWeight = rates.Where(rate => rate.Rarity == item.ItemRarity).Sum(rate => (double)rate.Weight);
                var itemTotal = candidates.Where(other => other.Item!.ItemRarity == item.ItemRarity).Sum(other => (double)other.Weight);
                var probability = rarityWeight / totalRate * row.Weight / itemTotal;
                var expectedCount = 20000 * probability;
                var tolerance = Math.Max(12, 8 * Math.Sqrt(20000 * probability * (1 - probability)));
                if (Math.Abs(counts.GetValueOrDefault(item.Id) - expectedCount) > tolerance)
                    report.Errors.Add($"盲盒 {box.Id} / 物品 {item.Id} 抽样偏差异常：预期 {expectedCount:F1} 次，实际 {counts.GetValueOrDefault(item.Id)} 次。");
            }
            if (box.RewardSelectionMode == ERewardSelectionMode.ExcludeDrawnInThisBox)
            {
                var state = new BlindBoxRuntimeState();
                var drawn = new HashSet<int>();
                for (var index = 0; index < validIds.Count; index++)
                {
                    var item = service.RollRewardForTesting(box, state);
                    if (item == null || !validIds.Contains(item.Id) || !drawn.Add(item.Id))
                        throw new InvalidDataException($"盲盒 {box.Id} 不重复抽取规则失败。");
                    service.RecordClaimedReward(state, box.Id, item.Id);
                }
                if (service.RollRewardForTesting(box, state) != null)
                    throw new InvalidDataException($"盲盒 {box.Id} 奖池抽完后仍返回奖励。");
            }
            var selectionCheck = box.RewardSelectionMode == ERewardSelectionMode.ExcludeDrawnInThisBox
                ? "；不重复规则检查完成" : "";
            report.Notes.Add($"[抽样] 盲盒 {box.Id}：固定种子抽样 20000 次，合法奖池 {validIds.Count} 件；概率检查完成{selectionCheck}。");
        }
    }
}
#endif
