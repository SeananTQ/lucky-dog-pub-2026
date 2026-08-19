using DataTables;

namespace LuckyItemLootEditor.Models;

public sealed class BlindBoxOption
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required EBlindBoxType BoxType { get; init; }
    public required bool IsEnabled { get; init; }

    public WeightField WeightField => BoxType switch
    {
        EBlindBoxType.Decoration => WeightField.StandardBoxWeight,
        EBlindBoxType.NewbieDecoration => WeightField.NewbieBoxWeight,
        EBlindBoxType.Refreshment => WeightField.RefreshmentBoxWeight,
        EBlindBoxType.Event => WeightField.EventBoxWeight,
        _ => WeightField.StandardBoxWeight,
    };

    public EAcquisitionType ExpectedAcquisitionType => BoxType switch
    {
        EBlindBoxType.Decoration => EAcquisitionType.DecorationBlindBox,
        EBlindBoxType.NewbieDecoration => EAcquisitionType.DecorationBlindBox,
        EBlindBoxType.Refreshment => EAcquisitionType.RefreshmentBlindBox,
        EBlindBoxType.Event => EAcquisitionType.EventReward,
        _ => EAcquisitionType.DebugOnly,
    };

    public string WeightFieldLabel => WeightField switch
    {
        WeightField.StandardBoxWeight => "StandardBoxWeight",
        WeightField.NewbieBoxWeight => "NewbieBoxWeight",
        WeightField.RefreshmentBoxWeight => "RefreshmentBoxWeight",
        WeightField.EventBoxWeight => "EventBoxWeight",
        _ => WeightField.ToString(),
    };

    public string DisplayName => $"{Id} · {Name} · {WeightFieldLabel}";
}
