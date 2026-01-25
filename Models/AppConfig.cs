using System.Collections.Generic;
using Newtonsoft.Json;

namespace ShowWrite.Models
{
    public class AppConfig
    {
        public bool StartMaximized { get; set; } = true;
        public bool AutoStartCamera { get; set; } = true;
        public int CameraIndex { get; set; } = 0;
        public double DefaultPenWidth { get; set; } = 3.0;
        public string DefaultPenColor { get; set; } = "#FF000000";
        public bool EnableHardwareAcceleration { get; set; } = true;
        public bool EnableFrameProcessing { get; set; } = true;
        public int FrameRateLimit { get; set; } = 0;
        public int EraserSizeMethod { get; set; } = 0;
        public double AreaThreshold { get; set; } = 2000.0;
        public double SpeedThreshold { get; set; } = 50.0;
        public double ManualEraserSize { get; set; } = 20.0;
        public bool DeveloperMode { get; set; } = false;
        public double PalmEraserThreshold { get; set; } = 100.0;
        public bool EnablePalmEraser { get; set; } = true;
        public string Theme { get; set; } = "Light";

        // 每个摄像头的配置字典（新格式）
        public Dictionary<int, CameraConfig> CameraConfigs { get; set; } = new Dictionary<int, CameraConfig>();

        // 旧的校正配置（向后兼容，不推荐使用）
        [JsonProperty("CameraCorrections")]
        public Dictionary<int, object> LegacyCameraCorrections { get; set; } = new Dictionary<int, object>();
    }
}