using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using Spire.Presentation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using System.Threading.Tasks;
using Avalonia.Styling;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Control = Avalonia.Controls.Control;

namespace ShowWrite
{
    public class PptService : IDisposable
    {
        #region PowerPointService
        private Spire.Presentation.Presentation? _presentation;
        private int _currentSlideIndex = 0;
        private bool _isDisposed;
        private bool _isPlaying;
        private List<SKBitmap>? _slideCache;
        private float _slideWidth;
        private float _slideHeight;

        public bool IsOpen => _presentation != null;
        public int SlideCount => _presentation?.Slides.Count ?? 0;
        public int CurrentSlideIndex => _currentSlideIndex + 1; // 返回1-based索引

        // PptXmlService properties
        public int PptXmlSlideCount => _pptXmlSlides?.Count ?? 0;
        public int PptXmlCurrentIndex => _pptXmlCurrentIndex + 1; // 返回1-based索引

        public event Action<int, int>? SlideChanged; // 当前页, 总页数
        public event Action? PresentationEnded;
        #endregion

        #region PptXmlService
        public class PptSlide
        {
            public int Index { get; set; }
            public string SlideId { get; set; }
            public List<PptShape> Shapes { get; set; } = new List<PptShape>();
            public string BackgroundColor { get; set; }
            public string BackgroundImage { get; set; }
        }

