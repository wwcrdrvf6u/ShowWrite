using SkiaSharp;
using Spire.Presentation;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// PowerPoint服务类,用于在白板模式下嵌入展示PPT,支持动画和放映
/// 使用Spire.Presentation进行内部解析和渲染,无需外部PowerPoint
/// </summary>
public class PowerPointService : IDisposable
{
    private Presentation? _presentation;
    private int _currentSlideIndex = 0;
    private bool _isDisposed;
    private bool _isPlaying;
    private List<SKBitmap>? _slideCache;
    private float _slideWidth;
    private float _slideHeight;

    public bool IsOpen => _presentation != null;
    public int SlideCount => _presentation?.Slides.Count ?? 0;
    public int CurrentSlideIndex => _currentSlideIndex + 1; // 返回1-based索引

    public event Action<int, int>? SlideChanged; // 当前页, 总页数
    public event Action? PresentationEnded;

    public PowerPointService()
    {
        // 初始化
    }

    /// <summary>
    /// 打开PPT文件
    /// </summary>
    public bool OpenPresentation(string filePath)
    {
        try
        {
            ClosePresentation();

            // 使用Spire.Presentation加载PPT
            _presentation = new Presentation();
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

    public void Dispose()
    {
        if (_isDisposed) return;

        try
        {
            ClosePresentation();
        }
        catch (Exception ex)
        {
            
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~PowerPointService()
    {
        Dispose();
    }
}
