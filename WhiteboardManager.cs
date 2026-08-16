using Aspose.Slides;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using D = DocumentFormat.OpenXml.Drawing;
using IOPath = System.IO.Path;
using P = DocumentFormat.OpenXml.Presentation;

namespace ShowWrite
{
    public class WhiteboardManager : INotifyPropertyChanged, IDisposable
    {
        private bool _isWhiteboardMode = false;
        private Border? _whiteboardBackground;
        private StackPanel? _whiteboardModeButtons;
        private StackPanel? _whiteboardPageButtons;
        private Border? _pagePanel;
        private TextBlock? _pageInfoText;
        private int _whiteboardCurrentPage = 1;
        private int _whiteboardTotalPages = 1;
        private List<List<InkStroke>> _whiteboardPages = new List<List<InkStroke>> { new List<InkStroke>() };
        private List<string?> _pageBackgrounds = new List<string?> { null };
        private SvgIcon? _nextPagePath;
        private TextBlock? _nextPageText;
        private bool _pagePanelOpen = false;
        private ObservableCollection<WhiteboardPageThumbnail> _whiteboardPageThumbnails = new();
        private InkCanvas _inkCanvas;
        private CameraService? _cameraService;
        private Image _videoImage;
        private Border? _importingOverlay;
        private List<InkStroke> _videoStrokesBackup = new();
        private bool _hasWhiteboardData = false;

        // 图片图层相关字段
        private Canvas? _imageOverlayCanvas;
        private Image? _draggingImage;
        private Point _dragStartPoint;
        private TranslateTransform _dragStartTransform = new();
        private Image? _selectedImage;
        private Border? _selectionBorder;
        // 每页的图片列表（与白板页面对应）
        private List<List<Image>> _pageImageOverlays = new();
        // 缩放手柄
        private List<Border> _scaleHandles = new();
        private Border? _activeHandle;
        private Point _handleStartPoint;
        private double _initialWidth;
        private double _initialHeight;
        private double _initialX;
        private double _initialY;

        // PptService fields
        private PptService? _pptService;
        private bool _isPowerPointMode = false;

        // PptXmlService fields
        private bool _isPptXmlMode = false;

        // AsposeSlidesService fields
        private Aspose.Slides.Presentation? _asposePresentation;
        private int _asposeCurrentIndex;

        public bool IsWhiteboardMode => _isWhiteboardMode;
        public int CurrentPage => _whiteboardCurrentPage;
        public int TotalPages => _whiteboardTotalPages;
        public ObservableCollection<WhiteboardPageThumbnail> WhiteboardPageThumbnails => _whiteboardPageThumbnails;

        // AsposeSlidesService properties
        public bool IsAsposePresentationOpen => _asposePresentation != null;
        public int AsposeSlideCount => _asposePresentation?.Slides.Count ?? 0;
        public int AsposeCurrentIndex => _asposeCurrentIndex;

        // PptService properties
        public bool IsPowerPointMode => _isPowerPointMode;
        public bool IsPowerPointOpen => _pptService?.IsOpen ?? false;
        public int PowerPointSlideCount => _pptService?.SlideCount ?? 0;
        public int PowerPointCurrentSlide => _pptService?.CurrentSlideIndex ?? 1;

        // PptXmlService properties
        public bool IsPptXmlMode => _isPptXmlMode;
        public int PptXmlSlideCount => _pptService?.PptXmlSlideCount ?? 0;
        public int PptXmlCurrentIndex => _pptService?.PptXmlCurrentIndex ?? 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? WhiteboardModeEntered;
        public event Action? WhiteboardModeExited;
        public event Action? ImportPptRequested;
        public event Action<PptSlideControl>? PptSlideControlReady;
        private PptSlideControl? _pendingPptSlideControl;
        public PptSlideControl? PendingPptSlideControl => _pendingPptSlideControl;
        public PptService.PptSlide? CurrentPptSlide => _pptService?.GetCurrentPptSlide();

        public WhiteboardManager(
            InkCanvas inkCanvas,
            Image videoImage,
            CameraService? cameraService)
        {
            _inkCanvas = inkCanvas;
            _videoImage = videoImage;
            _cameraService = cameraService;
        }

