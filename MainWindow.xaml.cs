using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace KoThumb2
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ImageProcessor _processor;
        private CancellationTokenSource? _cts;
        private const string SettingsFile = "settings.json";

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            _processor = new ImageProcessor();
            DataContext = _viewModel;

            LoadSettings();
        }

        private void BtnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "入力フォルダを選択してください"
            };
            if (dialog.ShowDialog() == true)
            {
                if (CheckFolderRelationship(dialog.FolderName, _viewModel.Settings.OutputFolder))
                {
                    System.Windows.MessageBox.Show("【警告】現在の出力フォルダと親子関係にあるフォルダは選択できません。別のフォルダを選択してください。", "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _viewModel.Settings.InputFolder = dialog.FolderName;
                _viewModel.Settings = _viewModel.Settings; 
                _viewModel.UpdateFilenamePreview();
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "出力フォルダを選択してください"
            };
            if (dialog.ShowDialog() == true)
            {
                if (CheckFolderRelationship(_viewModel.Settings.InputFolder, dialog.FolderName))
                {
                    System.Windows.MessageBox.Show("【警告】現在の入力フォルダと親子関係にあるフォルダは選択できません。別のフォルダを選択してください。", "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _viewModel.Settings.OutputFolder = dialog.FolderName;
                _viewModel.Settings = _viewModel.Settings;
                _viewModel.UpdateFilenamePreview();
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.LogItems.Clear();

            if (string.IsNullOrWhiteSpace(_viewModel.Settings.InputFolder) || !Directory.Exists(_viewModel.Settings.InputFolder))
            {
                System.Windows.MessageBox.Show( "有効な入力フォルダを選択してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_viewModel.Settings.OutputFolder))
            {
                System.Windows.MessageBox.Show("出力フォルダを選択してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (CheckFolderRelationship(_viewModel.Settings.InputFolder, _viewModel.Settings.OutputFolder))
            {
                System.Windows.MessageBox.Show("【エラー】入力フォルダと出力フォルダが同じ、または親子関係（サブフォルダ）にあります。\n\n" +
                    "この状態で処理を実行すると、無限ループや予期せぬ上書きが発生する危険があるため、実行できません。\n" +
                    "別の出力フォルダを選択してください。", 
                    "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // フォーマット変換のみモードかつ、拡張子フィルタが単一指定されている場合のチェック
            if (_viewModel.Settings.Mode == ConversionMode.FormatOnly)
            {
                bool isSameFormat = false;
                string filter = _viewModel.Settings.FileExtensions.ToLower();
                OutputFormat target = _viewModel.Settings.Format;

                if (filter == "*.jpg;*.jpeg" && target == OutputFormat.JPG) isSameFormat = true;
                else if (filter == "*.png" && target == OutputFormat.PNG) isSameFormat = true;
                else if (filter == "*.webp" && target == OutputFormat.WebP) isSameFormat = true;

                if (isSameFormat)
                {
                    var res = System.Windows.MessageBox.Show("【確認】入力と出力のフォーマットが同じ、かつ『リサイズなし』の設定です。画像が変化しない可能性がありますが、処理を開始しますか？", "設定の確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res == MessageBoxResult.No) return;
                }
            }

            SaveSettings();

            _viewModel.IsProcessing = true;
            _viewModel.ProgressValue = 0;
            _viewModel.ProgressText = "準備中...";
            _viewModel.SetCompleted(false);

            _cts = new CancellationTokenSource();

            try
            {
                await _processor.ProcessImagesAsync(
                    _viewModel.Settings,
                    (current, total, fileName, fullPath) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _viewModel.ProgressMax = total;
                            _viewModel.ProgressValue = current;
                            _viewModel.ProgressText = $"{current} / {total} ({fileName})";
                            _viewModel.LogItems.Insert(0, new LogItem { Message = $"[OK] {fileName}", OriginalPath = fullPath });
                            // Limit log size
                            if (_viewModel.LogItems.Count > 100) _viewModel.LogItems.RemoveAt(100);
                        });
                    },
                    (success, failed, errors) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _viewModel.IsProcessing = false;
                            _viewModel.ProgressText = $"完了: 成功 {success}, 失敗 {failed}";
                            _viewModel.SetCompleted(true);

                            foreach (var err in errors)
                            {
                                _viewModel.LogItems.Insert(0, new LogItem { Message = $"[ERR] {err}" });
                            }

                            System.Windows.MessageBox.Show($"処理が完了しました。\n成功: {success}\n失敗: {failed}", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    },
                    _cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                _viewModel.ProgressText = "キャンセルされました";
                _viewModel.LogItems.Insert(0, new LogItem { Message = "[INFO] 処理がキャンセルされました" });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"予期せぬエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _viewModel.IsProcessing = false;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void BtnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_viewModel.Settings.OutputFolder))
            {
                Process.Start("explorer.exe", _viewModel.Settings.OutputFolder);
            }
        }
        private async void LogList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LogList.SelectedItem is LogItem selectedItem && !string.IsNullOrEmpty(selectedItem.OriginalPath))
            {
                await GeneratePreviewAsync(selectedItem.OriginalPath);
            }
        }

        private async Task GeneratePreviewAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                byte[] data = await _processor.ProcessPreviewAsync(filePath, _viewModel.Settings);
                using var ms = new MemoryStream(data);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                _viewModel.PreviewImage = bitmap;
            }
            catch { /* 静かに無視 */ }
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string path = files[0];
                    if (Directory.Exists(path))
                    {
                        if (CheckFolderRelationship(path, _viewModel.Settings.OutputFolder))
                        {
                            System.Windows.MessageBox.Show("【警告】現在の設定（またはデフォルト設定）では、入力フォルダと出力フォルダが親子関係になってしまいます。別の場所を選択してください。", "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        _viewModel.Settings.InputFolder = path;
                        // Auto set output folder if empty
                        if (string.IsNullOrEmpty(_viewModel.Settings.OutputFolder))
                        {
                            string autoOutputDir = Path.Combine(path, "processed");
                            // 自分の直下も親子関係になるので、ここでは設定しないか、警告を出す
                            // ユーザーの要望通りに「親子関係を禁止」するなら、デスクトップ等別の場所を促すべき
                            _viewModel.Settings.OutputFolder = string.Empty; 
                            System.Windows.MessageBox.Show("入力フォルダを設定しました。出力フォルダは、入力フォルダの「外側」の場所を指定してください。", "出力フォルダの設定", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        _viewModel.Settings = _viewModel.Settings;
                        _viewModel.UpdateFilenamePreview();
                    }
                }
            }
        }

        private void FolderTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0 && Directory.Exists(files[0]))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void FolderTextBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string path = files[0];
                    if (Directory.Exists(path))
                    {
                        var textBox = sender as System.Windows.Controls.TextBox;
                        if (textBox?.Tag?.ToString() == "Input")
                        {
                            if (CheckFolderRelationship(path, _viewModel.Settings.OutputFolder))
                            {
                                System.Windows.MessageBox.Show("【警告】現在の出力フォルダと親子関係にあるフォルダは選択できません。", "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            _viewModel.Settings.InputFolder = path;
                        }
                        else if (textBox?.Tag?.ToString() == "Output")
                        {
                            if (CheckFolderRelationship(_viewModel.Settings.InputFolder, path))
                            {
                                System.Windows.MessageBox.Show("【警告】現在の入力フォルダと親子関係にあるフォルダは選択できません。", "フォルダ設定のエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            _viewModel.Settings.OutputFolder = path;
                        }
                        
                        _viewModel.Settings = _viewModel.Settings; // UI更新通知
                        _viewModel.UpdateFilenamePreview();
                    }
                }
            }
            e.Handled = true;
        }

        private void LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<ProcessSettings>(json);
                    if (settings != null)
                    {
                        _viewModel.Settings = settings;
                        // Refresh UI bindings
                        _viewModel.IsResizeMode = settings.Mode == ConversionMode.Resize;
                        _viewModel.SelectedFormatIndex = (int)settings.Format;
                        _viewModel.UpdateFilenamePreview();
                    }
                }
                catch { /* Ignore load error */ }
            }
        }

        private bool CheckFolderRelationship(string input, string output)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output)) return false;

            try
            {
                string path1 = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                string path2 = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

                if (path1 == path2) return true;

                // 末尾にセパレータを追加して、部分一致（C:\Images と C:\Images2 など）を防ぐ
                string p1 = path1 + Path.DirectorySeparatorChar;
                string p2 = path2 + Path.DirectorySeparatorChar;

                // どちらかがどちらかの配下にあるかチェック
                return p1.StartsWith(p2) || p2.StartsWith(p1);
            }
            catch { return false; }
        }

        private void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(_viewModel.Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { /* Ignore save error */ }
        }
    }
}