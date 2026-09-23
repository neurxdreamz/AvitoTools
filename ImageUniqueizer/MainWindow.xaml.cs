using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ImageUniqueizer;

public partial class MainWindow : Window
{
    private static Brush Brush(string hex) =>
        (Brush)new BrushConverter().ConvertFromInvariantString(hex)!;

    private readonly List<string> _selectedFiles = [];
    private string? _outputDirectory;
    private CancellationTokenSource? _cts;
    private bool _isProcessing;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    public MainWindow()
    {
        InitializeComponent();
        DropZone.PreviewMouseLeftButtonDown += DropZone_PreviewMouseLeftButtonDown;
        DropZone.Drop += DropZone_Drop;
        DropZone.DragEnter += DropZone_DragEnter;
        DropZone.DragOver += DropZone_DragOver;
    }

    private void DropZone_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isProcessing) return;
        OpenFileChooser();
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (_isProcessing)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (_isProcessing)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (_isProcessing) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            AddFiles(files);
        }
    }

    private void OpenFileChooser()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите фотографии товаров",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp|Все файлы|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (SupportedExtensions.Contains(ext) && !_selectedFiles.Contains(file))
            {
                _selectedFiles.Add(file);
            }
        }

        UpdateFileCount();
    }

    private void UpdateFileCount()
    {
        FileCountText.Text = $"Выбрано файлов: {_selectedFiles.Count}";
        ClearFilesButton.Visibility = _selectedFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProcessButton.IsEnabled = _selectedFiles.Count > 0 && !_isProcessing;
    }

    private void ClearFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _selectedFiles.Clear();
        UpdateFileCount();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения результатов"
        };

        if (dialog.ShowDialog() == true)
        {
            _outputDirectory = dialog.FolderName;
            OutputPathText.Text = _outputDirectory;
            OutputPathText.Foreground = Brush("#CDD6F4");
        }
    }

    private async void ProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing || _selectedFiles.Count == 0) return;

        if (!int.TryParse(SetsCountBox.Text.Trim(), out int setsCount) || setsCount <= 0)
        {
            MessageBox.Show("Введите корректное количество наборов (целое число > 0).",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            // Default: Output folder next to the first source image
            var firstDir = Path.GetDirectoryName(_selectedFiles[0]);
            _outputDirectory = Path.Combine(firstDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Output");
            OutputPathText.Text = _outputDirectory;
            OutputPathText.Foreground = Brush("#CDD6F4");
        }

        SetProcessingState(true);

        _cts = new CancellationTokenSource();
        var progress = new Progress<int>(percent =>
        {
            ProgressBar.Value = percent;
            StatusText.Text = $"Обработано {_selectedFiles.Count * setsCount * percent / 100} из {_selectedFiles.Count * setsCount}... ({percent}%)";
        });

        try
        {
            var result = await BatchProcessor.ProcessBatchAsync(
                _selectedFiles,
                _outputDirectory,
                setsCount,
                progress,
                _cts.Token);

            if (result.Errors.Count > 0)
            {
                var errorMsg = string.Join("\n", result.Errors.Take(10));
                if (result.Errors.Count > 10)
                    errorMsg += $"\n... и ещё {result.Errors.Count - 10} ошибок";

                StatusText.Text = $"Готово! Успешно: {result.SuccessCount}/{result.TotalImages}.\nОшибки:\n{errorMsg}";
                StatusText.Foreground = Brush("#FAB387");
            }
            else
            {
                StatusText.Text = $"Готово! Все {result.SuccessCount} изображений успешно обработаны.";
                StatusText.Foreground = Brush("#A6E3A1");
            }

            StatusText.Visibility = Visibility.Visible;
            OpenResultButton.Visibility = Visibility.Visible;
            OpenResultButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Обработка отменена.";
            StatusText.Foreground = Brush("#F9E2AF");
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка: {ex.Message}";
            StatusText.Foreground = Brush("#F38BA8");
            StatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            SetProcessingState(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetProcessingState(bool processing)
    {
        _isProcessing = processing;
        ProcessButton.IsEnabled = !processing && _selectedFiles.Count > 0;
        SetsCountBox.IsEnabled = !processing;
        BrowseButton.IsEnabled = !processing;
        ClearFilesButton.IsEnabled = !processing;

        if (processing)
        {
            ProgressBorder.Visibility = Visibility.Visible;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Подготовка...";
            StatusText.Foreground = Brush("#8A8A99");
        }
        else
        {
            ProgressBorder.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_outputDirectory) && Directory.Exists(_outputDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputDirectory,
                UseShellExecute = true
            });
        }
    }
}
