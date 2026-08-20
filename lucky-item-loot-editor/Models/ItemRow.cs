using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json.Nodes;
using DataTables;

namespace LuckyItemLootEditor.Models;

public sealed class ItemRow : INotifyPropertyChanged
{
    private readonly JsonObject _source;
    private WeightField _activeWeightField;
    private ERarity _rarity;
    private EAcquisitionType _acquisitionType;
    private int _standardBoxWeight;
    private int _newbieBoxWeight;
    private int _refreshmentBoxWeight;
    private int _eventBoxWeight;
    private ImageSource? _rarityPlate;
    private ImageSource? _rarityFrame;
    private double _rarityProbability;
    private double _withinRarityProbability;
    private double _expectedProbability;
    private string _candidateStatus = "未计算";
    private Brush _candidateStatusBackground = Brushes.Transparent;
    private Brush _candidateStatusForeground = Brushes.Black;

    private ItemRow(JsonObject source)
    {
        _source = source;
        Id = GetInt(source, "Id");
        Name = GetString(source, "Name");
        ItemTypeValue = GetInt(source, "ItemType");
        IconPath = GetString(source, "IconPath");
        _rarity = (ERarity)GetInt(source, "ItemRarity");
        _acquisitionType = (EAcquisitionType)GetInt(source, "AcquisitionType");
        _standardBoxWeight = GetInt(source, "StandardBoxWeight");
        _newbieBoxWeight = GetInt(source, "NewbieBoxWeight");
        _refreshmentBoxWeight = GetInt(source, "RefreshmentBoxWeight");
        _eventBoxWeight = GetInt(source, "EventBoxWeight");
        Icon = LoadIcon(IconPath);
        UpdateRarityAssets();
    }

