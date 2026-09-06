using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
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
        LoadStartupProject();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscardUnsavedChanges())
            TryRun(_viewModel.Load);
    }

    private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsavedChanges())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "打开掉率编辑工程",
            Filter = "掉率编辑工程 (*.ldloot)|*.ldloot|所有文件 (*.*)|*.*",
            DefaultExt = ".ldloot",
            CheckFileExists = true,
            InitialDirectory = GetSaveDirectory(),
        };
        if (dialog.ShowDialog(this) != true)
            return;
        TryRun(() =>
        {
            var warnings = _viewModel.OpenProject(dialog.FileName);
            if (warnings.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, warnings),
                    "工程数据合并提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void SaveProjectButton_Click(object sender, RoutedEventArgs e) => SaveCurrentProject(false);

    private void SaveProjectAsButton_Click(object sender, RoutedEventArgs e) => SaveCurrentProject(true);

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e) => TryRun(_viewModel.ExportCsv);

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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.IsDirty)
            return;
        var choice = MessageBox.Show(this, "工程还有未保存修改，关闭前保存吗？",
            "未保存的工程", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (choice == MessageBoxResult.Yes && !SaveCurrentProject(false))
            e.Cancel = true;
    }

    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!_viewModel.IsDirty)
            return true;
        return MessageBox.Show(this, "当前工程有未保存修改，确定放弃并继续吗？",
            "未保存的工程", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private bool SaveCurrentProject(bool saveAs)
    {
        var path = saveAs ? null : _viewModel.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存掉率编辑工程",
                Filter = "掉率编辑工程 (*.ldloot)|*.ldloot|所有文件 (*.*)|*.*",
                DefaultExt = ".ldloot",
                AddExtension = true,
                FileName = "loot-draft.ldloot",
                InitialDirectory = GetSaveDirectory(),
            };
            if (dialog.ShowDialog(this) != true)
                return false;
            path = dialog.FileName;
        }

        try
        {
            _viewModel.SaveProject(path);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.ToString(), "工具错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void LoadStartupProject()
    {
        var projectPath = ProjectPaths.TryFindLatestEditorProject();
        if (projectPath is null)
        {
            TryRun(_viewModel.Load);
            return;
        }

        try
        {
            var warnings = _viewModel.OpenProject(projectPath);
            if (warnings.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, warnings),
                    "已自动打开工程，但存在数据合并提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            try
            {
                _viewModel.Load();
            }
            catch (Exception fallbackException)
            {
                MessageBox.Show(this, fallbackException.ToString(),
                    "工具错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(this,
                $"无法自动打开上次保存的工程，已改为加载当前 Luban 数据。\n\n工程：{projectPath}\n原因：{exception.Message}",
                "自动打开工程失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? GetSaveDirectory()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.ProjectRoot))
            return null;
        var saveDirectory = ProjectPaths.GetEditorSaveDirectory(_viewModel.ProjectRoot);
        Directory.CreateDirectory(saveDirectory);
        return saveDirectory;
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
