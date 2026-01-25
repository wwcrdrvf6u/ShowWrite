using ShowWrite.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;

namespace ShowWrite.Services
{
    /// <summary>
    /// 照片悬浮窗管理器
    /// 负责管理照片悬浮窗的显示、定位、数据绑定等所有逻辑
    /// </summary>
    public class PhotoPopupManager : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly Popup _photoPopup;
        private readonly System.Windows.Controls.ListBox _photoList;
        private readonly Window _mainWindow;
        private readonly ObservableCollection<PhotoWithStrokes> _photos;
        private readonly DrawingManager _drawingManager;
        private readonly CameraManager _cameraManager;
        private readonly MemoryManager _memoryManager;
        private readonly FrameProcessor _frameProcessor;
        private readonly PanZoomManager _panZoomManager;

        // 常量定义
        private const double BottomToolbarHeight = 70; // 底部工具栏高度
        private const double PopupMargin = 10; // 悬浮窗边距
        private const double PopupWidth = 400; // 悬浮窗宽度
        private const double PopupHeight = 500; // 悬浮窗高度

        // 事件
        public event Action<PhotoWithStrokes> PhotoSelected;
        public event Action BackToLiveRequested;
        public event Action SaveImageRequested;

        // 状态
        private PhotoWithStrokes _currentPhoto;
        private StrokeCollection _liveStrokes;
        private bool _isLiveMode = true;
        private System.Windows.Controls.ListBox photoList;
        private LogManager logManager;

        /// <summary>
        /// 当前选中的照片
        /// </summary>
        public PhotoWithStrokes CurrentPhoto
        {
            get => _currentPhoto;
            set
            {
                if (_currentPhoto != value)
                {
                    _currentPhoto = value;
                    OnPropertyChanged(nameof(CurrentPhoto));
                }
            }
        }

        /// <summary>
        /// 是否处于实时模式
        /// </summary>
        public bool IsLiveMode
        {
            get => _isLiveMode;
            set => _isLiveMode = value;
        }

        public PhotoPopupManager(
            Popup photoPopup,
            ListBox photoList,
            Window mainWindow,
            ObservableCollection<PhotoWithStrokes> photos,
            DrawingManager drawingManager,
            CameraManager cameraManager,
            MemoryManager memoryManager,
            FrameProcessor frameProcessor,
            PanZoomManager panZoomManager)
        {
            _photoPopup = photoPopup ?? throw new ArgumentNullException(nameof(photoPopup));
            _photoList = photoList ?? throw new ArgumentNullException(nameof(photoList));
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _photos = photos ?? throw new ArgumentNullException(nameof(photos));
            _drawingManager = drawingManager ?? throw new ArgumentNullException(nameof(drawingManager));
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
            _frameProcessor = frameProcessor ?? throw new ArgumentNullException(nameof(frameProcessor));
            _panZoomManager = panZoomManager ?? throw new ArgumentNullException(nameof(panZoomManager));

            Initialize();
        }

        public PhotoPopupManager(Popup photoPopup, System.Windows.Controls.ListBox photoList, MainWindow mainWindow, ObservableCollection<PhotoWithStrokes> photos, DrawingManager drawingManager, CameraManager cameraManager, MemoryManager memoryManager, FrameProcessor frameProcessor, PanZoomManager panZoomManager, LogManager logManager)
        {
            _photoPopup = photoPopup;
            this.photoList = photoList;
            _mainWindow = mainWindow;
            _photos = photos;
            _drawingManager = drawingManager;
            _cameraManager = cameraManager;
            _memoryManager = memoryManager;
            _frameProcessor = frameProcessor;
            _panZoomManager = panZoomManager;
            this.logManager = logManager;
        }