    public static ItemRow FromJson(JsonObject source) => new(source);

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }
    public string Name { get; }
    public int ItemTypeValue { get; }
    public string IconPath { get; }
    public ImageSource? Icon { get; }
    public ImageSource? RarityPlate => _rarityPlate;
    public ImageSource? RarityFrame => _rarityFrame;

    public ERarity Rarity
    {
        get => _rarity;
        set
        {
            if (!SetField(ref _rarity, value))
                return;
            OnPropertyChanged(nameof(RarityValue));
            UpdateRarityAssets();
        }
    }

    public int RarityValue => (int)Rarity;

    public EAcquisitionType AcquisitionType
    {
        get => _acquisitionType;
        set => SetField(ref _acquisitionType, value);
    }

    public int StandardBoxWeight
    {
        get => _standardBoxWeight;
        set
        {
            if (SetField(ref _standardBoxWeight, value) && _activeWeightField == WeightField.StandardBoxWeight)
                OnPropertyChanged(nameof(CurrentWeight));
        }
    }

    public int NewbieBoxWeight
    {
        get => _newbieBoxWeight;
        set
        {
            if (SetField(ref _newbieBoxWeight, value) && _activeWeightField == WeightField.NewbieBoxWeight)
                OnPropertyChanged(nameof(CurrentWeight));
        }
    }

    public int RefreshmentBoxWeight
    {
        get => _refreshmentBoxWeight;
        set
        {
            if (SetField(ref _refreshmentBoxWeight, value) && _activeWeightField == WeightField.RefreshmentBoxWeight)
                OnPropertyChanged(nameof(CurrentWeight));
        }
    }

    public int EventBoxWeight
    {
        get => _eventBoxWeight;
        set
        {
            if (SetField(ref _eventBoxWeight, value) && _activeWeightField == WeightField.EventBoxWeight)
                OnPropertyChanged(nameof(CurrentWeight));
        }
    }

    public WeightField ActiveWeightField
    {
        get => _activeWeightField;
        set
        {
            if (SetField(ref _activeWeightField, value))
                OnPropertyChanged(nameof(CurrentWeight));
        }
    }

    public int CurrentWeight
    {
        get => GetWeight(_activeWeightField);
        set => SetWeight(_activeWeightField, value);
    }

    public double RarityProbability
    {
        get => _rarityProbability;
        private set => SetField(ref _rarityProbability, value);
    }

    public double WithinRarityProbability
    {
        get => _withinRarityProbability;
        private set => SetField(ref _withinRarityProbability, value);
    }

    public double ExpectedProbability
    {
        get => _expectedProbability;
        private set => SetField(ref _expectedProbability, value);
    }

    public string CandidateStatus
    {
        get => _candidateStatus;
        private set => SetField(ref _candidateStatus, value);
    }

    public Brush CandidateStatusBackground
    {
        get => _candidateStatusBackground;
        private set => SetField(ref _candidateStatusBackground, value);
    }

    public Brush CandidateStatusForeground
    {
        get => _candidateStatusForeground;
        private set => SetField(ref _candidateStatusForeground, value);
    }

    public int GetWeight(WeightField field) => field switch
    {
        WeightField.StandardBoxWeight => StandardBoxWeight,
        WeightField.NewbieBoxWeight => NewbieBoxWeight,
        WeightField.RefreshmentBoxWeight => RefreshmentBoxWeight,
        WeightField.EventBoxWeight => EventBoxWeight,
        _ => 0,
    };

    public void SetWeight(WeightField field, int value)
    {
        switch (field)
        {
            case WeightField.StandardBoxWeight:
                StandardBoxWeight = value;
                break;
            case WeightField.NewbieBoxWeight:
                NewbieBoxWeight = value;
                break;
            case WeightField.RefreshmentBoxWeight:
                RefreshmentBoxWeight = value;
                break;
            case WeightField.EventBoxWeight:
                EventBoxWeight = value;
                break;
        }
    }

    public void SetProbabilities(double rarityProbability, double withinRarityProbability, double expectedProbability, string candidateStatus)
    {
        RarityProbability = rarityProbability;
        WithinRarityProbability = withinRarityProbability;
        ExpectedProbability = expectedProbability;
        CandidateStatus = candidateStatus;
        UpdateCandidateStatusAppearance();
    }

    public void ApplyToJson()
    {
        _source["ItemRarity"] = (int)Rarity;
        _source["AcquisitionType"] = (int)AcquisitionType;
        _source["StandardBoxWeight"] = StandardBoxWeight;
        _source["NewbieBoxWeight"] = NewbieBoxWeight;
        _source["RefreshmentBoxWeight"] = RefreshmentBoxWeight;
        _source["EventBoxWeight"] = EventBoxWeight;
    }

    private static int GetInt(JsonObject source, string key) => source[key]?.GetValue<int>() ?? 0;
    private static string GetString(JsonObject source, string key) => source[key]?.GetValue<string>() ?? string.Empty;

    private void UpdateCandidateStatusAppearance()
    {
        var noRarityRate = CandidateStatus == "无稀有度概率";
        var zeroRarity = (int)Rarity == 0;
        var zeroWeight = CandidateStatus == "权重为 0";
        var isInitial = AcquisitionType == EAcquisitionType.Initial;

        if (isInitial && (noRarityRate || zeroRarity || zeroWeight))
        {
            CandidateStatusBackground = CreateBrush(140, 93, 60);
            CandidateStatusForeground = Brushes.White;
        }
        else if (zeroRarity || zeroWeight)
        {
            CandidateStatusBackground = CreateBrush(105, 105, 105);
            CandidateStatusForeground = Brushes.White;
        }
        else if (noRarityRate)
        {
            CandidateStatusBackground = CreateBrush(229, 231, 235);
            CandidateStatusForeground = CreateBrush(55, 65, 81);
        }
        else
        {
            CandidateStatusBackground = Brushes.Transparent;
            CandidateStatusForeground = Brushes.Black;
        }
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static ImageSource? LoadIcon(string relativePath)
    {
        var projectRoot = ProjectPaths.TryFindProjectRoot();
        if (projectRoot is null || string.IsNullOrWhiteSpace(relativePath))
            return null;

        var path = Path.Combine(projectRoot, "lucky-dog-rise", "Assets", relativePath.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateRarityAssets()
    {
        _rarityPlate = LoadRarityAsset($"Plate_{_rarity}.png");
        _rarityFrame = LoadRarityAsset($"Frame_{_rarity}.png");
        OnPropertyChanged(nameof(RarityPlate));
        OnPropertyChanged(nameof(RarityFrame));
    }

    private static ImageSource? LoadRarityAsset(string fileName)
    {
        var projectRoot = ProjectPaths.TryFindProjectRoot();
        if (projectRoot is null)
            return null;

        var path = Path.Combine(projectRoot, "lucky-dog-rise", "Assets", "UI", "ItemUI", fileName);
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

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