        public class PptShape
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string Text { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string FillColor { get; set; }
            public string BorderColor { get; set; }
            public double BorderWidth { get; set; }
            public string ImagePath { get; set; }
            // 新增字体属性
            public string FontFamily { get; set; }
            public double FontSize { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
        }

        // PptXmlService fields
        private List<PptSlide>? _pptXmlSlides;
        private int _pptXmlCurrentIndex;
        private PptService? _pptAnimationService;
        private PptSlideControl? _pptSlideControl;
        #endregion

        #region PptAnimationService
        public enum AnimationType
        {
            FadeIn,
            SlideInFromLeft,
            SlideInFromRight,
            SlideInFromTop,
            SlideInFromBottom,
            ZoomIn,
            ZoomOut
        }

        public class AnimationSettings
        {
            public AnimationType Type { get; set; }
            public TimeSpan Duration { get; set; }
            public Easing Easing { get; set; }
        }
        #endregion

        public PptService()
        {
            // 初始化
        }

        #region PowerPointService Methods
        /// <summary>
        /// 打开PPT文件
        /// </summary>
        public bool OpenPresentation(string filePath)
        {
            try
            {
                ClosePresentation();

                // 使用Spire.Presentation加载PPT
                _presentation = new Spire.Presentation.Presentation();
                _presentation.LoadFromFile(filePath);

                // 获取幻灯片尺寸
                _slideWidth = _presentation.SlideSize.Size.Width;
                _slideHeight = _presentation.SlideSize.Size.Height;

                // 预渲染所有幻灯片到缓存
                PreRenderSlides();

                _currentSlideIndex = 0;
                return true;
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        /// <summary>
        /// 预渲染所有幻灯片到缓存
        /// </summary>
        private void PreRenderSlides()
        {
            if (_presentation == null) return;

            _slideCache = new List<SKBitmap>();

            try
            {
                for (int i = 0; i < _presentation.Slides.Count; i++)
                {
                    var slide = _presentation.Slides[i];
                    var bitmap = RenderSlideToBitmap(slide, i);
                    _slideCache.Add(bitmap);
                }
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// 将幻灯片渲染为SKBitmap
        /// </summary>
        private SKBitmap RenderSlideToBitmap(ISlide slide, int slideIndex)
        {
            // 创建一个高分辨率的位图
            int width = 1920;
            int height = (int)(width * _slideHeight / _slideWidth);

            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

            try
            {
                // 使用Spire.Presentation渲染幻灯片
                // 将幻灯片保存为图片
                using (var image = slide.SaveAsImage())
                {
                    // 调整大小
                    using (var resized = new System.Drawing.Bitmap(width, height))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(image, 0, 0, width, height);
                        }

                        // 将System.Drawing.Bitmap转换为SKBitmap
                        using (var stream = new MemoryStream())
                        {
                            resized.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            stream.Position = 0;

                            // 使用SKBitmap.Decode从流中加载
                            var decoded = SKBitmap.Decode(stream);
                            if (decoded != null)
                            {
                                // 复制到目标bitmap
                                using (var canvas = new SKCanvas(bitmap))
                                {
                                    canvas.DrawBitmap(decoded, 0, 0);
                                }
                                decoded.Dispose();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                // 如果渲染失败,创建一个空白位图
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.White);
                    using (var paint = new SKPaint())
                    using (var font = new SKFont { Size = 40 })
                    {
                        paint.Color = SKColors.Black;
                        paint.IsAntialias = true;
                        canvas.DrawText("无法渲染此幻灯片", width / 2 - 150, height / 2, font, paint);
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 获取当前幻灯片的渲染图像
        /// </summary>
        public SKBitmap? GetCurrentSlideImage()
        {
            if (_slideCache == null || _currentSlideIndex < 0 || _currentSlideIndex >= _slideCache.Count)
            {
                return null;
            }

            return _slideCache[_currentSlideIndex];
        }

        /// <summary>
        /// 获取指定幻灯片的渲染图像
        /// </summary>
        public SKBitmap? GetSlideImage(int slideIndex)
        {
            if (_slideCache == null || slideIndex < 0 || slideIndex >= _slideCache.Count)
            {
                return null;
            }

            return _slideCache[slideIndex];
        }

        /// <summary>
        /// 开始放映PPT
        /// </summary>
        public bool StartSlideShow(IntPtr hostWindowHandle, int width, int height)
        {
            if (_presentation == null)
            {
                return false;
            }

            try
            {
                _isPlaying = true;
                _currentSlideIndex = 0;
                SlideChanged?.Invoke(CurrentSlideIndex, SlideCount);
                return true;
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        /// <summary>
        /// 下一页
        /// </summary>
        public void NextSlide()
        {
            if (_presentation == null || !_isPlaying) return;

            if (_currentSlideIndex < SlideCount - 1)
            {
                _currentSlideIndex++;
                SlideChanged?.Invoke(CurrentSlideIndex, SlideCount);
            }
            else
            {
                // 已经是最后一页,触发结束事件
                PresentationEnded?.Invoke();
            }
        }

        /// <summary>
        /// 上一页
        /// </summary>
        public void PreviousSlide()
        {
            if (_presentation == null || !_isPlaying) return;

            if (_currentSlideIndex > 0)
            {
                _currentSlideIndex--;
                SlideChanged?.Invoke(CurrentSlideIndex, SlideCount);
            }
        }

        /// <summary>
        /// 跳转到指定页
        /// </summary>
        public void GoToSlide(int slideIndex)
        {
            if (_presentation == null || !_isPlaying) return;

            // slideIndex是1-based
            int zeroBasedIndex = slideIndex - 1;
            if (zeroBasedIndex >= 0 && zeroBasedIndex < SlideCount)
            {
                _currentSlideIndex = zeroBasedIndex;
                SlideChanged?.Invoke(CurrentSlideIndex, SlideCount);
            }
        }

        /// <summary>
        /// 暂停放映
        /// </summary>
        public void PauseSlideShow()
        {
            _isPlaying = false;
        }

        /// <summary>
        /// 继续放映
        /// </summary>
        public void ResumeSlideShow()
        {
            if (_presentation != null)
            {
                _isPlaying = true;
            }
        }

        /// <summary>
        /// 停止放映
        /// </summary>
        public void StopSlideShow()
        {
            _isPlaying = false;
            _currentSlideIndex = 0;
        }

        /// <summary>
        /// 关闭PPT
        /// </summary>
        public void ClosePresentation()
        {
            try
            {
                StopSlideShow();

                // 清理幻灯片缓存
                if (_slideCache != null)
                {
                    foreach (var bitmap in _slideCache)
                    {
                        bitmap?.Dispose();
                    }
                    _slideCache.Clear();
                    _slideCache = null;
                }

                // 释放Presentation对象
                _presentation?.Dispose();
                _presentation = null;
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// 获取幻灯片尺寸
        /// </summary>
        public (float Width, float Height) GetSlideSize()
        {
            return (_slideWidth, _slideHeight);
        }

        /// <summary>
        /// 获取幻灯片宽高比
        /// </summary>
        public float GetSlideAspectRatio()
        {
            if (_slideHeight == 0) return 16f / 9f;
            return _slideWidth / _slideHeight;
        }
        #endregion

        #region PptXmlService Methods
        // 获取当前 PPT XML 幻灯片（用于读取背景信息等）
        public PptSlide? GetCurrentPptSlide()
        {
            if (_pptXmlSlides == null) return null;
            if (_pptXmlCurrentIndex < 0 || _pptXmlCurrentIndex >= _pptXmlSlides.Count) return null;
            return _pptXmlSlides[_pptXmlCurrentIndex];
        }

        public List<PptSlide> ReadPptSlides(string filePath)
        {
            var slides = new List<PptSlide>();

            try
            {
                using (var presentationDocument = PresentationDocument.Open(filePath, false))
                {
                    var presentationPart = presentationDocument.PresentationPart;
                    if (presentationPart != null)
                    {
                        var presentation = presentationPart.Presentation;
                        if (presentation != null)
                        {
                            var slideIdList = presentation.SlideIdList;
                            if (slideIdList != null)
                            {
                                var slideIds = slideIdList.ChildElements.OfType<SlideId>().ToList();
                                
                                for (int i = 0; i < slideIds.Count; i++)
                                {
                                    var slideId = slideIds[i].RelationshipId;
                                    var slide = new PptSlide { Index = i + 1, SlideId = slideId };
                                    
                                    // 读取对应的幻灯片
                                    var slidePart = presentationPart.GetPartById(slideId) as SlidePart;
                                    if (slidePart != null)
                                    {
                                        ParseSlideShapes(slidePart, slide);
                                    }
                                    
                                    slides.Add(slide);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }

            return slides;
        }

        private void ParseSlideShapes(SlidePart slidePart, PptSlide slide)
        {
            if (slidePart.Slide == null) return;
            var slideElement = slidePart.Slide;

            // ---------- 背景 ----------
            var background = slideElement.CommonSlideData?.Background?.BackgroundProperties;
            if (background != null)
            {
                // 颜色填充
                var solidFill = background.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.SolidFill>().FirstOrDefault();
                if (solidFill != null)
                {
                    var rgb = solidFill.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>().FirstOrDefault();
                    if (rgb?.Val != null)
                        slide.BackgroundColor = "#" + rgb.Val.Value;
                }
                // 背景图片（BlipFill）
                var blipFill = background.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.BlipFill>().FirstOrDefault();
                if (blipFill != null)
                {
                    var embed = blipFill.Blip?.Embed?.Value;
                    if (!string.IsNullOrEmpty(embed))
                    {
                        var imgPart = (ImagePart)slidePart.GetPartById(embed);
                        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ppt_bg_{Guid.NewGuid()}.png");
                        using (var stream = imgPart.GetStream())
                        using (var file = System.IO.File.OpenWrite(tempPath))
                        {
                            stream.CopyTo(file);
                        }
                        slide.BackgroundImage = tempPath;
                    }
                }
            }

            var spTree = slideElement.CommonSlideData?.ShapeTree;
            if (spTree == null) return;

            // 读取所有 Shape、GroupShape、GraphicFrame 等（这里仅处理 Shape）
            var shapeElements = spTree.ChildElements.OfType<DocumentFormat.OpenXml.Presentation.Shape>();
            foreach (var shape in shapeElements)
            {
                var pptShape = new PptShape();
                // ID
                pptShape.Id = shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id.ToString();

                // ---------- 位置与尺寸 ----------
                // 首先尝试 ShapeProperties.Transform2D
                var transform = shape.ShapeProperties?.Transform2D;
                if (transform != null && transform.Offset != null && transform.Extents != null)
                {
                    SetTransform(transform, pptShape);
                }
                else
                {
                    // 某些 shape 可能在 GroupShape 中，需要递归获取（简化：尝试获取其父 GroupShape's Transform)
                    var group = shape.Parent?.Parent as DocumentFormat.OpenXml.Presentation.GroupShape;
                    if (group?.GroupShapeProperties?.TransformGroup != null)
                    {
                        var tg = group.GroupShapeProperties.TransformGroup;
                        if (tg?.Offset != null && tg?.Extents != null)
                        {
                            SetTransform(tg, pptShape);
                        }
                    }
                }

                // ---------- 填充颜色 ----------
                var fill = shape.ShapeProperties?.ChildElements
                    .OfType<DocumentFormat.OpenXml.Drawing.SolidFill>().FirstOrDefault();
                if (fill != null)
                {
                    var rgb = fill.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.RgbColorModelHex>().FirstOrDefault();
                    if (rgb?.Val != null) pptShape.FillColor = "#" + rgb.Val.Value;
                }
                else
                {
                    // 可能是 GradientFill / PatternFill 等，这里仅记录为 null
                    pptShape.FillColor = null;
                }

                // ---------- 文本与字体 ----------
                if (shape.TextBody != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var para in shape.TextBody.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.Paragraph>())
                    {
                        foreach (var run in para.ChildElements.OfType<DocumentFormat.OpenXml.Drawing.Run>())
                        {
                            // 文本内容
                            if (run.Text?.Text != null)
                                sb.Append(run.Text.Text);

                            // 字体信息（可能在 RunProperties）
                            var rp = run.RunProperties;
                            if (rp != null)
                            {
                                // 字体名称
                                var latinFont = rp.GetFirstChild<DocumentFormat.OpenXml.Drawing.LatinFont>();
                                var latin = latinFont?.Typeface;
                                if (!string.IsNullOrEmpty(latin)) pptShape.FontFamily = latin;
                                // 大小（单位为 1/100 磅）
                                if (rp.FontSize != null)
                                {
                                    // FontSize 为 Int32Value，直接取 Value
                                    var size = rp.FontSize.Value;
                                    pptShape.FontSize = size / 100.0;
                                }
                                // 粗体 / 斜体（BooleanValue）
                                if (rp.Bold != null && rp.Bold.HasValue)
                                    pptShape.Bold = rp.Bold.Value;
                                if (rp.Italic != null && rp.Italic.HasValue)
                                    pptShape.Italic = rp.Italic.Value;
                            }
                        }
                        sb.AppendLine();
                    }
                    pptShape.Text = sb.ToString().Trim();
                }

                // ---------- 调试日志 ----------

                slide.Shapes.Add(pptShape);
            }
        }

        // Helper：将 Transform2D 转为像素坐标
        private void SetTransform(DocumentFormat.OpenXml.Drawing.Transform2D transform, PptShape target)
        {
            const double emuToPixel = 96.0 / 914400.0;
            if (transform.Offset != null && transform.Extents != null)
            {
                target.X = transform.Offset.X * emuToPixel;
                target.Y = transform.Offset.Y * emuToPixel;
                target.Width = transform.Extents.Cx * emuToPixel;
                target.Height = transform.Extents.Cy * emuToPixel;
            }
        }

        // Helper：将 TransformGroup 转为像素坐标（用于 GroupShape）
        private void SetTransform(DocumentFormat.OpenXml.Drawing.TransformGroup transform, PptShape target)
        {
            const double emuToPixel = 96.0 / 914400.0;
            if (transform.Offset != null && transform.Extents != null)
            {
                target.X = transform.Offset.X * emuToPixel;
                target.Y = transform.Offset.Y * emuToPixel;
                target.Width = transform.Extents.Cx * emuToPixel;
                target.Height = transform.Extents.Cy * emuToPixel;
            }
        }

        public void OpenPptXmlPresentation(string filePath)
        {
            ClosePptXmlPresentation();
            
            _pptXmlSlides = ReadPptSlides(filePath);
            _pptXmlCurrentIndex = 0;
            _pptAnimationService = this;
        }

        public void ClosePptXmlPresentation()
        {
            _pptXmlSlides = null;
            _pptXmlCurrentIndex = 0;
            _pptAnimationService = null;
            _pptSlideControl = null;
        }

        public void PptXmlNextSlide()
        {
            if (_pptXmlSlides == null) return;
            if (_pptXmlCurrentIndex < _pptXmlSlides.Count - 1)
            {
                _pptXmlCurrentIndex++;
            }
        }

        public void PptXmlPreviousSlide()
        {
            if (_pptXmlSlides == null) return;
            if (_pptXmlCurrentIndex > 0)
            {
                _pptXmlCurrentIndex--;
            }
        }

        public PptSlideControl? RenderPptXmlCurrentSlide()
        {
            if (_pptXmlSlides == null || _pptXmlSlides.Count == 0)
                return null;

            if (_pptXmlCurrentIndex < 0 || _pptXmlCurrentIndex >= _pptXmlSlides.Count)
                return null;

            var slide = _pptXmlSlides[_pptXmlCurrentIndex];
            
            if (_pptSlideControl == null)
            {
                _pptSlideControl = new PptSlideControl();
            }
            
            _pptSlideControl.LoadSlide(slide);
            
            // 播放动画
            _pptAnimationService?.AnimateSlide(_pptSlideControl);
            
            return _pptSlideControl;
        }
        #endregion

        #region PptAnimationService Methods
        public async Task AnimateElement(Control element, AnimationType animationType, TimeSpan duration = default)
        {
            if (element == null)
                return;

            if (duration == default)
                duration = TimeSpan.FromSeconds(1);

            var settings = new AnimationSettings
            {
                Type = animationType,
                Duration = duration,
                Easing = new CubicEaseInOut()
            };

            await AnimateElement(element, settings);
        }

        public async Task AnimateElement(Control element, AnimationSettings settings)
        {
            if (element == null)
                return;

            // 保存原始状态
            var originalOpacity = element.Opacity;
            var originalTransform = element.RenderTransform;

            // 初始化动画状态
            switch (settings.Type)
            {
                case AnimationType.FadeIn:
                    element.Opacity = 0;
                    break;
                case AnimationType.SlideInFromLeft:
                    element.RenderTransform = new TranslateTransform(-1000, 0);
                    element.Opacity = 0;
                    break;
                case AnimationType.SlideInFromRight:
                    element.RenderTransform = new TranslateTransform(1000, 0);
                    element.Opacity = 0;
                    break;
                case AnimationType.SlideInFromTop:
                    element.RenderTransform = new TranslateTransform(0, -1000);
                    element.Opacity = 0;
                    break;
                case AnimationType.SlideInFromBottom:
                    element.RenderTransform = new TranslateTransform(0, 1000);
                    element.Opacity = 0;
                    break;
                case AnimationType.ZoomIn:
                    element.RenderTransform = new ScaleTransform(0, 0);
                    element.Opacity = 0;
                    break;
                case AnimationType.ZoomOut:
                    element.RenderTransform = new ScaleTransform(2, 2);
                    element.Opacity = 0;
                    break;
            }

            // 创建动画
            var animation = CreateAnimation(settings);

            // 应用动画
            await animation.RunAsync(element);

            // 恢复原始状态
            element.RenderTransform = originalTransform;
        }

        private Animation CreateAnimation(AnimationSettings settings)
        {
            var animation = new Animation
            {
                Duration = settings.Duration,
                Easing = settings.Easing
            };

            switch (settings.Type)
            {
                case AnimationType.FadeIn:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0) }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0) }
                    });
                    break;
                case AnimationType.SlideInFromLeft:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(-1000, 0))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, 0))
                        }
                    });
                    break;
                case AnimationType.SlideInFromRight:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(1000, 0))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, 0))
                        }
                    });
                    break;
                case AnimationType.SlideInFromTop:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, -1000))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, 0))
                        }
                    });
                    break;
                case AnimationType.SlideInFromBottom:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, 1000))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new TranslateTransform(0, 0))
                        }
                    });
                    break;
                case AnimationType.ZoomIn:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new ScaleTransform(0, 0))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new ScaleTransform(1, 1))
                        }
                    });
                    break;
                case AnimationType.ZoomOut:
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = TimeSpan.FromSeconds(0),
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 0.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new ScaleTransform(2, 2))
                        }
                    });
                    animation.Children.Add(new KeyFrame
                    {
                        KeyTime = settings.Duration,
                        Setters = { 
                            new Avalonia.Styling.Setter(Control.OpacityProperty, 1.0),
                            new Avalonia.Styling.Setter(Control.RenderTransformProperty, new ScaleTransform(1, 1))
                        }
                    });
                    break;
            }

            return animation;
        }

        public async Task AnimateSlide(PptSlideControl slideControl)
        {
            if (slideControl == null || slideControl.SlideGrid.Children.Count == 0)
                return;

            // 按顺序动画每个元素
            foreach (var child in slideControl.SlideGrid.Children)
            {
                if (child is Control control)
                {
                    // 随机选择动画类型
                    var animationTypes = Enum.GetValues(typeof(AnimationType));
                    var random = new Random();
                    var animationType = (AnimationType)animationTypes.GetValue(random.Next(animationTypes.Length));

                    // 播放动画
                    await AnimateElement(control, animationType, TimeSpan.FromSeconds(0.5));

                    // 等待一小段时间
                    await Task.Delay(100);
                }
            }
        }
        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                ClosePresentation();
                ClosePptXmlPresentation();
            }
            catch (Exception ex)
            {

            }

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~PptService()
        {
            Dispose();
        }
    }
}