        /// <summary>
        /// 初始化照片悬浮窗
        /// </summary>
        private void Initialize()
        {
            try
            {
                // 绑定数据源
                _photoList.ItemsSource = _photos;

                // 设置数据模板
                _photoList.ItemTemplate = CreatePhotoListItemTemplate();

                // 订阅事件
                _photoList.SelectionChanged += PhotoList_SelectionChanged;
                _photoPopup.Opened += PhotoPopup_Opened;
                _photoPopup.Closed += PhotoPopup_Closed;

                // 初始化实时模式笔迹
                _liveStrokes = new StrokeCollection(_drawingManager.GetStrokes());

                Logger.Info("PhotoPopupManager", "照片悬浮窗初始化完成");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"初始化失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 显示照片悬浮窗
        /// </summary>
        public void ShowPhotoPopup()
        {
            try
            {
                // 重新定位悬浮窗
                RepositionPhotoPopup();

                // 打开悬浮窗
                _photoPopup.IsOpen = true;

                // 更新列表显示
                UpdatePhotoListDisplay();

                // 监听窗口大小变化，以便在窗口大小改变时重新定位
                _mainWindow.SizeChanged += MainWindow_SizeChanged;

                Logger.Debug("PhotoPopupManager", "显示照片悬浮窗");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"显示悬浮窗失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 隐藏照片悬浮窗
        /// </summary>
        public void HidePhotoPopup()
        {
            try
            {
                _photoPopup.IsOpen = false;

                // 移除窗口大小变化监听
                _mainWindow.SizeChanged -= MainWindow_SizeChanged;

                Logger.Debug("PhotoPopupManager", "隐藏照片悬浮窗");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"隐藏悬浮窗失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 切换照片悬浮窗显示状态
        /// </summary>
        public void TogglePhotoPopup()
        {
            if (_photoPopup.IsOpen)
            {
                HidePhotoPopup();
            }
            else
            {
                ShowPhotoPopup();
            }
        }

        /// <summary>
        /// 重新定位照片悬浮窗（显示在右下角，避开底部工具栏）
        /// </summary>
        public void RepositionPhotoPopup()
        {
            if (_photoPopup == null) return;

            try
            {
                // 获取主窗口的尺寸和位置
                double mainWindowWidth = _mainWindow.ActualWidth;
                double mainWindowHeight = _mainWindow.ActualHeight;

                // 计算右下角位置，避开底部工具栏
                double left = mainWindowWidth - PopupWidth - PopupMargin;

                // 重要：减去底部工具栏高度，确保悬浮窗不会覆盖工具栏
                double top = mainWindowHeight - PopupHeight - BottomToolbarHeight - PopupMargin;

                // 确保位置在屏幕范围内
                left = Math.Max(PopupMargin, left);
                top = Math.Max(PopupMargin, top);

                // 设置悬浮窗位置
                _photoPopup.HorizontalOffset = left;
                _photoPopup.VerticalOffset = top;

                // 调试信息
                Logger.Debug("PhotoPopupManager",
                    $"照片悬浮窗定位到: ({left:F0}, {top:F0}), " +
                    $"主窗口尺寸: ({mainWindowWidth:F0}x{mainWindowHeight:F0}), " +
                    $"避开了底部工具栏高度: {BottomToolbarHeight}");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"重新定位照片悬浮窗失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 添加新照片
        /// </summary>
        /// <param name="image">照片图像</param>
        /// <param name="strokes">笔迹</param>
        public void AddPhoto(BitmapSource image, StrokeCollection strokes)
        {
            try
            {
                if (image == null) return;

                var capturedImage = new CapturedImage(image);
                var photo = new PhotoWithStrokes(capturedImage);
                photo.Strokes = strokes != null ? new StrokeCollection(strokes) : new StrokeCollection();

                // 添加到列表开头
                _photos.Insert(0, photo);
                CurrentPhoto = photo;

                // 更新列表显示
                UpdatePhotoListDisplay();

                // 清理内存
                _memoryManager.TriggerMemoryCleanup();

                Logger.Info("PhotoPopupManager", "添加新照片成功");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"添加照片失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 选择照片查看
        /// </summary>
        /// <param name="photoWithStrokes">要查看的照片</param>
        public void SelectPhotoForViewing(PhotoWithStrokes photoWithStrokes)
        {
            try
            {
                // 检查是否点击了已选中的照片
                if (_currentPhoto != null && _currentPhoto == photoWithStrokes && !_isLiveMode)
                {
                    // 再次点击已选中的照片，返回实时模式
                    BackToLive();
                    return;
                }

                Logger.Info("PhotoPopupManager", "选择照片查看模式");

                // 保存当前实时模式的笔迹
                _liveStrokes = new StrokeCollection(_drawingManager.GetStrokes());
                _isLiveMode = false;
                CurrentPhoto = photoWithStrokes;

                // 触发照片选择事件
                PhotoSelected?.Invoke(photoWithStrokes);

                // 切换绘制管理器的StrokeCollection到照片的笔迹
                _drawingManager.SwitchToPhotoStrokes(photoWithStrokes.Strokes);

                // 重置缩放状态
                _panZoomManager.ResetZoom();

                // 释放摄像头资源
                _cameraManager.ReleaseCameraResources();

                // 触发GC释放旧资源
                _memoryManager.TriggerMemoryCleanup();

                // 更新列表显示
                UpdatePhotoListDisplay();
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"选择照片查看失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 返回实时模式
        /// </summary>
        public void BackToLive()
        {
            try
            {
                Logger.Info("PhotoPopupManager", "返回实时模式");

                // 保存当前照片的笔迹
                if (_currentPhoto != null)
                {
                    _currentPhoto.Strokes = new StrokeCollection(_drawingManager.GetStrokes());
                }

                _isLiveMode = true;
                CurrentPhoto = null;

                // 清空照片列表选中项
                _photoList.SelectedItem = null;

                // 触发返回实时模式事件
                BackToLiveRequested?.Invoke();

                // 切换回实时模式的笔迹
                _drawingManager.SwitchToPhotoStrokes(_liveStrokes);

                // 重置缩放
                _panZoomManager.ResetZoom();

                // 重启摄像头
                _cameraManager.RestartCamera();

                // 内存清理
                _memoryManager.TriggerMemoryCleanup();

                // 更新照片列表显示
                UpdatePhotoListDisplay();

                Logger.Info("PhotoPopupManager", "已返回实时模式");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"返回实时模式失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 保存当前照片
        /// </summary>
        public void SaveCurrentPhoto()
        {
            try
            {
                if (_currentPhoto == null)
                {
                    Logger.Warning("PhotoPopupManager", "没有当前照片可保存");
                    return;
                }

                Logger.Info("PhotoPopupManager", "开始保存图片");

                // 触发保存图片事件
                SaveImageRequested?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"保存当前照片失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 更新照片列表显示
        /// </summary>
        private void UpdatePhotoListDisplay()
        {
            try
            {
                if (_photoList != null)
                {
                    _photoList.Items.Refresh();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"更新照片列表显示失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 创建照片列表项模板
        /// </summary>
        private DataTemplate CreatePhotoListItemTemplate()
        {
            var dataTemplate = new DataTemplate();

            // 创建框架
            var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
            stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stackPanelFactory.SetValue(StackPanel.MarginProperty, new Thickness(6));

            // 创建缩略图
            var imageFactory = new FrameworkElementFactory(typeof(Image));
            imageFactory.SetValue(Image.WidthProperty, 70.0);
            imageFactory.SetValue(Image.HeightProperty, 52.0);
            imageFactory.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Thumbnail"));
            imageFactory.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.UniformToFill);

            // 创建右侧信息面板
            var infoPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
            infoPanelFactory.SetValue(StackPanel.MarginProperty, new Thickness(10, 0, 0, 0));
            infoPanelFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

            // 时间戳
            var timestampFactory = new FrameworkElementFactory(typeof(TextBlock));
            timestampFactory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
            timestampFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            timestampFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Timestamp"));

            // 笔迹数
            var strokesFactory = new FrameworkElementFactory(typeof(TextBlock));
            strokesFactory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.LightGray);
            strokesFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            strokesFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Strokes.Count")
            {
                StringFormat = "笔迹数: {0}"
            });

            // 选中状态提示文字
            var selectedTipFactory = new FrameworkElementFactory(typeof(TextBlock));
            selectedTipFactory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Yellow);
            selectedTipFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            selectedTipFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            selectedTipFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));

            // 使用多重绑定和值转换器来确定是否显示提示文字
            var multiBinding = new System.Windows.Data.MultiBinding();
            multiBinding.Bindings.Add(new System.Windows.Data.Binding(".")); // 当前项
            multiBinding.Bindings.Add(new System.Windows.Data.Binding("CurrentPhoto") { Source = this });
            multiBinding.Converter = new PhotoSelectedTipConverter();

            selectedTipFactory.SetBinding(TextBlock.TextProperty, multiBinding);

            // 组合面板
            infoPanelFactory.AppendChild(timestampFactory);
            infoPanelFactory.AppendChild(strokesFactory);
            infoPanelFactory.AppendChild(selectedTipFactory);

            // 组合主面板
            stackPanelFactory.AppendChild(imageFactory);
            stackPanelFactory.AppendChild(infoPanelFactory);

            dataTemplate.VisualTree = stackPanelFactory;
            return dataTemplate;
        }

        #region 事件处理方法

        /// <summary>
        /// 主窗口大小变化事件处理
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_photoPopup != null && _photoPopup.IsOpen)
            {
                // 当主窗口大小改变时，重新定位悬浮窗
                RepositionPhotoPopup();
            }
        }

        /// <summary>
        /// 照片悬浮窗打开事件
        /// </summary>
        private void PhotoPopup_Opened(object sender, EventArgs e)
        {
            try
            {
                // 重新定位悬浮窗（确保在打开时位置正确）
                RepositionPhotoPopup();

                // 更新列表显示
                UpdatePhotoListDisplay();

                Logger.Debug("PhotoPopupManager", "照片悬浮窗已打开");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"照片悬浮窗打开事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 照片悬浮窗关闭事件
        /// </summary>
        private void PhotoPopup_Closed(object sender, EventArgs e)
        {
            try
            {
                // 清空选中项
                _photoList.SelectedItem = null;

                Logger.Debug("PhotoPopupManager", "照片悬浮窗已关闭");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"照片悬浮窗关闭事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 照片列表选择变更事件
        /// </summary>
        private void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // 如果没有选中任何项，直接返回
                if (_photoList.SelectedItem == null)
                    return;

                if (_photoList.SelectedItem is PhotoWithStrokes photoWithStrokes)
                {
                    // 选择照片查看
                    SelectPhotoForViewing(photoWithStrokes);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"照片列表选择变更事件失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region INotifyPropertyChanged 实现

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            try
            {
                // 取消事件订阅（使用空值条件运算符确保安全）
                _photoList?.SelectionChanged -= PhotoList_SelectionChanged;
                _photoPopup?.Opened -= PhotoPopup_Opened;
                _photoPopup?.Closed -= PhotoPopup_Closed;
                _mainWindow?.SizeChanged -= MainWindow_SizeChanged;

                // 关闭悬浮窗
                _photoPopup?.Dispatcher.Invoke(() =>
                {
                    _photoPopup.IsOpen = false;
                });

                // 清空数据
                _photos?.Clear();

                // 释放引用
                _liveStrokes = null;
                CurrentPhoto = null;

                Logger.Info("PhotoPopupManager", "照片悬浮窗管理器已清理");
            }
            catch (Exception ex)
            {
                Logger.Error("PhotoPopupManager", $"清理资源失败: {ex.Message}", ex);
            }
        }

        #endregion
    }
}