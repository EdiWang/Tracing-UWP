using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Edi.UWP.Helpers;
using Edi.UWP.Helpers.Extensions;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Views;
using Tracing.Core;
using Tracing.Helpers;
using Tracing.Models;
using Tracing.Printing;

namespace Tracing.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private string _currentDocumentTitle;
        private ImageSource _currentImageSource;
        private readonly PickerLocationId _defaultInkFilePickerLocation = PickerLocationId.DocumentsLibrary;
        private TracingDocument _document;
        private bool _isFullScreen;
        private bool _isInitialized;
        private bool _isLockColor;
        private bool _isLockInkManipulationMode;
        private bool _isMouseDrawingEnabled;
        private bool _isToggleTouchEnabled;
        private bool _isToggleMouseEnabled;
        private bool _isTouchDrawingEnabled;
        private ObservableCollection<ImageSource> _sampleImages;
        readonly TouchCapabilities _touchCapabilities = new TouchCapabilities();
        private int _totalStrokes;
        private bool _isBlockingBusy;
        private int _imagePixelHeight;
        private int _imagePixelWidth;
        private bool _canPasteInk;
        private ImageSource _selectedSampleImage;
        private Color _customOverrideColor;
        private ObservableCollection<SolidColorBrush> _recentBindingColors;
        private bool _isInkToShapeOn;
        private SolidColorBrush _currentCanvasBackgroundBrush;
        private string _imageResolution;
        private SolidColorBrush _currentLockedColorBrush;

        public MainViewModel(IDialogService dialogService)
        {
            DialogService = dialogService;

            CommandToggleFullScreen = new RelayCommand(ToggleFullScreenMode);
            CommandPrint = new RelayCommand(async () => await DoPrintAsync());
            CommandExportInkToImage = new RelayCommand<ImageSavingOptions>(async opt => await SaveImageAsync(opt));
            CommandSaveAsInkFile =
                new RelayCommand(async () => await SaveAsInkFileAsync(_defaultInkFilePickerLocation));
            CommandSaveAsGifFile =
                new RelayCommand(async () => await SaveAsGifFileAsync(_defaultInkFilePickerLocation));
            CommandSaveCurrent = new RelayCommand(async () => await SaveCurrentSessionAsync());
            CommandUndo = new RelayCommand(() => { InkOperator?.UndoLastStorke(); });
            CommandRedo = new RelayCommand(() => { InkOperator?.RedoLastStorke(); });
            CommandLoadInkFile = new RelayCommand(async () => await OpenInkFile(_defaultInkFilePickerLocation));
            ClearImageSource = new RelayCommand(() => { CurrentImageSource = null; });
            CommandClearAll = new RelayCommand(async () => await DoClearAll());
            CommandPickImageFile = new RelayCommand(async () => await DoPickImageFile());
            CommandApplySampleImageSelection = new RelayCommand(async () => await ApplySampleImageSelection());
            CommandPickRecentColor = new RelayCommand<SolidColorBrush>(DoPickRecentColor);

            IsFullScreen = false;

            Document = new TracingDocument
            {
                Type = DocumentType.TempOrNew,
                Status = DocumentStatus.Saved
            };

            RecentColors = new Queue<Color>();
            CurrentCanvasBackgroundBrush = new SolidColorBrush(Colors.White);

            SampleImages = new ObservableCollection<ImageSource>(new List<ImageSource>());
            for (var i = 1; i <= 6; i++)
            {
                SampleImages.Add(new BitmapImage(new Uri($"ms-appx:///Assets/Samples/{i}.jpg")));
            }
        }

        private void DoPickRecentColor(SolidColorBrush solidColorBrush)
        {
            var color = solidColorBrush.Color;
            CurrentLockedColorBrush = solidColorBrush;
            var orgAttr = InkOperator.InkCanvas.InkPresenter.CopyDefaultDrawingAttributes();
            orgAttr.Color = color;
            InkOperator.InkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(orgAttr);
        }

        private async Task DoPickImageFile()
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var stream = await file.OpenAsync(FileAccessMode.Read);
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);
                    CurrentImageSource = bitmapImage;

                    ImagePixelHeight = bitmapImage.PixelHeight;
                    ImagePixelWidth = bitmapImage.PixelWidth;

                    RaisePropertyChanged(nameof(ImageResolution));

                    var tempFolder = ApplicationData.Current.TemporaryFolder;
                    var copiedFile = await file.CopyAsync(tempFolder, $"Tracing_Temp{file.FileType}", NameCollisionOption.ReplaceExisting);
                    InkOperator.ApplyImageFile(copiedFile);
                }
            }
            catch (Exception ex)
            {
                var msg = $"{nameof(DoPickImageFile)}(): {ex.Message}";
                await DialogService.ShowMessageBox(msg,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }
        }

        public Queue<Color> RecentColors { get; set; }

        public ObservableCollection<SolidColorBrush> RecentBindingColors
        {
            get => _recentBindingColors;
            set
            {
                _recentBindingColors = value.Reverse().ToObservableCollection();
                RaisePropertyChanged();
            }
        }

        public RelayCommand<SolidColorBrush> CommandPickRecentColor { get; set; }

        public string ImageResolution
        {
            get => $"{ImagePixelWidth} x {ImagePixelHeight}";
            set { _imageResolution = value; RaisePropertyChanged(); }
        }

        public int ImagePixelHeight
        {
            get => _imagePixelHeight;
            set { _imagePixelHeight = value; RaisePropertyChanged(); }
        }

        public int ImagePixelWidth
        {
            get => _imagePixelWidth;
            set { _imagePixelWidth = value; RaisePropertyChanged(); }
        }

        public int TotalStrokes
        {
            get => _totalStrokes;
            set
            {
                _totalStrokes = value;
                RaisePropertyChanged();
            }
        }

        public bool CanPasteInk
        {
            get => _canPasteInk;
            set { _canPasteInk = value; RaisePropertyChanged(); }
        }

        public ImageSource CurrentImageSource
        {
            get => _currentImageSource;
            set
            {
                _currentImageSource = value;
                RaisePropertyChanged();
            }
        }

        public TracingDocument Document
        {
            get => _document;
            set
            {
                _document = value;
                RaisePropertyChanged();
            }
        }

        public string CurrentDocumentTitle
        {
            get => _currentDocumentTitle;
            set
            {
                _currentDocumentTitle = value;
                RaisePropertyChanged();
            }
        }

        public IList<Brush> UniversalPalette { get; set; }

        private PhotosPrintHelper PrintHelper { get; set; }

        public IDialogService DialogService { get; set; }

        public SolidColorBrush CurrentCanvasBackgroundBrush
        {
            get => _currentCanvasBackgroundBrush;
            set { _currentCanvasBackgroundBrush = value; RaisePropertyChanged(); }
        }

        public bool IsMouseDrawingEnabled
        {
            get => _isMouseDrawingEnabled;
            set
            {
                _isMouseDrawingEnabled = value;
                if (value)
                {
                    InkOperator.InkCanvas.InkPresenter.InputDeviceTypes |= CoreInputDeviceTypes.Mouse;
                }
                else
                {
                    InkOperator.InkCanvas.InkPresenter.InputDeviceTypes &= ~CoreInputDeviceTypes.Mouse;
                }

                RaisePropertyChanged();
            }
        }

        public bool IsTouchDrawingEnabled
        {
            get => _isTouchDrawingEnabled;
            set
            {
                _isTouchDrawingEnabled = value;
                if (value)
                {
                    InkOperator.InkCanvas.InkPresenter.InputDeviceTypes |= CoreInputDeviceTypes.Touch;
                }
                else
                {
                    InkOperator.InkCanvas.InkPresenter.InputDeviceTypes &= ~CoreInputDeviceTypes.Touch;
                }

                RaisePropertyChanged();
            }
        }

        public bool IsToggleTouchEnabled
        {
            get => _isToggleTouchEnabled;
            set
            {
                _isToggleTouchEnabled = value;
                RaisePropertyChanged();
            }
        }

        public bool IsToggleMouseEnabled
        {
            get => _isToggleMouseEnabled;
            set
            {
                _isToggleMouseEnabled = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLockInkManipulationMode
        {
            get => _isLockInkManipulationMode;
            set
            {
                _isLockInkManipulationMode = value;
                if (value)
                {
                    //InkOperator.InkCanvas.ManipulationMode = ManipulationModes.Scale;

                    IsToggleTouchEnabled = GetTouchProperties();
                    IsToggleMouseEnabled = true;

                    IsTouchDrawingEnabled = false;
                    IsMouseDrawingEnabled = false;
                }
                else
                {
                    //InkOperator.InkCanvas.ManipulationMode = ManipulationModes.All;

                    IsToggleTouchEnabled = false;
                    IsToggleMouseEnabled = false;

                    IsTouchDrawingEnabled = false;
                    IsMouseDrawingEnabled = false;
                }
                RaisePropertyChanged();
            }
        }

        public bool IsBlockingBusy
        {
            get => _isBlockingBusy;
            set { _isBlockingBusy = value; RaisePropertyChanged(); }
        }

        public Color CustomOverrideColor
        {
            get => _customOverrideColor;
            set
            {
                _customOverrideColor = value;
                if (RecentColors.Count > 5)
                {
                    RecentColors.Dequeue();
                }
                RecentColors.Enqueue(value);
                RecentBindingColors = RecentColors.Select(p => new SolidColorBrush(p))
                                                  .ToObservableCollection();
                RaisePropertyChanged();

                CurrentLockedColorBrush = new SolidColorBrush(value);
            }
        }

        public SolidColorBrush CurrentLockedColorBrush
        {
            get => _currentLockedColorBrush;
            set { _currentLockedColorBrush = value; RaisePropertyChanged(); }
        }

        public InkOperator InkOperator { get; set; }

        public AutoSaveManager AutoSaveManager { get; set; }

        public RelayCommand CommandToggleFullScreen { get; set; }

        public RelayCommand CommandPickImageFile { get; set; }

        public RelayCommand CommandPrint { get; set; }

        public RelayCommand<ImageSavingOptions> CommandExportInkToImage { get; set; }

        public RelayCommand CommandSaveAsInkFile { get; set; }

        public RelayCommand CommandSaveAsGifFile { get; set; }

        public RelayCommand CommandUndo { get; set; }

        public RelayCommand CommandRedo { get; set; }

        public RelayCommand CommandLoadInkFile { get; set; }

        public RelayCommand CommandSaveCurrent { get; set; }

        public RelayCommand ClearImageSource { get; set; }

        public RelayCommand CommandClearAll { get; set; }

        public RelayCommand CommandApplySampleImageSelection { get; set; }

        public bool IsFullScreen
        {
            get => _isFullScreen;
            set
            {
                _isFullScreen = value;
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<ImageSource> SampleImages
        {
            get => _sampleImages;
            set
            {
                _sampleImages = value;
                RaisePropertyChanged();
            }
        }

        public ImageSource SelectedSampleImage
        {
            get => _selectedSampleImage;
            set
            {
                _selectedSampleImage = value;
                RaisePropertyChanged();
            }
        }

        public bool IsLockColor
        {
            get => _isLockColor;
            set
            {
                _isLockColor = value;
                if (value)
                {
                }
                RaisePropertyChanged();
            }
        }

        public async Task ApplySampleImageSelection()
        {
            CurrentImageSource = SelectedSampleImage;
            var selectedItem = (BitmapImage)SelectedSampleImage;
            if (selectedItem != null)
            {
                var uri = selectedItem.UriSource;
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                if (null != file)
                {
                    ImagePixelWidth = selectedItem.PixelWidth;
                    ImagePixelHeight = selectedItem.PixelHeight;
                    Document.ImageFile = file;
                    InkOperator.ApplyImageFile(file);

                    RaisePropertyChanged(nameof(ImageResolution));
                }
            }
        }

        private async Task DoClearAll()
        {
            var title = Utils.GetResource("Resources/Confirm");
            var message = Utils.GetResource("Resources/ClearAllWarning");
            var confirmText = Utils.GetResource("Resources/BtnClearAllConfirm");
            var cancelText = Utils.GetResource("Resources/BtnClearAllCancel");

            await DialogService.ShowMessage(message, title, confirmText, cancelText,
                b =>
                {
                    if (!b) return;
                    CurrentImageSource = null;
                    InkOperator.InkCanvas.InkPresenter.StrokeContainer.Clear();
                });
        }

        public async Task InitDataAsync(InkCanvas canvas, bool skipTempInkFile = false)
        {
            IsBlockingBusy = true;
            InkOperator = new InkOperator(canvas);

            AutoSaveManager = new AutoSaveManager(InkOperator);
            if (!skipTempInkFile)
                await AutoSaveManager.InitializeTempInkFile();
            _isInitialized = true;
            UpdateDocument();

            InkOperator.InkCanvas.InkPresenter.StrokesCollected += (sender, args) =>
            {
                Document.Status = DocumentStatus.Modified;
                TotalStrokes = InkOperator.InkCanvas.InkPresenter.StrokeContainer.GetStrokes().Count;
            };
            InkOperator.InkCanvas.InkPresenter.StrokesErased += (sender, args) =>
            {
                Document.Status = DocumentStatus.Modified;
                TotalStrokes = InkOperator.InkCanvas.InkPresenter.StrokeContainer.GetStrokes().Count;
            };

            IsLockInkManipulationMode = true;
            IsToggleTouchEnabled = GetTouchProperties();
            IsBlockingBusy = false;
        }

        public void UpdateDocument()
        {
            Document.InkFile = InkOperator.CurrentInkFile;
            Document.ImageFile = InkOperator.CurrentTargetImageFile;
            CurrentDocumentTitle = $" - {Document.Title}";
        }

        public void SetupPrinting(Page scenarioPage)
        {
            PrintHelper = new PhotosPrintHelper(scenarioPage);
            PrintHelper.RegisterForPrinting();
        }

        public void UnregisterPrinting()
        {
            PrintHelper.UnregisterForPrinting();
        }

        #region Command Logic

        private void ToggleFullScreenMode()
        {
            var view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode)
            {
                view.ExitFullScreenMode();
            }
            else
            {
                view.TryEnterFullScreenMode();
            }
        }

        public bool IsInkToShapeOn
        {
            get => _isInkToShapeOn;
            set
            {
                _isInkToShapeOn = value;
                InkOperator.InkToShapeAssKicker.IsOn = value;
                RaisePropertyChanged();
            }
        }

        private async Task OpenInkFile(PickerLocationId location)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = location
                };
                picker.FileTypeFilter.Add(".ink");
                var pickedFile = await picker.PickSingleFileAsync();
                if (pickedFile != null)
                    await OpenInkFile(pickedFile);
            }
            catch (Exception ex)
            {
                var msg = $"{ex.Message}";
                await DialogService.ShowMessageBox(msg,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }
        }

        public async Task OpenInkFile(StorageFile sFile)
        {
            IsBlockingBusy = true;
            try
            {
                await InkOperator.ApplyInkFile(sFile);
                Document.Type = DocumentType.Existing;
                UpdateDocument();
                IsBlockingBusy = false;
            }
            catch (Exception ex)
            {
                IsBlockingBusy = false;
                var msg = $"{typeof(MainViewModel).FullName}.OpenInkFile() :{ex.Message}";
                await DialogService.ShowMessageBox(msg,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }

            IsLockInkManipulationMode = true;
        }

        private async Task DoPrintAsync()
        {
            try
            {
                PrintHelper.RBitmap = await InkOperator.SaveInkImageToBitmap();
                await PrintHelper.ShowPrintUIAsync();
            }
            catch (Exception ex)
            {
                var msg = $"{ex.Message}";
                await DialogService.ShowMessageBox(msg,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }
        }

        private async Task SaveAsGifFileAsync(PickerLocationId location)
        {
            IsBlockingBusy = true;
            try
            {
                var result = await InkOperator.SaveInkToGif(location);
                if (result)
                {
                    var msg = Utils.GetResource("Resources/SaveSuccess");
                }
                else
                {
                    await DialogService.ShowMessageBox(Utils.GetResource("Resources/MessageDialogTitle-Error"),
                        Utils.GetResource("Resources/NoInk"));
                }
                IsBlockingBusy = false;
            }
            catch (Exception ex)
            {
                IsBlockingBusy = false;
                await DialogService.ShowMessageBox(ex.Message,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }
        }

        public async Task SaveCurrentSessionAsync()
        {
            if (Document.Type == DocumentType.TempOrNew)
            {
                //await AutoSaveManager.SaveTempSession();
                var response = await SaveAsInkFileAsync(_defaultInkFilePickerLocation);
                if (response.IsSuccess)
                {
                    await OpenInkFile(response.Item);
                }
            }
            else
            {
                await InkOperator.SaveInkToStorageFile();
            }
            Document.Status = DocumentStatus.Saved;
        }

        private async Task<Response<StorageFile>> SaveAsInkFileAsync(PickerLocationId location)
        {
            IsBlockingBusy = true;
            try
            {
                var result = await InkOperator.SaveInkToInkFile(location);
                if (result.IsSuccess)
                {
                    Document.Status = DocumentStatus.Saved;
                }
                else
                {
                    await DialogService.ShowMessageBox(Utils.GetResource("Resources/MessageDialogTitle-Error"),
                        Utils.GetResource("Resources/NoInk"));
                }
                IsBlockingBusy = false;
                return result;
            }
            catch (Exception ex)
            {
                IsBlockingBusy = false;
                await DialogService.ShowMessageBox(ex.Message,
                    Utils.GetResource("Resources/MessageDialogTitle-Error"));
                return new Response<StorageFile>();
            }
        }

        private async Task SaveImageAsync(ImageSavingOptions option)
        {
            IsBlockingBusy = true;
            try
            {
                var fileName = Utils.GetResource("Resources/SaveImageFileNamePrefix") +
                               DateTime.Now.ToString("yyyy-MM-dd-HHmmss");

                FileUpdateStatus result = FileUpdateStatus.Incomplete;

                switch (option)
                {
                    case ImageSavingOptions.InkWithColoredBackground:
                        result = await InkOperator.ExportInkToImageFile(PickerLocationId.PicturesLibrary, fileName, CurrentCanvasBackgroundBrush.Color);
                        break;
                    case ImageSavingOptions.InkWithTransparentBackground:
                        result = await InkOperator.ExportInkToImageFile(PickerLocationId.PicturesLibrary, fileName, Colors.Transparent);
                        break;
                }

                if (result == FileUpdateStatus.Complete)
                {
                    var msg = Utils.GetResource("Resources/SaveSuccess");
                }
                else
                {
                    await DialogService.ShowMessageBox(result.ToString(),
                        Utils.GetResource("Resources/MessageDialogTitle-Error"));
                }

                IsBlockingBusy = false;
            }
            catch (Exception ex)
            {
                IsBlockingBusy = false;
                var msg = $"{ex.Message}";
                await DialogService.ShowMessageBox(msg, Utils.GetResource("Resources/MessageDialogTitle-Error"));
            }
        }

        private bool GetTouchProperties()
        {
            return _touchCapabilities.TouchPresent != 0;
        }

        #endregion
    }
}