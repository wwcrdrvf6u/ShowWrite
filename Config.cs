using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ShowWrite
{
    public class KeystonePoints
    {
        public float TLX { get; set; }
        public float TLY { get; set; }
        public float TRX { get; set; }
        public float TRY { get; set; }
        public float BRX { get; set; }
        public float BRY { get; set; }
        public float BLX { get; set; }
        public float BLY { get; set; }
        public string AspectRatio { get; set; } = "自由";
    }

    public class PenSettings
    {
        public int Denominator { get; set; } = 30;
        public float RatioMin { get; set; } = 0.3f;
        public float RatioMax { get; set; } = 1.5f;
        public float SpeedThresholdFast { get; set; } = 15f;
        public float SpeedThresholdSlow { get; set; } = 5f;
        public float RatioChangeCoefficient { get; set; } = 0.95f;
        public double PalmEraserThreshold { get; set; } = 5000.0;
        public bool EnablePalmEraser { get; set; } = true;
        public bool IsInfraredScreen { get; set; } = false;
        public double PalmTouchMultiplier { get; set; } = 1.0;
        public int PalmActivationSamples { get; set; } = 2;
        public int PalmReleaseSamples { get; set; } = 3;
    }

    public class RandomNoteConfig
    {
        public bool Enabled { get; set; } = false;
        public int DefaultCameraIndex { get; set; } = -1;
        public int MicrophoneIndex { get; set; } = -1;
        public string SavePath { get; set; } = "";
        public string ShortcutKey { get; set; } = "Alt+Z";
        public int RecordingDurationMinutes { get; set; } = 5;
    }

    public class OcrSettings
    {
        // 启用 OCR 识别目录（禁用后照片栏只显示照片选项卡，隐藏底部选择条）
        public bool EnableOcrDirectory { get; set; } = true;
        // 当前选用的模型集 Key（v4-mobile / v4-server / custom）
        public string ModelSet { get; set; } = "v4-mobile";
        // 是否启用版面分析（PicoDet 中文版面模型，识别标题/正文/表格区域；关闭则用字号聚类猜标题）
        public bool EnableLayout { get; set; } = true;
        // 版面分析模型 Key（picodet-layout-ch = PicoDet 中文 11 类）
        public string LayoutModel { get; set; } = "picodet-layout-ch";
        // 自定义模型路径（ModelSet=="custom" 时生效；cls 留空则用自带）
        public string? CustomDetPath { get; set; }
        public string? CustomRecPath { get; set; }
        public string? CustomDictPath { get; set; }
        public string? CustomClsPath { get; set; }
    }

    public class Config
    {
        public List<int> AvailableCameraIndices { get; set; } = new();
        public Dictionary<int, string> AvailableCameraNames { get; set; } = new();
        public DateTime LastScanTime { get; set; }
        public int CurrentCameraIndex { get; set; } = 0;
        public Dictionary<int, KeystonePoints> CameraKeystoneSettings { get; set; } = new();
        public PenSettings PenSettings { get; set; } = new PenSettings();
        public List<string> EnabledPlugins { get; set; } = new();
        public string Theme { get; set; } = "Dark";
        // 是否显示板中板按钮（默认不显示）
        public bool ShowPictureInPicture { get; set; } = false;
        // 摄像头保活：显示照片时不断开摄像头，返回时立即恢复画面
        public bool CameraKeepAlive { get; set; } = false;
        // 是否显示照片栏滚动条
        public bool ShowPhotoPanelScrollbar { get; set; } = true;
        // 启动图版本 API（默认当前使用的地址）
        public string BootImageApiUrl { get; set; } = "https://sxvillage.dpdns.org/bootp/api/app";
        public RandomNoteConfig RandomNote { get; set; } = new RandomNoteConfig();
        public OcrSettings Ocr { get; set; } = new OcrSettings();

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShowWrite",
            "config.json");

        public static string GetPluginsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShowWrite",
                "PKG");
        }

        /// <summary>
        /// 启动图所在的目录（配置目录下的 bootP 文件夹）。
        /// </summary>
        public static string GetBootPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShowWrite",
                "bootP");
        }

        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
            }
            catch { }

            return new Config();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
