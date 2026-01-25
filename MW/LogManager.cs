// [file name]: LogManager.cs
// [file content begin]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShowWrite
{
    /// <summary>
    /// 日志管理器，提供日志相关的管理功能
    /// </summary>
    public class LogManager
    {
        private readonly string _logDirectory;

        public LogManager()
        {
            _logDirectory = Logger.GetLogDirectory();
        }

        /// <summary>
        /// 获取所有日志文件
        /// </summary>
        public List<LogFileInfo> GetLogFiles()
        {
            var logFiles = new List<LogFileInfo>();

            try
            {
                if (!Directory.Exists(_logDirectory))
                    return logFiles;

                var files = Directory.GetFiles(_logDirectory, "app_*.log")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    logFiles.Add(new LogFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Size = fileInfo.Length,
                        CreatedTime = fileInfo.CreationTime,
                        LastModified = fileInfo.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LogManager", $"获取日志文件列表失败: {ex.Message}", ex);
            }

            return logFiles;
        }

        /// <summary>
        /// 获取日志文件内容
        /// </summary>
        public string GetLogContent(string filePath, int maxLines = 1000)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "日志文件不存在";

                var lines = File.ReadLines(filePath).TakeLast(maxLines);
                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                Logger.Error("LogManager", $"读取日志文件失败: {ex.Message}", ex);
                return $"读取日志文件失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 删除旧的日志文件
        /// </summary>
        public void CleanupOldLogs(int keepDays = 7)
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-keepDays);
                var files = Directory.GetFiles(_logDirectory, "app_*.log");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                        Logger.Info("LogManager", $"删除旧日志文件: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LogManager", $"清理旧日志失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取当前内存使用情况的日志摘要
        /// </summary>
        public string GetMemoryUsageSummary()
        {
            var memory = GC.GetTotalMemory(false) / 1024 / 1024;
            var totalMemory = System.Environment.WorkingSet / 1024 / 1024;

            return $"内存使用: {memory} MB / {totalMemory} MB, GC 代数: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";
        }

        /// <summary>
        /// 记录系统状态摘要
        /// </summary>
        public void LogSystemStatus()
        {
            try
            {
                var memory = GC.GetTotalMemory(false) / 1024 / 1024;
                var driveInfo = new DriveInfo(Path.GetPathRoot(_logDirectory));
                var availableSpace = driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024;

                Logger.Info("System", $"系统状态 - 内存: {memory} MB, 磁盘可用空间: {availableSpace} GB, 运行时间: {DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime}");
            }
            catch (Exception ex)
            {
                Logger.Error("LogManager", $"记录系统状态失败: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// 日志文件信息
    /// </summary>
    public class LogFileInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastModified { get; set; }

        public string SizeFormatted => Size < 1024 ? $"{Size} B" : 
                                     Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" : 
                                     $"{Size / (1024.0 * 1024.0):F1} MB";
    }
}
// [file content end]