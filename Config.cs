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
        public RandomNoteConfig RandomNote { get; set; } = new RandomNoteConfig();

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
