using System.Collections.Generic;
using System.Drawing;

namespace ShowWrite.Models
{
    public class CameraConfig
    {
        public int CameraIndex { get; set; } = -1;
        public string CameraName { get; set; } = string.Empty;
        public ImageAdjustments Adjustments { get; set; } = new ImageAdjustments();
        public List<PointF> PerspectivePoints { get; set; } = new();

        // 校正相关参数
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public int OriginalCameraWidth { get; set; }
        public int OriginalCameraHeight { get; set; }
        public bool HasCorrection { get; set; } = false;

        // 将PointF转换为AForge.IntPoint
        public List<AForge.IntPoint> GetCorrectionPoints()
        {
            var points = new List<AForge.IntPoint>();
            foreach (var point in PerspectivePoints)
            {
                points.Add(new AForge.IntPoint((int)point.X, (int)point.Y));
            }
            return points;
        }

        // 设置校正点
        public void SetCorrectionPoints(List<AForge.IntPoint> points)
        {
            PerspectivePoints.Clear();
            foreach (var point in points)
            {
                PerspectivePoints.Add(new PointF(point.X, point.Y));
            }
            HasCorrection = points.Count == 4;
        }

        // 清除校正
        public void ClearCorrection()
        {
            PerspectivePoints.Clear();
            HasCorrection = false;
        }
    }

    public class ImageAdjustments
    {
        public int Brightness { get; set; } = 100; // 100=原样
        public int Contrast { get; set; } = 100;   // 100=原样
        public int Orientation { get; set; } = 0;  // 角度
        public bool FlipHorizontal { get; set; } = false;
    }
}