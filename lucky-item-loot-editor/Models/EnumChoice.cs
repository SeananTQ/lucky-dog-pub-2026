namespace LuckyItemLootEditor.Models;

public sealed class EnumChoice<T>
{
    public EnumChoice(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }
    public string Label { get; }
    public string Background { get; init; } = "#FFFFFF";
    public string Foreground { get; init; } = "#222222";
}
