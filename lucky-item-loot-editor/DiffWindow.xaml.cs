using System.Windows;

namespace LuckyItemLootEditor;

public partial class DiffWindow : Window
{
    public string DiffText { get; }

    public DiffWindow(string diff)
    {
        DiffText = string.IsNullOrWhiteSpace(diff) ? "当前没有 Git diff。" : diff;
        InitializeComponent();
        DataContext = this;
    }
}