        public void Initialize(
            Border whiteboardBackground,
            StackPanel whiteboardModeButtons,
            StackPanel whiteboardPageButtons,
            Border pagePanel,
            TextBlock pageInfoText,
            SvgIcon nextPagePath,
            TextBlock nextPageText,
            Border importingOverlay,
            Canvas imageOverlayCanvas)
        {
            _whiteboardBackground = whiteboardBackground;
            _whiteboardModeButtons = whiteboardModeButtons;
            _whiteboardPageButtons = whiteboardPageButtons;
            _pagePanel = pagePanel;
            _pageInfoText = pageInfoText;
            _nextPagePath = nextPagePath;
            _nextPageText = nextPageText;
            _importingOverlay = importingOverlay;
            _imageOverlayCanvas = imageOverlayCanvas;
        }

        public void EnterWhiteboardMode(
            StackPanel normalBottomButtons,
            Button captureBtn,
            Button scanBtn,
            Button connectDeviceBtn,
            Button pipBtn,
            StackPanel normalRightButtons)
        {
            if (_isWhiteboardMode) return;

            _isWhiteboardMode = true;

            _cameraService?.CancelConnecting();

            _videoImage.IsVisible = false;

            if (_whiteboardBackground != null)
            {
                _whiteboardBackground.IsVisible = true;
            }

            if (_imageOverlayCanvas != null)
            {
                _imageOverlayCanvas.IsVisible = true;
            }

            if (normalBottomButtons != null)
            {
                normalBottomButtons.IsVisible = false;
            }

            if (_whiteboardModeButtons != null)
            {
                _whiteboardModeButtons.IsVisible = true;
            }

            if (captureBtn != null) captureBtn.IsVisible = false;
            if (scanBtn != null) scanBtn.IsVisible = false;
            if (connectDeviceBtn != null) connectDeviceBtn.IsVisible = false;
            if (pipBtn != null) pipBtn.IsVisible = false;
            if (normalRightButtons != null) normalRightButtons.IsVisible = false;

            if (_whiteboardPageButtons != null)
            {
                _whiteboardPageButtons.IsVisible = true;
            }

            _videoStrokesBackup = _inkCanvas.GetStrokes();

            if (!_hasWhiteboardData)
            {
                _whiteboardPages = new List<List<InkStroke>> { new List<InkStroke>() };
                _pageBackgrounds = new List<string?> { null };
                _whiteboardCurrentPage = 1;
                _whiteboardTotalPages = 1;
                _inkCanvas.ClearStrokes();
                _inkCanvas.SetWhiteboardBackground(null);
            }
            else
            {
                if (_whiteboardCurrentPage - 1 < _whiteboardPages.Count)
                {
                    _inkCanvas.SetStrokes(_whiteboardPages[_whiteboardCurrentPage - 1]);
                }
                if (_whiteboardCurrentPage - 1 < _pageBackgrounds.Count)
                {
                    _inkCanvas.SetWhiteboardBackground(_pageBackgrounds[_whiteboardCurrentPage - 1]);
                }
            }

            UpdatePageInfo();
            RefreshImageOverlays();

            Grid.SetRowSpan(_inkCanvas, 2);
            _inkCanvas.SetWhiteboardMode();
            _inkCanvas.IsHitTestVisible = true;

            WhiteboardModeEntered?.Invoke();
        }

        public void ExitWhiteboardMode(
            StackPanel normalBottomButtons,
            Button captureBtn,
            Button scanBtn,
            Button connectDeviceBtn,
            Button pipBtn,
            StackPanel normalRightButtons,
            ToggleButton moveBtn,
            ToggleButton penBtn,
            ToggleButton eraserBtn,
            Border toolSliderBackground,
            StackPanel loadingContainer,
            StackPanel loadingPanel,
            Action startLoadingAnimation)
        {
            if (!_isWhiteboardMode) return;

            if (_whiteboardCurrentPage - 1 < _whiteboardPages.Count)
            {
                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
            }
            _hasWhiteboardData = _whiteboardTotalPages > 1 || _pageBackgrounds.Any(p => p != null) || _whiteboardPages.Any(p => p.Count > 0);

            _isWhiteboardMode = false;

            _videoImage.IsVisible = true;

            if (_whiteboardBackground != null)
            {
                _whiteboardBackground.IsVisible = false;
            }

            if (_imageOverlayCanvas != null)
            {
                _imageOverlayCanvas.IsVisible = false;
            }

            if (normalBottomButtons != null)
            {
                normalBottomButtons.IsVisible = true;
            }

            if (_whiteboardModeButtons != null)
            {
                _whiteboardModeButtons.IsVisible = false;
            }

            if (captureBtn != null) captureBtn.IsVisible = true;
            if (scanBtn != null) scanBtn.IsVisible = true;
            if (connectDeviceBtn != null) connectDeviceBtn.IsVisible = true;
            if (pipBtn != null) pipBtn.IsVisible = true;
            if (normalRightButtons != null) normalRightButtons.IsVisible = true;

            if (_whiteboardPageButtons != null)
            {
                _whiteboardPageButtons.IsVisible = false;
            }

            _inkCanvas.ExitWhiteboardMode();
            _inkCanvas.ClearStrokes();
            _inkCanvas.SetWhiteboardBackground(null);
            _inkCanvas.IsHitTestVisible = false;
            Grid.SetRowSpan(_inkCanvas, 1);
            _inkCanvas.SetStrokes(_videoStrokesBackup);

            _videoStrokesBackup = new List<InkStroke>();

            if (moveBtn != null) moveBtn.IsChecked = true;
            if (penBtn != null) penBtn.IsChecked = false;
            if (eraserBtn != null) eraserBtn.IsChecked = false;

            if (toolSliderBackground != null)
            {
                toolSliderBackground.Margin = new Thickness(0, 0, 0, 0);
            }

            if (loadingContainer != null)
            {
                loadingContainer.IsVisible = !_cameraService?.IsConnected ?? true;
            }
            if (loadingPanel != null)
            {
                loadingPanel.IsVisible = !_cameraService?.IsConnected ?? true;
            }
            if (_cameraService?.IsConnected != true)
            {
                startLoadingAnimation?.Invoke();
            }

            _cameraService?.DetectAndConnectCamera();

            WhiteboardModeExited?.Invoke();
        }

