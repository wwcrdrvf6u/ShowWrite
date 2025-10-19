using AForge;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowWrite.Models
{
    public enum EraserSizeMethod
    {
        AreaRecognition,
        SpeedRecognition,
        ManualAdjustment
    }

    public class AppConfig
    {
        public bool StartMaximized { get; set; } = true;
        public bool AutoStartCamera { get; set; } = true;
        public int CameraIndex { get; set; } = 0;
        public double DefaultPenWidth { get; set; } = 2.0;
        public string? DefaultPenColor { get; set; } = "#FF000000";
        public bool EnableHardwareAcceleration { get; set; } = true;
        public bool EnableFrameProcessing { get; set; } = false;
        public int FrameRateLimit { get; set; } = 25;
        public EraserSizeMethod? EraserSizeMethod { get; set; }
        public double? AreaThreshold { get; set; } = 2000.0;
        public double? SpeedThreshold { get; set; } = 50.0;
        public double? ManualEraserSize { get; set; } = 20.0;
        public bool? DeveloperMode { get; set; } = false;
        public double PalmEraserThreshold { get; set; } = 100.0;
        public bool EnablePalmEraser { get; set; } = true;

        // 修改：为每个摄像头存储独立的矫正信息
        public Dictionary<int, CameraCorrectionConfig> CameraCorrections { get; set; } = new Dictionary<int, CameraCorrectionConfig>();

        public AppConfig()
        {
            EraserSizeMethod = Models.EraserSizeMethod.AreaRecognition;
        }
    }

    // 新增：摄像头矫正配置类
    public class CameraCorrectionConfig
    {
        public List<IntPoint>? CorrectionPoints { get; set; }
        public int SourceWidth { get; set; } = 0;
        public int SourceHeight { get; set; } = 0;

        // 新增：保存原始摄像头分辨率，用于缩放计算
        public int OriginalCameraWidth { get; set; }
        public int OriginalCameraHeight { get; set; }
    }
}
