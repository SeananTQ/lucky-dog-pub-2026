using System.Windows;
using System.Diagnostics;
using LuckyItemLootEditor.ViewModels;

namespace LuckyItemLootEditor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;
        TryRun(_viewModel.Load);
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => TryRun(_viewModel.Load);

    private void SaveButton_Click(object sender, RoutedEventArgs e) => TryRun(_viewModel.Save);

    private void RandomButton_Click(object sender, RoutedEventArgs e) => TryRun(_viewModel.RollRandom);

    private void BlindBoxComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loaded && DataContext is MainViewModel viewModel && e.AddedItems.Count > 0)
            viewModel.SelectedBlindBox = e.AddedItems[0] as Models.BlindBoxOption;
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is System.Windows.Controls.TextBox textBox)
            viewModel.SearchText = textBox.Text;
    }

    private void DiffButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var diff = _viewModel.GetGitDiff();
            var window = new DiffWindow(diff) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Git Diff 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.ToString(), "工具错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
