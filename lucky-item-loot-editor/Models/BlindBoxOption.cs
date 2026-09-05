using DataTables;

namespace LuckyItemLootEditor.Models;

public sealed class BlindBoxOption
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required EBlindBoxType BoxType { get; init; }
    public required bool IsEnabled { get; init; }

    public EAcquisitionType ExpectedAcquisitionType => BoxType switch
    {
        EBlindBoxType.Decoration => EAcquisitionType.DecorationBlindBox,
        EBlindBoxType.NewbieDecoration => EAcquisitionType.DecorationBlindBox,
        EBlindBoxType.Refreshment => EAcquisitionType.RefreshmentBlindBox,
        EBlindBoxType.Event => EAcquisitionType.EventReward,
        _ => EAcquisitionType.DebugOnly,
    };

    public string DisplayName => $"{Id} · {Name}";
}
