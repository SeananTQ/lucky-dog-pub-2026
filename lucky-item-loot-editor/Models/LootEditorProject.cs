namespace LuckyItemLootEditor.Models;

public sealed class LootEditorProject
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; }
    public string SourceItemPath { get; set; } = string.Empty;
    public string SourceItemSha256 { get; set; } = string.Empty;
    public string SourceItemWeightPath { get; set; } = string.Empty;
    public string SourceItemWeightSha256 { get; set; } = string.Empty;
    public int? SelectedBlindBoxId { get; set; }
    public List<LootEditorItemState> Items { get; set; } = new();
    public List<LootEditorWeightState> ItemWeights { get; set; } = new();
}

public sealed class LootEditorItemState
{
    public int ItemId { get; set; }
    public int Rarity { get; set; }
    public int AcquisitionType { get; set; }
}

public sealed class LootEditorWeightState
{
    public int Id { get; set; }
    public int BlindBoxId { get; set; }
    public int ItemId { get; set; }
    public int Weight { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed record LootEditorProjectLoadResult(int? SelectedBlindBoxId, IReadOnlyList<string> Warnings);
