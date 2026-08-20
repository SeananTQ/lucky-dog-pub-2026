using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using DataTables;
using LuckyItemLootEditor.Models;
using LuckyItemLootEditor.Services;

namespace LuckyItemLootEditor.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private LootDataStore? _store;
    private BlindBoxOption? _selectedBlindBox;
    private ItemRow? _selectedItem;
    private string _searchText = string.Empty;
    private string _statusText = "尚未加载数据";
    private ImageSource? _randomResultIcon;
    private ItemRow? _randomResultItem;
    private string _randomResultText = "点击“随机一次”体验当前奖池。";
    private bool _isDirty;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;

        RarityOptions = new[] { (ERarity)0 }
            .Concat(Enum.GetValues<ERarity>())
            .Distinct()
            .Select(value => new EnumChoice<ERarity>(value, GetRarityLabel(value))
            {
                Background = GetRarityColor(value),
                Foreground = GetRarityForeground(value),
            })
            .ToList();
        AcquisitionOptions = Enum.GetValues<EAcquisitionType>()
            .Select(value => new EnumChoice<EAcquisitionType>(value, GetAcquisitionLabel(value)))
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ItemRow> Items { get; } = new();
    public ObservableCollection<RarityCountRow> RarityCounts { get; } = new();
    public ICollectionView ItemsView { get; }
    public IReadOnlyList<BlindBoxOption> BlindBoxes { get; private set; } = Array.Empty<BlindBoxOption>();
    public IReadOnlyList<EnumChoice<ERarity>> RarityOptions { get; }
    public IReadOnlyList<EnumChoice<EAcquisitionType>> AcquisitionOptions { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value;
            OnPropertyChanged();
            ItemsView.Refresh();
        }
    }

    public BlindBoxOption? SelectedBlindBox
    {
        get => _selectedBlindBox;
        set
        {
            if (ReferenceEquals(_selectedBlindBox, value))
                return;
            _selectedBlindBox = value;
            if (value is not null)
            {
                foreach (var item in Items)
                    item.ActiveWeightField = value.WeightField;
            }
            RecalculateAll();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWeightFieldLabel));
        }
    }

    public string SelectedWeightFieldLabel => SelectedBlindBox?.WeightFieldLabel ?? "未选择权重字段";

    public ItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
                return;
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public ImageSource? RandomResultIcon
    {
        get => _randomResultIcon;
        private set => SetField(ref _randomResultIcon, value);
    }

    public ItemRow? RandomResultItem
    {
        get => _randomResultItem;
        private set => SetField(ref _randomResultItem, value);
    }

    public string RandomResultText
    {
        get => _randomResultText;
        private set => SetField(ref _randomResultText, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    public string ItemPath => _store?.ItemPath ?? string.Empty;
    public string ProjectRoot => _store?.ProjectRoot ?? string.Empty;

    public void Load()
    {
        _store = LootDataStore.Load();
        foreach (var item in Items)
            item.PropertyChanged -= ItemOnPropertyChanged;
        Items.Clear();
        foreach (var item in _store.Items)
        {
            item.PropertyChanged += ItemOnPropertyChanged;
            Items.Add(item);
        }

        BlindBoxes = _store.BlindBoxes;
        OnPropertyChanged(nameof(BlindBoxes));
        SelectedBlindBox = BlindBoxes.FirstOrDefault(box => box.IsEnabled) ?? BlindBoxes.FirstOrDefault();
        IsDirty = false;
        StatusText = $"已加载 {Items.Count} 个物品；数据源：{ItemPath}";
        RecalculateAll();
    }

    public void Save()
    {
        if (_store is null)
            return;
        _store.Save();
        IsDirty = false;
        StatusText = $"已保存 tbItem.json；可使用 Git 查看差异。";
    }

    public void RollRandom()
    {
        if (_store is null || SelectedBlindBox is null)
        {
            RandomResultItem = null;
            RandomResultText = "请先加载并选择盲盒。";
            return;
        }

        var rates = _store.RarityRates
            .Where(rate => rate.IsEnabled && rate.BlindBoxId == SelectedBlindBox.Id && rate.Weight > 0)
            .ToList();
        var rarity = PickWeighted(rates, rate => rate.Weight)?.Rarity;
        if (rarity is null)
        {
            RandomResultItem = null;
            RandomResultIcon = null;
            RandomResultText = "当前盲盒没有可用的稀有度概率。";
            return;
        }

        var primary = Items
            .Where(item => item.Rarity == rarity && item.CurrentWeight > 0 && item.AcquisitionType == SelectedBlindBox.ExpectedAcquisitionType)
            .ToList();
        var candidates = primary.Count > 0
            ? primary
            : Items.Where(item => item.Rarity == rarity && item.CurrentWeight > 0).ToList();
        var item = PickWeighted(candidates, candidate => candidate.CurrentWeight);
        if (item is null)
        {
            RandomResultItem = null;
            RandomResultIcon = null;
            RandomResultText = $"抽中了 {GetRarityLabel(rarity.Value)}，但该稀有度没有可用物品。";
            return;
        }

        RandomResultItem = item;
        RandomResultIcon = item.Icon;
        RandomResultText = $"{item.Name}（ID {item.Id}）\n{GetRarityLabel(item.Rarity)} · 权重 {item.CurrentWeight:N0}";
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ItemRow.Rarity) or nameof(ItemRow.AcquisitionType) or nameof(ItemRow.CurrentWeight)
            or nameof(ItemRow.StandardBoxWeight) or nameof(ItemRow.NewbieBoxWeight)
            or nameof(ItemRow.RefreshmentBoxWeight) or nameof(ItemRow.EventBoxWeight))
        {
            IsDirty = true;
            RecalculateAll();
            StatusText = "有未保存修改。";
        }
    }

    private void RecalculateAll()
    {
        UpdateRarityCounts();
        if (_store is null || SelectedBlindBox is null)
            return;

        var rates = _store.RarityRates
            .Where(rate => rate.IsEnabled && rate.BlindBoxId == SelectedBlindBox.Id && rate.Weight > 0)
            .ToList();
        var totalRateWeight = rates.Sum(rate => rate.Weight);
        var rarityProbabilities = rates
            .GroupBy(rate => rate.Rarity)
            .ToDictionary(group => group.Key, group => totalRateWeight == 0 ? 0 : group.Sum(rate => rate.Weight) / (double)totalRateWeight);

        var effectiveCandidates = new Dictionary<ERarity, IReadOnlyList<ItemRow>>();
        foreach (var group in Items.GroupBy(item => item.Rarity))
        {
            var primary = group.Where(item => item.CurrentWeight > 0 && item.AcquisitionType == SelectedBlindBox.ExpectedAcquisitionType).ToList();
            effectiveCandidates[group.Key] = primary.Count > 0
                ? primary
                : group.Where(item => item.CurrentWeight > 0).ToList();
        }

        foreach (var item in Items)
        {
            var rarityProbability = rarityProbabilities.GetValueOrDefault(item.Rarity);
            var candidates = effectiveCandidates.GetValueOrDefault(item.Rarity) ?? Array.Empty<ItemRow>();
            var totalItemWeight = candidates.Sum(candidate => candidate.CurrentWeight);
            var isCandidate = candidates.Contains(item);
            var within = isCandidate && totalItemWeight > 0 ? item.CurrentWeight / (double)totalItemWeight : 0;
            var expected = rarityProbability * within;
            var status = (int)item.Rarity == 0
                ? "稀有度为 0"
                : !rarityProbabilities.ContainsKey(item.Rarity)
                    ? "无稀有度概率"
                : isCandidate
                    ? candidates.Any(candidate => candidate.AcquisitionType == SelectedBlindBox.ExpectedAcquisitionType)
                        ? "正式候选"
                        : "兜底候选"
                    : item.CurrentWeight <= 0 ? "权重为 0" : "不参与当前抽取";
            item.SetProbabilities(rarityProbability, within, expected, status);
        }
    }

    private void UpdateRarityCounts()
    {
        var regularRarities = new[]
        {
            ERarity.Mythic,
            ERarity.Legendary,
            ERarity.Epic,
            ERarity.Rare,
            ERarity.Uncommon,
            ERarity.Common,
        };
        var counts = Items
            .GroupBy(item => item.Rarity)
            .ToDictionary(group => group.Key, group => group.Count());
        var otherCount = counts
            .Where(pair => !regularRarities.Contains(pair.Key))
            .Sum(pair => pair.Value);
        var maxCount = regularRarities
            .Select(rarity => counts.GetValueOrDefault(rarity))
            .Append(otherCount)
            .DefaultIfEmpty()
            .Max();

        RarityCounts.Clear();
        foreach (var rarity in regularRarities)
        {
            var count = counts.GetValueOrDefault(rarity);
            RarityCounts.Add(new RarityCountRow(
                GetRarityLabel(rarity),
                count,
                GetCountBarWidth(count, maxCount),
                CreateBrush(GetRarityColor(rarity))));
        }

        RarityCounts.Add(new RarityCountRow(
            "其他",
            otherCount,
            GetCountBarWidth(otherCount, maxCount),
            CreateBrush("#9CA3AF")));
    }

    private static double GetCountBarWidth(int count, int maxCount) =>
        maxCount <= 0 ? 0 : 160d * count / maxCount;

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ItemRow item)
            return false;
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;
        return item.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.IconPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private static T? PickWeighted<T>(IReadOnlyList<T> entries, Func<T, int> getWeight) where T : class
    {
        var total = entries.Sum(getWeight);
        if (entries.Count == 0 || total <= 0)
            return null;
        var roll = Random.Shared.Next(total);
        foreach (var entry in entries)
        {
            roll -= getWeight(entry);
            if (roll < 0)
                return entry;
        }
        return entries[^1];
    }

    private static string GetRarityLabel(ERarity rarity) => rarity switch
    {
        (ERarity)0 => "无稀有度（0）",
        ERarity.Common => "普通",
        ERarity.Uncommon => "优秀",
        ERarity.Rare => "稀有",
        ERarity.Epic => "史诗",
        ERarity.Legendary => "传说",
        ERarity.Mythic => "神话",
        ERarity.Special1 => "特殊 1",
        ERarity.Special2 => "特殊 2",
        _ => rarity.ToString(),
    };

    private static string GetRarityColor(ERarity rarity) => rarity switch
    {
        (ERarity)0 => "#9CA3AF",
        ERarity.Common => "#D7D7D7",
        ERarity.Uncommon => "#8ED9AE",
        ERarity.Rare => "#78B8FF",
        ERarity.Epic => "#B28CFF",
        ERarity.Legendary => "#FFC15C",
        ERarity.Mythic => "#C85D64",
        ERarity.Special1 => "#F28C8C",
        ERarity.Special2 => "#9CA9B6",
        _ => "#FFFFFF",
    };

    private static string GetRarityForeground(ERarity rarity) => rarity switch
    {
        (ERarity)0 or ERarity.Common or ERarity.Uncommon => "#222222",
        _ => "#FFFFFF",
    };

    private static string GetAcquisitionLabel(EAcquisitionType type) => type switch
    {
        EAcquisitionType.Initial => "初始拥有",
        EAcquisitionType.DecorationBlindBox => "装扮盲盒",
        EAcquisitionType.RefreshmentBlindBox => "消耗品盲盒",
        EAcquisitionType.EventReward => "活动 / LinkTree",
        EAcquisitionType.Retired => "已下架",
        EAcquisitionType.DebugOnly => "仅调试",
        _ => type.ToString(),
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
