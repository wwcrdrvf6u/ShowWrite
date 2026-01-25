// [file name]: Logger.cs
// [file content begin]
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShowWrite
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 日志系统核心类
    /// </summary>
    public static class Logger
    {
        private static readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private static readonly ManualResetEvent _logEvent = new ManualResetEvent(false);
        private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private static StreamWriter _logWriter;
        private static bool _isInitialized = false;
        private static LogLevel _minLogLevel = LogLevel.Debug;
        private static string _logDirectory;
        private static string _currentLogFile;

        // 视频帧接收状态记录
        private static bool _isFirstFrameLogged = false;

        /// <summary>
        /// 日志条目
        /// </summary>
        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public string Category { get; set; }
            public Exception Exception { get; set; }
        }

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        public static void Initialize(string logDirectory = null, LogLevel minLogLevel = LogLevel.Debug)
        {
            if (_isInitialized) return;

            _minLogLevel = minLogLevel;
            _logDirectory = logDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            try
            {
                // 确保日志目录存在
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                // 创建当前日志文件
                _currentLogFile = Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _logWriter = new StreamWriter(_currentLogFile, true, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                // 启动日志处理线程
                Task.Run(() => ProcessLogQueue(_cancellationTokenSource.Token));

                _isInitialized = true;

                // 记录启动日志
                Info("Logger", "日志系统初始化完成");
                Info("Logger", $"日志文件: {_currentLogFile}");
                Info("Logger", $"最低日志级别: {_minLogLevel}");
            }
            catch (Exception ex)
            {
                // 如果文件日志失败，至少输出到控制台
                Console.WriteLine($"日志系统初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭日志系统
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                _logEvent.Set();

                // 等待一段时间让队列处理完成
                Thread.Sleep(1000);

                _logWriter?.Close();
                _logWriter?.Dispose();
                _isInitialized = false;

                Info("Logger", "日志系统已关闭");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭日志系统时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置最低日志级别
        /// </summary>
        public static void SetMinLogLevel(LogLevel level)
        {
            _minLogLevel = level;
            Info("Logger", $"设置最低日志级别为: {level}");
        }

        /// <summary>
        /// 记录调试信息
        /// </summary>
        public static void Debug(string category, string message, Exception ex = null)
        {
            Log(LogLevel.Debug, category, message, ex);
        }

        /// <summary>
        /// 记录一般信息
        /// </summary>
        public static void Info(string category, string message, Exception ex = null)
        {
            Log(LogLevel.Info, category, message, ex);
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        public static void Warning(string category, string message, Exception ex = null)
        {
            Log(LogLevel.Warning, category, message, ex);
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        public static void Error(string category, string message, Exception ex = null)
        {
            Log(LogLevel.Error, category, message, ex);
        }

        /// <summary>
        /// 记录视频帧接收状态（只记录第一次）
        /// </summary>
        public static void LogVideoFrameStatus(string category, bool isFrameReceived, string additionalInfo = "")
        {
            if (!_isFirstFrameLogged)
            {
                string status = isFrameReceived ? "成功接收" : "未能接收";
                string message = $"视频帧状态: {status}";
                if (!string.IsNullOrEmpty(additionalInfo))
                {
                    message += $", {additionalInfo}";
                }

                Info(category, message);
                _isFirstFrameLogged = true;
            }
        }

        /// <summary>
        /// 重置视频帧记录状态（用于重新开始记录）
        /// </summary>
        public static void ResetVideoFrameLogging()
        {
            _isFirstFrameLogged = false;
        }

        /// <summary>
        /// 记录日志的核心方法
        /// </summary>
        private static void Log(LogLevel level, string category, string message, Exception ex)
        {
            if (level < _minLogLevel) return;

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Exception = ex
            };

            _logQueue.Enqueue(logEntry);
            _logEvent.Set();
        }

        /// <summary>
        /// 处理日志队列
        /// </summary>
        private static async Task ProcessLogQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logEvent.WaitOne(1000);

                    while (_logQueue.TryDequeue(out var logEntry))
                    {
                        await WriteLogEntry(logEntry);
                    }

                    _logEvent.Reset();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"日志处理线程异常: {ex.Message}");
                    await Task.Delay(1000);
                }
            }

            // 退出前处理剩余日志
            while (_logQueue.TryDequeue(out var logEntry))
            {
                await WriteLogEntry(logEntry);
            }
        }

        /// <summary>
        /// 写入日志条目
        /// </summary>
        private static async Task WriteLogEntry(LogEntry entry)
        {
            try
            {
                var logMessage = FormatLogMessage(entry);

                // 写入文件
                if (_logWriter != null)
                {
                    await _logWriter.WriteLineAsync(logMessage);
                }

                // 同时输出到控制台（在调试模式下）
#if DEBUG
                Console.WriteLine(logMessage);
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 格式化日志消息
        /// </summary>
        private static string FormatLogMessage(LogEntry entry)
        {
            var levelStr = entry.Level.ToString().ToUpper().PadRight(7);
            var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var category = entry.Category.PadRight(15);

            var message = $"{timestamp} [{levelStr}] [{category}] {entry.Message}";

            if (entry.Exception != null)
            {
                message += $"\n{timestamp} [EXCEPTION] [{category}] {entry.Exception.GetType().Name}: {entry.Exception.Message}";
                message += $"\n{timestamp} [STACKTRACE] [{category}] {entry.Exception.StackTrace}";
            }

            return message;
        }

        /// <summary>
        /// 获取当前日志文件路径
        /// </summary>
        public static string GetCurrentLogFile()
        {
            return _currentLogFile;
        }

        /// <summary>
        /// 获取日志目录
        /// </summary>
        public static string GetLogDirectory()
        {
            return _logDirectory;
        }
    }
}
// [file content end]