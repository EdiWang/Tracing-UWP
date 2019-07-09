using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using GalaSoft.MvvmLight.Command;
using Tracing.Annotations;
using Tracing.Configuration;
using Tracing.Controls;
using Tracing.Helpers;
using Tracing.Models;
using Tracing.ViewModels;

namespace Tracing.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private MainViewModel ViewModel => DataContext as MainViewModel;

        public MainPage()
        {
            Settings = App.AppSettings;
            InitializeComponent();

            WindowsShareHelper = new WindowsShareHelper(
                Edi.UWP.Helpers.Utils.GetResource("Resources/MyTracingWork"),
                Edi.UWP.Helpers.Utils.GetResource("Resources/MainPage_DtmOnDataRequested_ShareTitle"));

            SetDefaultInkBehaviour();
            InkReplayer = new InkReplayer(Ink, BtnInkReplay, BtnStopReplay, ReplayProgress,
                () => ViewModel.Document.Status == DocumentStatus.Saved,
                async () =>
                {
                    await new MessageDialog("Replay Ink Requires Saving Ink File First.",
                        Edi.UWP.Helpers.Utils.GetResource("Resources/InkReplay-Save-Your-Work")).ShowAsync();
                });
            InitSurfaceDiao();

            CommandZoomFromMenuItem = new RelayCommand<string>(ZoomTo);
            CurrentZoomFucktor = 1.ToString("#%");
            CanvasPixelWidth = Settings.DefaultCanvasWidth.ToString();
            CanvasPixelHeight = Settings.DefaultCanvasHeight.ToString();

            BorderFadeInStoryboard.Completed += async (o, _) =>
            {
                await Task.Delay(1500);
                BorderFadeOutStoryboard.Begin();
            };

            Clipboard.ContentChanged += (sender, o) =>
            {
                try
                {
                    ViewModel.CanPasteInk = ViewModel.InkOperator.InkCanvas.InkPresenter.StrokeContainer.CanPasteFromClipboard();
                }
                catch (Exception)
                {
                    // Sometimes get Access Denied from system.
                    // Eat the exception, pretend everything is fine.
                    // to avoid being 1 stared by the users.
                }
            };

            //Windows.UI.Core.Preview.SystemNavigationManagerPreview.GetForCurrentView().CloseRequested +=
            //    async (sender, args) =>
            //    {
            //        args.Handled = true;
            //        var hasUnsavedChanges = ViewModel.Document.Status == DocumentStatus.Modified;

            //        if (!isCloseButtonClicked && hasUnsavedChanges)
            //        {
            //            isCloseButtonClicked = true;

            //            var result = await DigSaveConfirm.ShowAsync();
            //            if (result == ContentDialogResult.Primary)
            //            {
            //                // Save work;
            //                await ViewModel.SaveCurrentSessionAsync();
            //                App.Current.Exit();
            //            }
            //            else if (result == ContentDialogResult.Secondary)
            //            {
            //                App.Current.Exit();
            //            }
            //            else
            //            {
            //                isCloseButtonClicked = false;
            //            }
            //        }
            //        else
            //        {
            //            App.Current.Exit();
            //        }
            //    };
        }

        private bool isCloseButtonClicked = false;

        #region Initialize

        public AppSettings Settings { get; set; }

        public bool HasDataInitialzed { get; set; }

        private void SetDefaultInkBehaviour()
        {
            Ink.InkPresenter.StrokeInput.StrokeStarted += (sender, args) =>
            {
                if (IsBoundRectPresent)
                {
                    ClearSelection();
                }
            };
            Ink.InkPresenter.InputProcessingConfiguration.RightDragAction = InkInputRightDragAction.LeaveUnprocessed;
            Ink.InkPresenter.UnprocessedInput.PointerPressed += UnprocessedInput_PointerPressed;
            Ink.InkPresenter.UnprocessedInput.PointerMoved += UnprocessedInput_PointerMoved;
            Ink.InkPresenter.UnprocessedInput.PointerReleased += UnprocessedInput_PointerReleased;

            RefreshPenSettings();
        }

        #endregion

        #region Title Bar

        private CoreApplicationViewTitleBar _coreTitleBar = CoreApplication.GetCurrentView().TitleBar;

        public Thickness CoreTitleBarPadding
        {
            get
            {
                if (ApplicationView.GetForCurrentView().IsFullScreenMode)
                {
                    return new Thickness(0, 0, 0, 0);
                }
                return FlowDirection == FlowDirection.LeftToRight ?
                    new Thickness { Left = _coreTitleBar.SystemOverlayLeftInset, Right = _coreTitleBar.SystemOverlayRightInset } :
                    new Thickness { Left = _coreTitleBar.SystemOverlayRightInset, Right = _coreTitleBar.SystemOverlayLeftInset };
            }
        }

        public double CoreTitleBarHeight => _coreTitleBar.Height;

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            _coreTitleBar.LayoutMetricsChanged += (bar, args) =>
            {
                NotifyLayoutChanges();
            };
            Window.Current.SizeChanged += (o, args) =>
            {
                NotifyLayoutChanges();
            };
            Window.Current.Activated += (wd, args) =>
            {
                TitleBar.Opacity = args.WindowActivationState != CoreWindowActivationState.Deactivated ? 1 : 0.5;
            };

            NotifyLayoutChanges();
            if (!HasDataInitialzed)
            {
                ViewModel?.InitDataAsync(Ink);
                HasDataInitialzed = true;
            }
        }

        private void NotifyLayoutChanges()
        {
            OnPropertyChanged(nameof(CoreTitleBarHeight));
            OnPropertyChanged(nameof(CoreTitleBarPadding));
        }

        #endregion

        #region Surface Diao

        private SurfaceDial SurfaceDiao { get; set; }

        private void InitSurfaceDiao()
        {
            if (!Settings.EnableSurfaceDial)
            {
                return;
            }

            try
            {
                SurfaceDiao = new SurfaceDial();
            }
            catch (Exception e)
            {
                var msg = Edi.UWP.Helpers.Utils.GetResource("Tracing.Core/Resources/FailedInitiateSurfaceDial") +
                                                            e.Message;
                MessageNotification.Show(msg);
            }

            // Menu Selection Message
            SurfaceDiao.OpacityInvoked += (sender, args) =>
            {
                TxtMessage.Text = Edi.UWP.Helpers.Utils.GetResource("Tracing.Core/Resources/OpacitySelected");
                BorderFadeInStoryboard.Begin();
            };
            SurfaceDiao.UndoRedoInvoked += (sender, args) =>
            {
                TxtMessage.Text = Edi.UWP.Helpers.Utils.GetResource("Tracing.Core/Resources/UndoRedoSelected");
                BorderFadeInStoryboard.Begin();
            };
            SurfaceDiao.ZoomInvoked += (sender, args) =>
            {
                TxtMessage.Text = Edi.UWP.Helpers.Utils.GetResource("Tracing.Core/Resources/ZoomSelected");
                BorderFadeInStoryboard.Begin();
            };
            SurfaceDiao.AlignmentGridInvoked += (sender, args) =>
            {
                TxtMessage.Text = Edi.UWP.Helpers.Utils.GetResource("Tracing.Core/Resources/AlignmentGridSelected");
                BorderFadeInStoryboard.Begin();
            };

            // Rotation Handling
            SurfaceDiao.Zooming += (sender, args) =>
            {
                var zoomFucktor = CanvasScrollViewer.ZoomFactor;

                zoomFucktor += 0.01f * (args.RotationDeltaInDegrees < 0 ? -1 : 1);
                CanvasScrollViewer.ChangeView(0, 0, zoomFucktor);
            };
            SurfaceDiao.UndoRedo += (sender, args) =>
            {
                if (args.RotationDeltaInDegrees < 0)
                {
                    ViewModel.InkOperator.UndoLastStorke();
                }
                else
                {
                    ViewModel.InkOperator.RedoLastStorke();
                }
            };
            SurfaceDiao.OpacityChanging += (sender, args) =>
            {
                if (OpacitySlider.Value + args.RotationDeltaInDegrees > 100)
                {
                    OpacitySlider.Value = 100;
                    return;
                }
                if (OpacitySlider.Value + args.RotationDeltaInDegrees < 0)
                {
                    OpacitySlider.Value = 0;
                    return;
                }
                OpacitySlider.Value += args.RotationDeltaInDegrees;
            };
            SurfaceDiao.AlignmentGridChanging += (sender, args) =>
            {
                if (SldAlignmentGridSize.Value + args.RotationDeltaInDegrees > 128)
                {
                    SldAlignmentGridSize.Value = 128;
                    return;
                }
                if (SldAlignmentGridSize.Value + args.RotationDeltaInDegrees < 16)
                {
                    SldAlignmentGridSize.Value = 16;
                    return;
                }
                SldAlignmentGridSize.Value += args.RotationDeltaInDegrees;
            };
        }

        #endregion

        #region Toolbar Button Commands

        private async void BtnSampleImage_Click(object sender, RoutedEventArgs e)
        {
            await DigSampleGallery.ShowAsync();
        }

        #endregion

        #region File Association (.ink)

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _coreTitleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(TitleBarBackgroundElement);

            try
            {
                var args = e.Parameter as IActivatedEventArgs;
                if (args?.Kind == ActivationKind.File)
                {
                    if (!HasDataInitialzed)
                    {
                        ViewModel?.InitDataAsync(Ink, true);
                        HasDataInitialzed = true;
                    }

                    if (args is FileActivatedEventArgs fileArgs)
                    {
                        var file = (StorageFile)fileArgs.Files[0];
                        if (ViewModel != null) await ViewModel.OpenInkFile(file);
                    }
                }

                RefreshPenSettings();
                ViewModel?.SetupPrinting(this);
            }
            catch (Exception ex)
            {
                var msg = $"{typeof(MainPage).FullName}.OnNavigatedTo() blows up sky high: {ex.Message}, stack trace: {ex.StackTrace}";
                var dig = new MessageDialog(msg, Edi.UWP.Helpers.Utils.GetResource("Resources/MessageDialogTitle-Error"));
                await dig.ShowAsync();
            }

            // Useful to know when to initialize/clean up the camera
            Application.Current.Suspending += Application_Suspending;
            Application.Current.Resuming += Application_Resuming;

            IsActivePage = true;

        }

        private void RefreshPenSettings()
        {
            var attr = InkTB.InkDrawingAttributes ?? new InkDrawingAttributes();
            attr.IgnorePressure = !Settings.IsPressureEnabled;
            attr.IgnoreTilt = !Settings.EnablePenTilt;
            attr.FitToCurve = Settings.FitToCurve;
            Ink?.InkPresenter.UpdateDefaultDrawingAttributes(attr);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.UnregisterPrinting();
        }

        #endregion

        #region Drag and Drop Image

        private async void CanvasGrid_OnDrop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();

                if (items.Any())
                {
                    if (items[0] is StorageFile storageFile)
                    {
                        var contentType = storageFile.ContentType;

                        //StorageFolder folder = ApplicationData.Current.LocalFolder;

                        if (contentType == "image/png" ||
                            contentType == "image/jpeg" ||
                            contentType == "image/bmp" ||
                            contentType == "image/gif")
                        {
                            //StorageFile newFile = await storageFile.CopyAsync(folder, storageFile.Name, NameCollisionOption.GenerateUniqueName);
                            var bitmapImage = new BitmapImage();
                            bitmapImage.SetSource(await storageFile.OpenAsync(FileAccessMode.Read));
                            ViewModel.CurrentImageSource = bitmapImage;
                        }

                        if (string.IsNullOrEmpty(contentType) && storageFile.FileType == ".ink")
                        {
                            await ViewModel.OpenInkFile(storageFile);
                        }
                    }
                }
            }
        }

        private void CanvasGrid_OnDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (e.DragUIOverride != null)
            {
                e.DragUIOverride.Caption = Edi.UWP.Helpers.Utils.GetResource("Resources/HolyCowSupportDrag");
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
                e.DragUIOverride.SetContentFromBitmapImage(new BitmapImage(new Uri("ms-appx:///Assets/filelogo.png")));
            }
        }

        #endregion

        #region Windows Share

        public WindowsShareHelper WindowsShareHelper { get; set; }

        private async void BtnShare_OnClick(object sender, RoutedEventArgs e)
        {
            var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("Tracing_Temp.png", CreationCollisionOption.ReplaceExisting);
            var tempExportFile = await ViewModel.InkOperator.SaveInkImageToStorageFile(tempFile, Colors.Transparent);
            WindowsShareHelper.InkImageFile = tempExportFile;
            WindowsShareHelper.Share();
        }

        #endregion

        #region Advanced Color Picker

        private void DigAdvColor_OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            InkTB.ActiveTool = PenButton;
        }

        private void InkTB_OnLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel.UniversalPalette = PenButton.Palette;
            ResetUpdatePalette();
        }

        private async void AdvColor_OnClick(object sender, RoutedEventArgs e)
        {
            ViewModel.IsLockColor = true;
            await DigAdvColor.ShowAsync();
        }

        private void DigAdvColor_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var drawingAttributes = InkTB.InkDrawingAttributes;
            drawingAttributes.Color = PlainColorPicker.Color;
            Ink.InkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);

            if (ViewModel.IsLockColor)
            {
                ViewModel.CustomOverrideColor = PlainColorPicker.Color;
                ClearPalette();
            }
            else
            {
                ResetUpdatePalette();
            }
        }

        private void ResetUpdatePalette()
        {
            PenButton.Palette = ViewModel.UniversalPalette;
            CalligraphicPen.Palette = ViewModel.UniversalPalette;
            MarkerButton.Palette = ViewModel.UniversalPalette;
            PencilButton.Palette = ViewModel.UniversalPalette;
            // HighligherButton.Palette = ViewModel.UniversalPalette;
        }

        private void ClearPalette()
        {
            PenButton.Palette = null;
            CalligraphicPen.Palette = null;
            MarkerButton.Palette = null;
            PencilButton.Palette = null;
            // HighligherButton.Palette = null;
        }

        private void InkTB_OnInkDrawingAttributesChanged(InkToolbar sender, object args)
        {
            var drawingAttributes = InkTB.InkDrawingAttributes;
            if (ViewModel.IsLockColor)
            {
                drawingAttributes.Color = ViewModel.CustomOverrideColor;
            }
            else
            {
                PlainColorPicker.Color = sender.InkDrawingAttributes.Color;
            }
            drawingAttributes.IgnoreTilt = !Settings.EnablePenTilt;
            drawingAttributes.FitToCurve = Settings.FitToCurve;
            Ink.InkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);
        }

        private void InkTB_OnActiveToolChanged(InkToolbar sender, object args)
        {
            if (ViewModel.IsLockColor)
            {
                var drawingAttributes = InkTB.InkDrawingAttributes;
                drawingAttributes.Color = ViewModel.CustomOverrideColor;
                Ink.InkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);
            }
            else
            {
                PlainColorPicker.Color = sender.InkDrawingAttributes.Color;
            }

            if (IsBoundRectPresent)
            {
                ClearSelection();
            }
        }

        #endregion

        #region Resize

        public ICommand CommandZoomFromMenuItem { get; set; }

        public string CanvasPixelWidth
        {
            get => _canvasPixelWidth;
            set { _canvasPixelWidth = value; OnPropertyChanged(); }
        }

        public string CanvasPixelHeight
        {
            get => _canvasPixelHeight;
            set { _canvasPixelHeight = value; OnPropertyChanged(); }
        }

        public string CurrentZoomFucktor
        {
            get => _currentZoomFucktor;
            set { _currentZoomFucktor = value; OnPropertyChanged(); }
        }

        private void ZoomTo(string f)
        {
            var fucktor = float.Parse(f);
            CurrentZoomFucktor = fucktor.ToString("#%");
            CanvasScrollViewer.ChangeView(0, 0, fucktor);
        }

        private void CanvasScrollViewer_OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var zoomFucktor = CanvasScrollViewer.ZoomFactor;
            CurrentZoomFucktor = zoomFucktor.ToString("#%");
        }

        private void ZoomFitScreen_OnClick(object sender, RoutedEventArgs e)
        {
            var targetZoomFucktor = 1f;
            var currentFrameSize = new Size(CanvasGrid.ActualWidth, CanvasGrid.ActualHeight);
            var currentCanvasSize = new Size(Ink.Width, Ink.Height);

            var b = currentFrameSize.Width > currentFrameSize.Height
                ? currentFrameSize.Height
                : currentFrameSize.Width;

            var a = currentCanvasSize.Width > currentCanvasSize.Height
                ? currentCanvasSize.Height
                : currentCanvasSize.Width;

            targetZoomFucktor = (float)(b / a);
            CanvasScrollViewer.ChangeView(0, 0, targetZoomFucktor);
        }

        private void BtnApplyNewCanvasSize_OnClick(object sender, RoutedEventArgs e)
        {
            int h = Settings.DefaultCanvasHeight;
            int w = Settings.DefaultCanvasWidth;

            if (!string.IsNullOrEmpty(CanvasPixelHeight) && !string.IsNullOrEmpty(CanvasPixelWidth))
            {
                var b1 = int.TryParse(CanvasPixelHeight, out h);
                var b2 = int.TryParse(CanvasPixelWidth, out w);

                if (b1 && b2 && h > 0 && w > 0)
                {
                    AmazingCanvas.Width = w;
                    AmazingCanvas.Height = h;
                }
            }
        }

        private void ImageStretchMenuItemNone_OnClick(object sender, RoutedEventArgs e)
        {
            Img.Stretch = Stretch.None;
        }

        private void ImageStretchMenuItemFill_OnClick(object sender, RoutedEventArgs e)
        {
            Img.Stretch = Stretch.Fill;
        }

        private void ImageStretchMenuItemUniform_OnClick(object sender, RoutedEventArgs e)
        {
            Img.Stretch = Stretch.Uniform;
        }

        private void ImageStretchMenuItemUniformToFill_OnClick(object sender, RoutedEventArgs e)
        {
            Img.Stretch = Stretch.UniformToFill;
        }

        public bool IsZoomingCanvasByTouch { get; set; }

        private void AmazingCanvas_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            IsZoomingCanvasByTouch = !IsZoomingCanvasByTouch;
            if (IsZoomingCanvasByTouch)
            {
                MessageNotification.Show(Edi.UWP.Helpers.Utils.GetResource("Resources/TouchZoomEnabled"), 5000);
                Canvas.SetZIndex(Ink, ViewModel.IsLockInkManipulationMode ? 0 : 10);
            }
            else
            {
                MessageNotification.Show(Edi.UWP.Helpers.Utils.GetResource("Resources/TouchZoomDisabled"), 5000);
                Canvas.SetZIndex(Ink, 10);
            }
        }

        private void Ink_OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            if (ViewModel.IsLockInkManipulationMode && !IsZoomingCanvasByTouch)
            {
                MessageNotification.Show(Edi.UWP.Helpers.Utils.GetResource("Resources/TouchZoomWarning"), 3000);
            }
        }

        private async void ink_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            try
            {
                if (!ViewModel.IsLockInkManipulationMode)
                {
                    var scale = Matrix3x2.CreateScale(e.Delta.Scale);
                    var transform = Matrix3x2.CreateTranslation((float)-e.Position.X, (float)-e.Position.Y) *
                                    scale *
                                    Matrix3x2.CreateRotation((float)(e.Delta.Rotation / 180 * Math.PI)) *
                                    Matrix3x2.CreateTranslation((float)e.Position.X, (float)e.Position.Y) *
                                    Matrix3x2.CreateTranslation((float)e.Delta.Translation.X,
                                        (float)e.Delta.Translation.Y);

                    List<Rect> individualBoundingRects = new List<Rect>();

                    var targetStrokes = IsBoundRectPresent
                        ? Ink.InkPresenter.StrokeContainer.GetStrokes().Where(s => s.Selected).ToList()
                        : Ink.InkPresenter.StrokeContainer.GetStrokes();

                    foreach (var stroke in targetStrokes)
                    {
                        individualBoundingRects.Add(stroke.BoundingRect);

                        var attr = stroke.DrawingAttributes;
                        // Fix for pencil storke movement blowup. Avoid being 1 stared in the store.
                        if (attr.Kind != InkDrawingAttributesKind.Pencil)
                        {
                            attr.PenTipTransform *= scale;
                            stroke.DrawingAttributes = attr;
                        }

                        stroke.PointTransform *= transform;
                    }

                    if (IsBoundRectPresent)
                    {
                        var newRect = individualBoundingRects.First();
                        for (int i = 1; i < individualBoundingRects.Count; i++)
                        {
                            newRect.Union(individualBoundingRects[i]);
                        }

                        SelectionGrid.Width = newRect.Width + 4;
                        SelectionGrid.Height = newRect.Height + 4;

                        Canvas.SetLeft(SelectionGrid, newRect.X);
                        Canvas.SetTop(SelectionGrid, newRect.Y);
                    }
                }
            }
            catch (Exception ex)
            {
                var dig = new MessageDialog(ex.Message, Edi.UWP.Helpers.Utils.GetResource("Resources/MessageDialogTitle-Error"));
                await dig.ShowAsync();
            }
        }

        #endregion

        #region .GIF Handling

        private async void BtnOpenGifFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Let users choose their ink file using a file picker.
                // Initialize the picker.
                var openPicker =
                    new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                openPicker.FileTypeFilter.Add(".gif");
                openPicker.FileTypeFilter.Add(".isf");

                // Show the file picker.
                var file = await openPicker.PickSingleFileAsync();
                // User selects a file and picker returns a reference to the selected file.
                if (file != null)
                {
                    // Open a file stream for reading.
                    var stream = await file.OpenAsync(FileAccessMode.Read);
                    // Read from file.
                    using (var inputStream = stream.GetInputStreamAt(0))
                    {
                        await Ink.InkPresenter.StrokeContainer.LoadAsync(inputStream);
                    }
                    stream.Dispose();
                }
                // User selects Cancel and picker returns null.
            }
            catch (Exception ex)
            {
                var msg = $"{ex.Message}";
                var dig = new MessageDialog(msg, Edi.UWP.Helpers.Utils.GetResource("Resources/MessageDialogTitle-Error"));
                await dig.ShowAsync();
            }
        }

        #endregion

        #region Cavas Background Color

        private async void BtnBackgroundColor_OnClick(object sender, RoutedEventArgs e)
        {
            await DigCvsBgColor.ShowAsync();
        }

        private void DigCvsBgColor_OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ViewModel.CurrentCanvasBackgroundBrush = PlainColorPickerCvsBgColor.SolidColorBrush;
        }

        #endregion

        #region Ink Replay

        public InkReplayer InkReplayer { get; set; }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            InkReplayer.StopReplay();

            Application.Current.Suspending -= Application_Suspending;
            Application.Current.Resuming -= Application_Resuming;
            // await SetUpBasedOnStateAsync();
        }

        #endregion

        #region Lasso Selection

        private Polyline _lasso;
        private Rect _boundingRect;
        private bool _isBoundRect;

        private bool _isBoundRectPresent;

        public bool IsBoundRectPresent
        {
            get => _isBoundRectPresent;
            set { _isBoundRectPresent = value; OnPropertyChanged(); }
        }

        private void UnprocessedInput_PointerPressed(InkUnprocessedInput sender, PointerEventArgs args)
        {
            _lasso = new Polyline
            {
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            _lasso.Points?.Add(args.CurrentPoint.RawPosition);
            AmazingCanvas.Children.Add(_lasso);
            _isBoundRect = true;
        }

        private void UnprocessedInput_PointerMoved(InkUnprocessedInput sender, PointerEventArgs args)
        {
            if (_isBoundRect)
            {
                _lasso.Points?.Add(args.CurrentPoint.RawPosition);
            }
        }

        private void UnprocessedInput_PointerReleased(InkUnprocessedInput sender, PointerEventArgs args)
        {
            if (_lasso.Points != null)
            {
                _lasso.Points.Add(args.CurrentPoint.RawPosition);
                _boundingRect = Ink.InkPresenter.StrokeContainer.SelectWithPolyLine(_lasso.Points);
            }
            _isBoundRect = false;
            DrawBoundingRect();
        }

        private void DrawBoundingRect()
        {
            var polylines = AmazingCanvas.Children.Where(c => c is Polyline);
            foreach (var item in polylines)
            {
                AmazingCanvas.Children.Remove(item);
            }

            if (_boundingRect.Width <= 0 || _boundingRect.Height <= 0)
            {
                return;
            }

            SelectionGrid.Width = _boundingRect.Width + 4;
            SelectionGrid.Height = _boundingRect.Height + 4;

            Canvas.SetLeft(SelectionGrid, _boundingRect.X);
            Canvas.SetTop(SelectionGrid, _boundingRect.Y);

            SelectionGrid.Visibility = Visibility.Visible;
            IsBoundRectPresent = true;
        }

        private void ClearSelection()
        {
            var strokes = Ink.InkPresenter.StrokeContainer.GetStrokes();
            foreach (var stroke in strokes)
            {
                stroke.Selected = false;
            }
            ClearDrawnBoundingRect();
            IsBoundRectPresent = false;
        }

        private void ClearDrawnBoundingRect()
        {
            SelectionGrid.Height = 0;
            SelectionGrid.Width = 0;
            SelectionGrid.Visibility = Visibility.Collapsed;
            _boundingRect = Rect.Empty;
        }

        private void BtnMoveLeft_OnClick(object sender, RoutedEventArgs e)
        {
            MoveInk(-1, 0);
        }

        private void BtnMoveRight_OnClick(object sender, RoutedEventArgs e)
        {
            MoveInk(1, 0);
        }

        private void BtnMoveTop_OnClick(object sender, RoutedEventArgs e)
        {
            MoveInk(0, -1);
        }

        private void BtnMoveDown_OnClick(object sender, RoutedEventArgs e)
        {
            MoveInk(0, 1);
        }

        private void MoveInk(int deltaX, int deltaY)
        {
            _boundingRect.X += deltaX;
            _boundingRect.Y += deltaY;

            Canvas.SetLeft(SelectionGrid, _boundingRect.X);
            Canvas.SetTop(SelectionGrid, _boundingRect.Y);

            var newPoint = new Point(
                (CanvasScrollViewer.HorizontalOffset + deltaX) / CanvasScrollViewer.ZoomFactor,
                (CanvasScrollViewer.VerticalOffset + deltaY) / CanvasScrollViewer.ZoomFactor);

            ViewModel.InkOperator.InkCanvas.InkPresenter.StrokeContainer.MoveSelected(newPoint);
        }

        #endregion

        #region Copy / Paste Ink

        private bool _isCtrlKeyPressed;
        private string _currentZoomFucktor;
        private string _canvasPixelWidth;
        private string _canvasPixelHeight;

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            ViewModel.InkOperator.InkCanvas.InkPresenter.StrokeContainer.CopySelectedToClipboard();
        }

        private void LayoutRoot_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Control) _isCtrlKeyPressed = false;
        }

        private async void LayoutRoot_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Control) _isCtrlKeyPressed = true;
            else if (_isCtrlKeyPressed)
            {
                switch (e.Key)
                {
                    case VirtualKey.V: await PasteInkOrImageFromClipboard(); break;
                    case VirtualKey.Z: ViewModel.CommandUndo.Execute(null); break;
                    case VirtualKey.Y: ViewModel.CommandRedo.Execute(null); break;
                    case VirtualKey.S: ViewModel.CommandSaveCurrent.Execute(null); break;
                }
            }
        }

        private async Task PasteInkOrImageFromClipboard()
        {
            DataPackageView dataPackageView = Clipboard.GetContent();
            if (!ViewModel.CanPasteInk &&
                dataPackageView.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = await dataPackageView.GetBitmapAsync();
                IRandomAccessStream irac = await bitmap.OpenReadAsync();
                BitmapImage img = new BitmapImage();
                img.SetSource(irac);
                Img.Source = img;
            }

            if (ViewModel.CanPasteInk)
            {
                var x = (CanvasScrollViewer.HorizontalOffset + 10) / CanvasScrollViewer.ZoomFactor;
                var y = (CanvasScrollViewer.VerticalOffset + 10) / CanvasScrollViewer.ZoomFactor;
                ViewModel.InkOperator.InkCanvas.InkPresenter.StrokeContainer.PasteFromClipboard(new Point(x, y));
            }
        }

        private async void BtnPasteInk_Click(object sender, RoutedEventArgs e)
        {
            await PasteInkOrImageFromClipboard();
        }

        #endregion

        #region Helper

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region ToolBar

        void OnBringIntoView(object sender, RoutedEventArgs e)
        {
            // Set stencil origin to Scrollviewer Viewport origin.
            // The purpose of this behavior is to allow the user to "grab" the
            // stencil and bring it into view no matter where the scrollviewer viewport
            // happens to be.  Note that this is accomplished by a simple translation
            // that adjusts to the zoom factor.  The additional ZoomFactor term is to
            // ensure the scale of the InkPresenterStencil is invariant to Zoom.
            Matrix3x2 viewportTransform =
                Matrix3x2.CreateScale(CanvasScrollViewer.ZoomFactor) *
                Matrix3x2.CreateTranslation(
                    (float)CanvasScrollViewer.HorizontalOffset,
                    (float)CanvasScrollViewer.VerticalOffset) *
                Matrix3x2.CreateScale(1.0f / CanvasScrollViewer.ZoomFactor);

            var stencilButton = (InkToolbarStencilButton)InkTB.GetMenuButton(InkToolbarMenuKind.Stencil);
            switch (stencilButton.SelectedStencil)
            {
                case InkToolbarStencilKind.Protractor:
                    stencilButton.Protractor.Transform = viewportTransform;
                    break;

                case InkToolbarStencilKind.Ruler:
                    stencilButton.Ruler.Transform = viewportTransform;
                    break;
            }
        }

        private async void BtnFeedback_Click(object sender, RoutedEventArgs e)
        {
            var launcher = Microsoft.Services.Store.Engagement.StoreServicesFeedbackLauncher.GetDefault();
            await launcher.LaunchAsync();
        }

        private void CopyRGB_Plain_Click(object sender, RoutedEventArgs e)
        {
            Edi.UWP.Helpers.Utils.CopyToClipBoard(PlainPicker_TextRGB.Text);
        }

        private void CopyHEX_Plain_Click(object sender, RoutedEventArgs e)
        {
            Edi.UWP.Helpers.Utils.CopyToClipBoard(PlainPicker_TextHEX.Text);
        }

        private void CopyCMYK_Plain_Click(object sender, RoutedEventArgs e)
        {
            Edi.UWP.Helpers.Utils.CopyToClipBoard(PlainPicker_TextCMYK.Text);
        }

        private void InkTB_OnIsStencilButtonCheckedChanged(InkToolbar sender, InkToolbarIsStencilButtonCheckedChangedEventArgs args)
        {
            var stencilButton = (InkToolbarStencilButton)InkTB.GetMenuButton(InkToolbarMenuKind.Stencil);
            BringIntoViewButton.IsEnabled = stencilButton.IsChecked.Value;
        }

        #endregion

        #region Camera

        private static readonly Guid RotationKey = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");

        public StorageFolder CaptureFolder { get; private set; } = null;

        private readonly DisplayRequest _displayRequest = new DisplayRequest();

        private readonly SystemMediaTransportControls _systemMediaControls = SystemMediaTransportControls.GetForCurrentView();

        public MediaCapture MediaCapture { get; private set; }

        public bool IsCameraInitialized { get; private set; }

        public bool IsPreviewing { get; private set; }

        // UI state
        private bool _isSuspending;

        public bool IsActivePage { get; private set; }

        public bool IsUiActive { get; private set; }
        private Task _setupTask = Task.CompletedTask;

        // Information about the camera device
        private bool _mirroringPreview;
        private bool _externalCamera;

        // Rotation Helper to simplify handling rotation compensation for the camera streams
        private CameraRotationHelper _rotationHelper;

        private async void BtnTakePicture_Click(object sender, RoutedEventArgs e)
        {
            var b = await Helper.IsCameraPresent();
            if (b)
            {
                PreviewControl.Visibility = Visibility.Visible;
                await SetUpBasedOnStateAsync();
            }
            else
            {
                MessageNotification.Show(Edi.UWP.Helpers.Utils.GetResource("Resources/CameraNotFoundDetail"), 3000);
            }
        }

        private async void PreviewControl_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            await TakePhotoAsync();
        }

        private void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            _isSuspending = false;

            var deferral = e.SuspendingOperation.GetDeferral();
            var task = Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
            {
                await SetUpBasedOnStateAsync();
                deferral.Complete();
            });
        }

        private void Application_Resuming(object sender, object o)
        {
            _isSuspending = false;

            var task = Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
            {
                await SetUpBasedOnStateAsync();
            });
        }

        #region Event handlers

        /// <summary>
        /// In the event of the app being minimized this method handles media property change events. If the app receives a mute
        /// notification, it is no longer in the foregroud.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void SystemMediaControls_PropertyChanged(SystemMediaTransportControls sender, SystemMediaTransportControlsPropertyChangedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                // Only handle this event if this page is currently being displayed
                if (args.Property == SystemMediaTransportControlsProperty.SoundLevel && Frame.CurrentSourcePageType == typeof(MainPage))
                {
                    // Check to see if the app is being muted. If so, it is being minimized.
                    // Otherwise if it is not initialized, it is being brought into focus.
                    if (sender.SoundLevel == SoundLevel.Muted)
                    {
                        await CleanupCameraAsync();
                    }
                    else if (!IsCameraInitialized)
                    {
                        await InitializeCameraAsync();
                    }
                }
            });
        }

        private async void MediaCapture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            await CleanupCameraAsync();
        }

        #endregion Event handlers

        #region MediaCapture methods

        private async Task InitializeCameraAsync()
        {
            if (MediaCapture == null)
            {
                var cameraDevice = await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back);

                if (cameraDevice == null)
                {
                    return;
                }

                MediaCapture = new MediaCapture();
                MediaCapture.Failed += MediaCapture_Failed;

                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = cameraDevice.Id
                };

                try
                {
                    await MediaCapture.InitializeAsync(settings);
                    IsCameraInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    await new MessageDialog("The app was denied access to the camera", "ERROR").ShowAsync();
                }

                if (IsCameraInitialized)
                {
                    if (cameraDevice.EnclosureLocation == null || cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        _externalCamera = true;
                    }
                    else
                    {
                        _externalCamera = false;
                        _mirroringPreview = (cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
                    }

                    _rotationHelper = new CameraRotationHelper(cameraDevice.EnclosureLocation);
                    _rotationHelper.OrientationChanged += RotationHelper_OrientationChanged;
                    await StartPreviewAsync();
                }
            }
        }

        private async void RotationHelper_OrientationChanged(object sender, bool updatePreview)
        {
            if (updatePreview)
            {
                await SetPreviewRotationAsync();
            }
        }

        private async Task StartPreviewAsync()
        {
            _displayRequest.RequestActive();

            PreviewControl.Source = MediaCapture;
            PreviewControl.FlowDirection = _mirroringPreview ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            await MediaCapture.StartPreviewAsync();
            IsPreviewing = true;

            if (IsPreviewing)
            {
                await SetPreviewRotationAsync();
            }
        }


        private async Task SetPreviewRotationAsync()
        {
            if (_externalCamera) return;
            var rotation = _rotationHelper.GetCameraPreviewOrientation();
            var props = MediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            props.Properties.Add(RotationKey, CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(rotation));
            await MediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, null);
        }

        private async Task StopPreviewAsync()
        {
            IsPreviewing = false;
            await MediaCapture.StopPreviewAsync();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                PreviewControl.Source = null;
                _displayRequest.RequestRelease();
            });
        }

        private async Task TakePhotoAsync()
        {
            var stream = new InMemoryRandomAccessStream();
            await MediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);

            var appPhotoFolder = await CaptureFolder.CreateFolderAsync("Tracing", CreationCollisionOption.OpenIfExists);
            var file = await appPhotoFolder.CreateFileAsync($"Tracing_Background_{DateTime.Now:yyyyMMddHHmmss}.jpg", CreationCollisionOption.GenerateUniqueName);
            var photoOrientation = CameraRotationHelper.ConvertSimpleOrientationToPhotoOrientation(_rotationHelper.GetCameraCaptureOrientation());
            await ReencodeAndSavePhotoAsync(stream, file, photoOrientation);

            await CleanupCameraAsync();
            PreviewControl.Visibility = Visibility.Collapsed;

            var bitmapImage = new BitmapImage();
            bitmapImage.SetSource(await file.OpenAsync(FileAccessMode.Read));
            ViewModel.CurrentImageSource = bitmapImage;

            ViewModel.InkOperator.ApplyImageFile(file);
            ViewModel.UpdateDocument();
        }

        private async Task CleanupCameraAsync()
        {
            if (IsCameraInitialized)
            {
                if (IsPreviewing)
                {
                    await StopPreviewAsync();
                }

                IsCameraInitialized = false;
            }

            if (MediaCapture != null)
            {
                MediaCapture.Failed -= MediaCapture_Failed;
                MediaCapture.Dispose();
                MediaCapture = null;
            }

            if (_rotationHelper != null)
            {
                _rotationHelper.OrientationChanged -= RotationHelper_OrientationChanged;
                _rotationHelper = null;
            }
        }

        #endregion MediaCapture methods

        #region Helper functions

        private async Task SetUpBasedOnStateAsync()
        {
            while (!_setupTask.IsCompleted)
            {
                await _setupTask;
            }

            bool wantUiActive = Window.Current.Visible && !_isSuspending;

            async Task SetupAsync()
            {
                if (wantUiActive)
                {
                    await SetupUiAsync();
                    await InitializeCameraAsync();
                }
                else
                {
                    await CleanupCameraAsync();
                    CleanupUiAsync();
                }
            }

            _setupTask = SetupAsync();
            await _setupTask;
        }

        private async Task SetupUiAsync()
        {
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
            _systemMediaControls.PropertyChanged += SystemMediaControls_PropertyChanged;
            var picturesLibrary = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
            CaptureFolder = picturesLibrary.SaveFolder ?? ApplicationData.Current.LocalFolder;
        }

        private void CleanupUiAsync()
        {
            _systemMediaControls.PropertyChanged -= SystemMediaControls_PropertyChanged;
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.None;
        }

        private static async Task<DeviceInformation> FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            DeviceInformation desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == desiredPanel);
            return desiredDevice ?? allVideoDevices.FirstOrDefault();
        }

        private static async Task ReencodeAndSavePhotoAsync(IRandomAccessStream stream, StorageFile file, PhotoOrientation photoOrientation)
        {
            using (var inputStream = stream)
            {
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                using (var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);

                    var properties = new BitmapPropertySet {
                    {
                        "System.Photo.Orientation", new BitmapTypedValue(photoOrientation, PropertyType.UInt16)
                    } };

                    await encoder.BitmapProperties.SetPropertiesAsync(properties);
                    await encoder.FlushAsync();
                }
            }
        }

        #endregion Helper functions

        #endregion
    }
}