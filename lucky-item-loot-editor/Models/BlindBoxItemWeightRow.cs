namespace LuckyItemLootEditor.Models;

public sealed class BlindBoxItemWeightRow
{
    public required int Id { get; init; }
    public required int BlindBoxId { get; init; }
    public required int ItemId { get; init; }
    public required int Weight { get; set; }
    public required bool IsEnabled { get; set; }
}
