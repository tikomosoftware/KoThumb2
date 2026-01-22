using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.IO;

namespace KoThumb2
{
    public class LogItem
    {
        public string Message { get; set; } = string.Empty;
        public string? OriginalPath { get; set; }
        public override string ToString() => Message;
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private System.Windows.Media.Imaging.BitmapSource? _previewImage;
        public System.Windows.Media.Imaging.BitmapSource? PreviewImage
        {
            get => _previewImage;
            set { _previewImage = value; OnPropertyChanged(); }
        }

        private ProcessSettings _settings = new();
        public ProcessSettings Settings
        {
            get => _settings;
            set { _settings = value; OnPropertyChanged(); UpdateFilenamePreview(); }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set 
            { 
                _isProcessing = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsNotProcessing));
                OnPropertyChanged(nameof(CompletionVisibility));
            }
        }
        public bool IsNotProcessing => !IsProcessing;

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private double _progressMax = 100;
        public double ProgressMax
        {
            get => _progressMax;
            set { _progressMax = value; OnPropertyChanged(); }
        }

        private string _progressText = "待機中";
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public int SelectedExtensionIndex
        {
            get
            {
                return Settings.FileExtensions switch
                {
                    "*.jpg;*.jpeg" => 1,
                    "*.png" => 2,
                    "*.webp" => 3,
                    "*.bmp" => 4,
                    "*.gif" => 5,
                    _ => 0
                };
            }
            set
            {
                Settings.FileExtensions = value switch
                {
                    1 => "*.jpg;*.jpeg",
                    2 => "*.png",
                    3 => "*.webp",
                    4 => "*.bmp",
                    5 => "*.gif",
                    _ => "*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tiff"
                };
                OnPropertyChanged();
            }
        }

        public ObservableCollection<LogItem> LogItems { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // UI Helpers
        public bool IsResizeMode
        {
            get => Settings.Mode == ConversionMode.Resize;
            set { Settings.Mode = value ? ConversionMode.Resize : ConversionMode.FormatOnly; OnPropertyChanged(); OnPropertyChanged(nameof(ResizeGroupVisibility)); }
        }
        public bool IsFormatOnlyMode
        {
            get => Settings.Mode == ConversionMode.FormatOnly;
            set { Settings.Mode = value ? ConversionMode.FormatOnly : ConversionMode.Resize; OnPropertyChanged(); OnPropertyChanged(nameof(ResizeGroupVisibility)); }
        }
        public Visibility ResizeGroupVisibility => IsResizeMode ? Visibility.Visible : Visibility.Collapsed;

        public bool IsResizeByWidth
        {
            get => Settings.ResizeBase == ResizeBase.Width;
            set { if (value) Settings.ResizeBase = ResizeBase.Width; OnPropertyChanged(); }
        }
        public bool IsResizeByHeight
        {
            get => Settings.ResizeBase == ResizeBase.Height;
            set { if (value) Settings.ResizeBase = ResizeBase.Height; OnPropertyChanged(); }
        }
        public bool IsResizeByLongSide
        {
            get => Settings.ResizeBase == ResizeBase.LongSide;
            set { if (value) Settings.ResizeBase = ResizeBase.LongSide; OnPropertyChanged(); }
        }

        public int SelectedAlgorithmIndex
        {
            get => (int)Settings.Algorithm;
            set { Settings.Algorithm = (ResizeAlgorithm)value; OnPropertyChanged(); }
        }

        public int SelectedFormatIndex
        {
            get => (int)Settings.Format;
            set 
            { 
                Settings.Format = (OutputFormat)value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(JpegSettingsVisibility));
                OnPropertyChanged(nameof(PngSettingsVisibility));
                OnPropertyChanged(nameof(WebpSettingsVisibility));
                UpdateFilenamePreview();
            }
        }

        public Visibility JpegSettingsVisibility => Settings.Format == OutputFormat.JPG ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PngSettingsVisibility => Settings.Format == OutputFormat.PNG ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WebpSettingsVisibility => Settings.Format == OutputFormat.WebP ? Visibility.Visible : Visibility.Collapsed;

        public string JpegQualityLabel => $"品質: {Settings.JpegQuality}";
        public string PngCompressionLabel => $"圧縮レベル: {Settings.PngCompressionLevel}";
        public string WebpQualityLabel => $"品質: {Settings.WebpQuality}";

        // Filename Rule UI
        public bool IsFilenameKeep { get { return Settings.FilenameRule == FilenameRule.Keep; } set { if (value) Settings.FilenameRule = FilenameRule.Keep; OnPropertyChanged(nameof(PrefixInputVisibility)); OnPropertyChanged(nameof(SuffixInputVisibility)); OnPropertyChanged(nameof(SerialInputVisibility)); UpdateFilenamePreview(); } }
        public bool IsFilenamePrefix { get { return Settings.FilenameRule == FilenameRule.Prefix; } set { if (value) Settings.FilenameRule = FilenameRule.Prefix; OnPropertyChanged(nameof(PrefixInputVisibility)); OnPropertyChanged(nameof(SuffixInputVisibility)); OnPropertyChanged(nameof(SerialInputVisibility)); UpdateFilenamePreview(); } }
        public bool IsFilenameSuffix { get { return Settings.FilenameRule == FilenameRule.Suffix; } set { if (value) Settings.FilenameRule = FilenameRule.Suffix; OnPropertyChanged(nameof(PrefixInputVisibility)); OnPropertyChanged(nameof(SuffixInputVisibility)); OnPropertyChanged(nameof(SerialInputVisibility)); UpdateFilenamePreview(); } }
        public bool IsFilenameSerial { get { return Settings.FilenameRule == FilenameRule.Serial; } set { if (value) Settings.FilenameRule = FilenameRule.Serial; OnPropertyChanged(nameof(PrefixInputVisibility)); OnPropertyChanged(nameof(SuffixInputVisibility)); OnPropertyChanged(nameof(SerialInputVisibility)); UpdateFilenamePreview(); } }

        public string Prefix
        {
            get => Settings.Prefix;
            set { Settings.Prefix = value; OnPropertyChanged(); UpdateFilenamePreview(); }
        }

        public string Suffix
        {
            get => Settings.Suffix;
            set { Settings.Suffix = value; OnPropertyChanged(); UpdateFilenamePreview(); }
        }

        public string SerialPattern
        {
            get => Settings.SerialPattern;
            set { Settings.SerialPattern = value; OnPropertyChanged(); UpdateFilenamePreview(); }
        }

        public Visibility PrefixInputVisibility => Settings.FilenameRule == FilenameRule.Prefix ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SuffixInputVisibility => Settings.FilenameRule == FilenameRule.Suffix ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SerialInputVisibility => Settings.FilenameRule == FilenameRule.Serial ? Visibility.Visible : Visibility.Collapsed;

        public int SelectedConflictIndex
        {
            get => Settings.ConflictResolution == ConflictResolution.Skip ? 1 : 0;
            set { Settings.ConflictResolution = value == 1 ? ConflictResolution.Skip : ConflictResolution.AutoRename; OnPropertyChanged(); }
        }

        public string FilenamePreviewLabel
        {
            get
            {
                string baseName = "image_sample";
                string ext = Settings.Format switch
                {
                    OutputFormat.PNG => ".png",
                    OutputFormat.WebP => ".webp",
                    _ => ".jpg"
                };

                string result = Settings.FilenameRule switch
                {
                    FilenameRule.Prefix => Settings.Prefix + baseName,
                    FilenameRule.Suffix => baseName + Settings.Suffix,
                    FilenameRule.Serial => string.Format(Settings.SerialPattern, 1),
                    _ => baseName
                };

                return $"例: {baseName}.jpg  →  {result}{ext}";
            }
        }

        public void UpdateFilenamePreview()
        {
            OnPropertyChanged(nameof(FilenamePreviewLabel));
        }

        private bool _isCompleted;
        public Visibility CompletionVisibility => (_isCompleted && !IsProcessing) ? Visibility.Visible : Visibility.Collapsed;

        public bool IsAlwaysOnTop
        {
            get => Settings.AlwaysOnTop;
            set { Settings.AlwaysOnTop = value; OnPropertyChanged(); }
        }

        public void SetCompleted(bool completed)
        {
            _isCompleted = completed;
            OnPropertyChanged(nameof(CompletionVisibility));
        }
    }
}