        public bool HandlePointerWheel()
        {
            return _isWhiteboardMode;
        }

        public bool HandleCanvasPointerPressed()
        {
            return _isWhiteboardMode;
        }

        public void PrevPage_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPptXmlMode)
            {
                // PPT XML模式下切换幻灯片
                PptXmlPreviousSlide();
                var slideControl = RenderPptXmlCurrentSlide();
                if (slideControl != null)
                {
                    SetWhiteboardBackgroundFromPptControl(slideControl);
                }
                // 更新页码信息
                UpdatePageInfo();
            }
            else if (_whiteboardCurrentPage > 1)
            {
                // 普通白板模式下切换页面
                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
                _whiteboardCurrentPage--;
                _inkCanvas.SetStrokes(_whiteboardPages[_whiteboardCurrentPage - 1]);
                if (_whiteboardCurrentPage - 1 < _pageBackgrounds.Count)
                {
                    _inkCanvas.SetWhiteboardBackground(_pageBackgrounds[_whiteboardCurrentPage - 1]);
                }
                UpdatePageInfo();
                RefreshImageOverlays();
            }
        }

        public void NextPage_Click(object? sender, RoutedEventArgs e)
        {
            if (_isPptXmlMode)
            {
                // PPT XML模式下切换幻灯片
                PptXmlNextSlide();
                var slideControl = RenderPptXmlCurrentSlide();
                if (slideControl != null)
                {
                    SetWhiteboardBackgroundFromPptControl(slideControl);
                }
                // 更新页码信息
                UpdatePageInfo();
            }
            else if (_whiteboardCurrentPage == _whiteboardTotalPages)
            {
                // 普通白板模式下添加新页
                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
                _whiteboardPages.Add(new List<InkStroke>());
                _pageBackgrounds.Add(null);
                _whiteboardTotalPages++;
                _whiteboardCurrentPage++;
                _inkCanvas.SetStrokes(new List<InkStroke>());
                _inkCanvas.SetWhiteboardBackground(null);
                UpdatePageInfo();
                UpdatePageThumbnails();
                RefreshImageOverlays();
            }
            else
            {
                // 普通白板模式下切换页面
                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
                _whiteboardCurrentPage++;
                _inkCanvas.SetStrokes(_whiteboardPages[_whiteboardCurrentPage - 1]);
                if (_whiteboardCurrentPage - 1 < _pageBackgrounds.Count)
                {
                    _inkCanvas.SetWhiteboardBackground(_pageBackgrounds[_whiteboardCurrentPage - 1]);
                }
                UpdatePageInfo();
                RefreshImageOverlays();
            }
        }

        private void UpdatePageInfo()
        {
            if (_pageInfoText != null)
            {
                if (_isPptXmlMode)
                {
                    // PPT XML模式下显示PPT页码
                    _pageInfoText.Text = $"{PptXmlCurrentIndex}/{PptXmlSlideCount}";
                }
                else
                {
                    // 普通白板模式下显示白板页码
                    _pageInfoText.Text = $"{_whiteboardCurrentPage}/{_whiteboardTotalPages}";
                }
            }

            if (_nextPagePath != null && _nextPageText != null)
            {
                if (_isPptXmlMode)
                {
                    // PPT XML模式下始终显示"下一页"
                    _nextPagePath.IconName = "chevron-right";
                    _nextPageText.Text = "下一页";
                }
                else if (_whiteboardCurrentPage == _whiteboardTotalPages)
                {
                    _nextPagePath.IconName = "plus";
                    _nextPageText.Text = "加页";
                }
                else
                {
                    _nextPagePath.IconName = "chevron-right";
                    _nextPageText.Text = "下一页";
                }
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPages)));
        }

        public void PageInfoBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            TogglePagePanel();
        }

        private void TogglePagePanel()
        {
            if (_pagePanel == null) return;

            if (_pagePanelOpen)
            {
                ClosePagePanel();
            }
            else
            {
                OpenPagePanel();
            }
        }

        private async void OpenPagePanel()
        {
            if (_pagePanel == null || _pagePanelOpen) return;

            _pagePanelOpen = true;
            _pagePanel.IsVisible = true;
            UpdatePageThumbnails();

            await UIAnimations.SlideInFromRight(_pagePanel, 280);
        }

        private async void ClosePagePanel()
        {
            if (_pagePanel == null || !_pagePanelOpen) return;

            _pagePanelOpen = false;
            await UIAnimations.SlideOutToRight(_pagePanel, 280);
            _pagePanel.IsVisible = false;
        }

        private void UpdatePageThumbnails()
        {
            _whiteboardPageThumbnails.Clear();
            for (int i = 0; i < _whiteboardTotalPages; i++)
            {
                _whiteboardPageThumbnails.Add(new WhiteboardPageThumbnail
                {
                    PageNumber = i + 1,
                    Thumbnail = GeneratePageThumbnail(i),
                    IsSelected = (i + 1) == _whiteboardCurrentPage
                });
            }
        }

        private Bitmap? GeneratePageThumbnail(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pageBackgrounds.Count)
                return null;

            var backgroundPath = _pageBackgrounds[pageIndex];
            if (string.IsNullOrEmpty(backgroundPath) || !File.Exists(backgroundPath))
                return null;

            try
            {
                using var stream = File.OpenRead(backgroundPath);
                using var originalBitmap = SKBitmap.Decode(stream);
                if (originalBitmap == null) return null;

                int thumbWidth = 260;
                int thumbHeight = (int)(originalBitmap.Height * ((double)thumbWidth / originalBitmap.Width));

                using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(thumbWidth, thumbHeight), SKSamplingOptions.Default);
                if (resizedBitmap == null) return null;

                using var image = SKImage.FromBitmap(resizedBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 80);

                var ms = new MemoryStream(data.ToArray());
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public void PageItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is int pageNumber)
            {
                if (pageNumber < 1 || pageNumber > _whiteboardTotalPages)
                    return;

                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
                _whiteboardCurrentPage = pageNumber;
                _inkCanvas.SetStrokes(_whiteboardPages[_whiteboardCurrentPage - 1]);
                if (_whiteboardCurrentPage - 1 < _pageBackgrounds.Count)
                {
                    _inkCanvas.SetWhiteboardBackground(_pageBackgrounds[_whiteboardCurrentPage - 1]);
                }
                UpdatePageInfo();
                UpdatePageThumbnails();
                ClosePagePanel();
            }
        }

        public void AddPageBtn_Click(object? sender, RoutedEventArgs e)
        {
            _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
            _whiteboardPages.Add(new List<InkStroke>());
            _pageBackgrounds.Add(null);
            _whiteboardTotalPages++;
            _whiteboardCurrentPage = _whiteboardTotalPages;
            _inkCanvas.SetStrokes(new List<InkStroke>());
            _inkCanvas.SetWhiteboardBackground(null);
            UpdatePageInfo();
            UpdatePageThumbnails();
            RefreshImageOverlays();
        }

        public void ImportPptBtn_Click(object? sender, RoutedEventArgs e)
        {
            ImportPptRequested?.Invoke();
        }

        public async void ImportPptSlides(List<string> slideImagePaths)
        {
            if (slideImagePaths == null || slideImagePaths.Count == 0)
                return;

            if (_importingOverlay != null)
            {
                _importingOverlay.IsVisible = true;
            }

            try
            {
                await Task.Run(async () =>
                {
                    foreach (var imagePath in slideImagePaths)
                    {
                        if (!File.Exists(imagePath)) continue;

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_whiteboardCurrentPage - 1 < _whiteboardPages.Count)
                            {
                                _whiteboardPages[_whiteboardCurrentPage - 1] = _inkCanvas.GetStrokes();
                            }
                            _whiteboardPages.Add(new List<InkStroke>());
                            _pageBackgrounds.Add(imagePath);
                            _whiteboardTotalPages++;
                            _whiteboardCurrentPage = _whiteboardTotalPages;
                            _inkCanvas.SetStrokes(new List<InkStroke>());
                            _inkCanvas.SetWhiteboardBackground(imagePath);
                            UpdatePageInfo();
                            UpdatePageThumbnails();
                        });
                    }
                });
            }
            finally
            {
                if (_importingOverlay != null)
                {
                    _importingOverlay.IsVisible = false;
                }
            }
        }

        #region PptService Methods (Spire.Presentation)

        public static List<string> ConvertPptToImages(string pptPath, string? outputDirectory = null)
        {
            var imagePaths = new List<string>();

            if (!File.Exists(pptPath))
            {
                return imagePaths;
            }

            outputDirectory ??= IOPath.Combine(IOPath.GetTempPath(), "ShowWrite_PptImages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);

            try
            {
                using var presentation = new Spire.Presentation.Presentation();
                presentation.LoadFromFile(pptPath);

                for (int i = 0; i < presentation.Slides.Count; i++)
                {
                    var slide = presentation.Slides[i];
                    var fileName = $"Slide_{i + 1}.png";
                    var outputPath = IOPath.Combine(outputDirectory, fileName);

                    using var image = slide.SaveAsImage();
                    if (image != null)
                    {
                        using var ms = new MemoryStream();
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Position = 0;

                        using var skBitmap = SKBitmap.Decode(ms);
                        if (skBitmap != null)
                        {
                            using var fileStream = File.OpenWrite(outputPath);
                            skBitmap.Encode(fileStream, SKEncodedImageFormat.Png, 100);
                            imagePaths.Add(outputPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }

            return imagePaths;
        }

        public static int GetSlideCount(string pptPath)
        {
            if (!File.Exists(pptPath))
            {
                return 0;
            }

            try
            {
                using var presentation = new Spire.Presentation.Presentation();
                presentation.LoadFromFile(pptPath);
                return presentation.Slides.Count;
            }
            catch
            {
                return 0;
            }
        }

        private void ImageOverlay_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_draggingImage != null)
            {
                e.Pointer.Capture(null);
                _draggingImage = null;
            }
        }

        // ---------- 缩放手柄相关 ----------
        private void ClearScaleHandles()
        {
            foreach (var h in _scaleHandles)
            {
                if (_imageOverlayCanvas != null)
                    _imageOverlayCanvas.Children.Remove(h);
            }
            _scaleHandles.Clear();
        }

        private void AddScaleHandle(string corner)
        {
            var handle = new Border
            {
                Width = 10,
                Height = 10,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Tag = corner,
                IsHitTestVisible = true,
                Cursor = corner switch
                {
                    "TopLeft" => Cursor.Default,
                    "BottomRight" => Cursor.Default,
                    "TopRight" => Cursor.Default,
                    "BottomLeft" => Cursor.Default,
                    _ => Cursor.Default
                }
            };
            handle.PointerPressed += ImageOverlay_CornerPressed;
            handle.PointerMoved += ImageOverlay_CornerMoved;
            handle.PointerReleased += ImageOverlay_CornerReleased;
            _scaleHandles.Add(handle);
            _imageOverlayCanvas?.Children.Add(handle);
        }

        private void UpdateScaleHandles()
        {
            if (_selectedImage == null || _imageOverlayCanvas == null) return;
            var tt = _selectedImage.RenderTransform as TranslateTransform;
            double x = tt?.X ?? 0;
            double y = tt?.Y ?? 0;
            double w = _selectedImage.Width;
            double h = _selectedImage.Height;

            foreach (var handle in _scaleHandles)
            {
                switch (handle.Tag as string)
                {
                    case "TopLeft":
                        Canvas.SetLeft(handle, x - handle.Width / 2);
                        Canvas.SetTop(handle, y - handle.Height / 2);
                        break;
                    case "TopRight":
                        Canvas.SetLeft(handle, x + w - handle.Width / 2);
                        Canvas.SetTop(handle, y - handle.Height / 2);
                        break;
                    case "BottomLeft":
                        Canvas.SetLeft(handle, x - handle.Width / 2);
                        Canvas.SetTop(handle, y + h - handle.Height / 2);
                        break;
                    case "BottomRight":
                        Canvas.SetLeft(handle, x + w - handle.Width / 2);
                        Canvas.SetTop(handle, y + h - handle.Height / 2);
                        break;
                }
            }
        }

        private void ImageOverlay_CornerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border handle) return;
            _activeHandle = handle;
            _handleStartPoint = e.GetPosition(_imageOverlayCanvas);
            if (_selectedImage?.RenderTransform is TranslateTransform tt)
            {
                _initialX = tt.X;
                _initialY = tt.Y;
            }
            _initialWidth = _selectedImage?.Width ?? 0;
            _initialHeight = _selectedImage?.Height ?? 0;
            e.Pointer.Capture(handle);
        }

        private void ImageOverlay_CornerMoved(object? sender, PointerEventArgs e)
        {
            if (_activeHandle == null || _selectedImage == null) return;
            var cur = e.GetPosition(_imageOverlayCanvas);
            var dx = cur.X - _handleStartPoint.X;
            var dy = cur.Y - _handleStartPoint.Y;

            double newWidth = _initialWidth;
            double newHeight = _initialHeight;
            double newX = _initialX;
            double newY = _initialY;

            switch (_activeHandle.Tag as string)
            {
                case "BottomRight":
                    newWidth = Math.Max(30, _initialWidth + dx);
                    newHeight = Math.Max(30, _initialHeight + dy);
                    break;
                case "TopLeft":
                    newWidth = Math.Max(30, _initialWidth - dx);
                    newHeight = Math.Max(30, _initialHeight - dy);
                    newX = _initialX + dx;
                    newY = _initialY + dy;
                    break;
                case "TopRight":
                    newWidth = Math.Max(30, _initialWidth + dx);
                    newHeight = Math.Max(30, _initialHeight - dy);
                    newY = _initialY + dy;
                    break;
                case "BottomLeft":
                    newWidth = Math.Max(30, _initialWidth - dx);
                    newHeight = Math.Max(30, _initialHeight + dy);
                    newX = _initialX + dx;
                    break;
            }

            _selectedImage.Width = newWidth;
            _selectedImage.Height = newHeight;
            if (_selectedImage.RenderTransform is TranslateTransform tt2)
            {
                tt2.X = newX;
                tt2.Y = newY;
            }

            // 更新选中边框尺寸
            if (_selectionBorder != null)
            {
                _selectionBorder.Width = newWidth + 4;
                _selectionBorder.Height = newHeight + 4;
                _selectionBorder.RenderTransform = _selectedImage.RenderTransform;
            }

            UpdateScaleHandles();
        }

        private void ImageOverlay_CornerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_activeHandle != null)
            {
                e.Pointer.Capture(null);
                _activeHandle = null;
            }
        }

        #endregion

        #region AsposeSlidesService Methods

        public void OpenAsposePresentation(string filePath)
        {
            CloseAsposePresentation();
            _asposePresentation = new Aspose.Slides.Presentation(filePath);
            _asposeCurrentIndex = 0;
        }

        public void CloseAsposePresentation()
        {
            _asposePresentation?.Dispose();
            _asposePresentation = null;
            _asposeCurrentIndex = 0;
        }

        public void AsposeNextSlide()
        {
            if (_asposePresentation == null) return;
            if (_asposeCurrentIndex < _asposePresentation.Slides.Count - 1)
            {
                _asposeCurrentIndex++;
            }
        }

        public void AsposePreviousSlide()
        {
            if (_asposePresentation == null) return;
            if (_asposeCurrentIndex > 0)
            {
                _asposeCurrentIndex--;
            }
        }

        public Bitmap? RenderAsposeCurrentSlide(int targetWidth = 1280, int targetHeight = 720)
        {
            if (_asposePresentation == null) return null;
            var slide = _asposePresentation.Slides[_asposeCurrentIndex];

            var slideSize = _asposePresentation.SlideSize.Size;
            var scaleX = (float)targetWidth / slideSize.Width;
            var scaleY = (float)targetHeight / slideSize.Height;
            var scale = Math.Min(scaleX, scaleY);
            if (scale <= 0) scale = 1f;

            using Aspose.Slides.IImage image = slide.GetImage(scale, scale);
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }

        #endregion

        #region OpenXmlPptService Methods

        public static List<string> ReadAllTextFromPpt(string filePath)
        {
            var result = new List<string>();
            using var ppt = PresentationDocument.Open(filePath, false);
            var presentationPart = ppt.PresentationPart;
            if (presentationPart?.Presentation?.SlideIdList == null)
                return result;

            foreach (var slideId in presentationPart.Presentation.SlideIdList.Elements<P.SlideId>())
            {
                var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId);
                var texts = slidePart.Slide.Descendants<D.Text>();
                foreach (var t in texts)
                {
                    if (!string.IsNullOrWhiteSpace(t.Text))
                    {
                        result.Add(t.Text);
                    }
                }
            }
            return result;
        }

        #endregion

        #region PowerPointService Methods

        /// <summary>
        /// 打开PowerPoint文件并准备放映
        /// </summary>
        public bool OpenPowerPointPresentation(string filePath)
        {
            try
            {
                ClosePowerPointPresentation();

                _pptService = new PptService();

                return _pptService.OpenPresentation(filePath);
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        /// <summary>
        /// 开始PowerPoint放映
        /// </summary>
        public bool StartPowerPointSlideShow(IntPtr hostWindowHandle, int width, int height)
        {
            if (_pptService == null) return false;

            try
            {
                _isPowerPointMode = true;
                return _pptService.StartSlideShow(hostWindowHandle, width, height);
            }
            catch (Exception ex)
            {

                _isPowerPointMode = false;
                return false;
            }
        }

        /// <summary>
        /// PowerPoint下一页
        /// </summary>
        public void PowerPointNextSlide()
        {
            _pptService?.NextSlide();
        }

        /// <summary>
        /// PowerPoint上一页
        /// </summary>
        public void PowerPointPreviousSlide()
        {
            _pptService?.PreviousSlide();
        }

        /// <summary>
        /// 跳转到PowerPoint指定页
        /// </summary>
        public void PowerPointGoToSlide(int slideIndex)
        {
            _pptService?.GoToSlide(slideIndex);
        }

        /// <summary>
        /// 停止PowerPoint放映
        /// </summary>
        public void StopPowerPointSlideShow()
        {
            _pptService?.StopSlideShow();
            _isPowerPointMode = false;
        }

        /// <summary>
        /// 关闭PowerPoint演示文稿
        /// </summary>
        public void ClosePowerPointPresentation()
        {
            if (_pptService != null)
            {
                _pptService.Dispose();
                _pptService = null;
            }
            _isPowerPointMode = false;
        }

        private void OnPowerPointSlideChanged(int currentSlide, int totalSlides)
        {
            // 触发属性变更通知
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PowerPointCurrentSlide)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PowerPointSlideCount)));
        }

        private void OnPowerPointPresentationEnded()
        {
            _isPowerPointMode = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPowerPointMode)));
        }

        #endregion

        #region PptXmlService Methods

        public void OpenPptXmlPresentation(string filePath)
        {
            ClosePptXmlPresentation();
            
            _pptService = new PptService();
            _pptService.OpenPptXmlPresentation(filePath);
            _isPptXmlMode = true;
        }

        public void ClosePptXmlPresentation()
        {
            if (_pptService != null)
            {
                _pptService.Dispose();
                _pptService = null;
            }
            _isPptXmlMode = false;
        }

        public void PptXmlNextSlide()
        {
            _pptService?.PptXmlNextSlide();
        }

        public void PptXmlPreviousSlide()
        {
            _pptService?.PptXmlPreviousSlide();
        }

        public PptSlideControl? RenderPptXmlCurrentSlide()
        {
            return _pptService?.RenderPptXmlCurrentSlide();
        }

        public void SetWhiteboardBackgroundFromPptControl(PptSlideControl slideControl)
        {
            if (_inkCanvas == null || slideControl == null) return;
            
            // 设置幻灯片控件的拉伸模式
            slideControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            slideControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            slideControl.Stretch = Avalonia.Media.Stretch.Uniform;
            
            // 将幻灯片控件添加到VideoAreaContainer
            // 这需要在主窗口中执行，暂时设置一个标记
            _pendingPptSlideControl = slideControl;
            
            // 触发事件让主窗口处理
            PptSlideControlReady?.Invoke(slideControl);
            
            // 更新页码信息
            UpdatePageInfo();
        }

        // 设置 PPT 背景图片（如果 PPT 定义了背景图）
        public void SetWhiteboardBackgroundFromPptImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return;
            // 使用 InkCanvas 已有的背景加载方式
            _inkCanvas.SetWhiteboardBackground(imagePath);
        }

        #endregion

        #region 图片图层方法

        public void AddImageOverlay(string imagePath)
        {
            if (_imageOverlayCanvas == null || !File.Exists(imagePath)) return;

            var bitmap = new Bitmap(imagePath);

            // 限制图片最大尺寸，避免超出屏幕
            double maxWidth = 800;
            double maxHeight = 600;
            double scale = 1.0;
            if (bitmap.PixelSize.Width > maxWidth || bitmap.PixelSize.Height > maxHeight)
            {
                scale = Math.Min(maxWidth / bitmap.PixelSize.Width, maxHeight / bitmap.PixelSize.Height);
            }

            var img = new Image
            {
                Source = bitmap,
                Width = bitmap.PixelSize.Width * scale,
                Height = bitmap.PixelSize.Height * scale,
                RenderTransform = new TranslateTransform(0, 0),
                Tag = imagePath,
                IsHitTestVisible = true
            };

            img.PointerPressed += ImageOverlay_PointerPressed;
            img.PointerMoved += ImageOverlay_PointerMoved;
            img.PointerReleased += ImageOverlay_PointerReleased;

            // 保存到当前页面的图片列表
            int pageIdx = _whiteboardCurrentPage - 1;
            while (_pageImageOverlays.Count <= pageIdx)
                _pageImageOverlays.Add(new List<Image>());
            _pageImageOverlays[pageIdx].Add(img);

            _imageOverlayCanvas.Children.Add(img);
        }

        public void ClearImageOverlays()
        {
            if (_imageOverlayCanvas == null) return;
            foreach (var child in _imageOverlayCanvas.Children.ToList())
            {
                if (child is Image img)
                {
                    img.PointerPressed -= ImageOverlay_PointerPressed;
                    img.PointerMoved -= ImageOverlay_PointerMoved;
                    img.PointerReleased -= ImageOverlay_PointerReleased;
                }
            }
            _imageOverlayCanvas.Children.Clear();
            _selectedImage = null;
            _selectionBorder = null;
        }

        public void SetImageOverlayHitTest(bool hitTestVisible)
        {
            if (_imageOverlayCanvas == null) return;
            _imageOverlayCanvas.IsHitTestVisible = hitTestVisible;
        }

        private void SelectImage(Image img)
        {
            // 取消之前的选中并清除手柄
            DeselectImage();
            ClearScaleHandles();

            _selectedImage = img;

            // 添加选中边框
            _selectionBorder = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(2),
                Width = img.Width + 4,
                Height = img.Height + 4,
                RenderTransform = img.RenderTransform,
                IsHitTestVisible = false
            };

            if (_imageOverlayCanvas != null)
            {
                _imageOverlayCanvas.Children.Add(_selectionBorder);
            }

            // 添加四个缩放手柄
            AddScaleHandle("TopLeft");
            AddScaleHandle("TopRight");
            AddScaleHandle("BottomLeft");
            AddScaleHandle("BottomRight");
            UpdateScaleHandles();
        }

        private void DeselectImage()
        {
            if (_selectionBorder != null && _imageOverlayCanvas != null)
            {
                _imageOverlayCanvas.Children.Remove(_selectionBorder);
            }
            _selectionBorder = null;
            _selectedImage = null;
            ClearScaleHandles();
        }

        private void ImageOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Image img) return;

            // 选中该图片
            SelectImage(img);

            _draggingImage = img;
            _dragStartPoint = e.GetPosition(_imageOverlayCanvas);

            if (img.RenderTransform is TranslateTransform tt)
            {
                _dragStartTransform = new TranslateTransform(tt.X, tt.Y);
            }
            else
            {
                img.RenderTransform = new TranslateTransform(0, 0);
                _dragStartTransform = new TranslateTransform(0, 0);
            }

            e.Pointer.Capture(img);
            e.Handled = true;
        }

        private void ImageOverlay_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_draggingImage == null || _imageOverlayCanvas == null) return;

            var cur = e.GetPosition(_imageOverlayCanvas);
            var dx = cur.X - _dragStartPoint.X;
            var dy = cur.Y - _dragStartPoint.Y;

            if (_draggingImage.RenderTransform is TranslateTransform tt)
            {
                tt.X = _dragStartTransform.X + dx;
                tt.Y = _dragStartTransform.Y + dy;

                // 同步选中边框位置
                if (_selectionBorder?.RenderTransform is TranslateTransform st)
                {
                    st.X = tt.X;
                    st.Y = tt.Y;
                }
            }
        }


        private void RefreshImageOverlays()
        {
            if (_imageOverlayCanvas == null) return;
            _imageOverlayCanvas.Children.Clear();
            int pageIdx = _whiteboardCurrentPage - 1;
            if (pageIdx >= 0 && pageIdx < _pageImageOverlays.Count)
            {
                foreach (var img in _pageImageOverlays[pageIdx])
                {
                    _imageOverlayCanvas.Children.Add(img);
                }
            }
            DeselectImage();
        }

        public void Dispose()
        {
            CloseAsposePresentation();
            ClosePowerPointPresentation();
            ClosePptXmlPresentation();
            GC.SuppressFinalize(this);
        }
    }
}
    #endregion