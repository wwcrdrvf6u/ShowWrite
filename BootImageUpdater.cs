using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShowWrite
{
    /// <summary>
    /// 启动图远程更新器：启动完成后异步校验并下载最新启动图。
    /// 响应示例：{"version":"1787043680","image_url":"https://github.com/.../boot-package.zip"}
    /// 全流程写入 %TEMP%\showwrite_bootp.log 便于排查。
    /// </summary>
    public static class BootImageUpdater
    {
        private const string DefaultApiUrl = "https://sxvillage.dpdns.org/bootp/api/app";

        /// <summary>获取实际使用的 API 地址（配置为空时回退默认地址）。</summary>
        public static string GetApiUrl(string? apiUrl = null)
        {
            var url = apiUrl ?? Config.Load().BootImageApiUrl;
            return string.IsNullOrWhiteSpace(url) ? DefaultApiUrl : url;
        }
        // GitHub 下载可能较慢，给足超时
        private static readonly HttpClient HttpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        })
        { Timeout = TimeSpan.FromMinutes(2) };

        private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "showwrite_bootp.log");

        /// <summary>
        /// 检查并更新启动图。force=true 时跳过版本比对强制下载。
        /// 返回是否成功完成更新（版本一致跳过也返回 true）。
        /// </summary>
        public static async Task<bool> CheckAndUpdateAsync(string? apiUrl = null, bool force = false)
        {
            Log("=== 启动图更新检查开始 ===");
            try
            {
                var url = GetApiUrl(apiUrl);
                Log($"请求 API: {url}");
                var server = await FetchServerInfoAsync(url);
                if (server == null)
                {
                    Log("API 返回 null，退出");
                    return false;
                }
                Log($"服务端 version={server.Version}, image_url={server.ImageUrl}");

                if (string.IsNullOrEmpty(server.Version) || string.IsNullOrEmpty(server.ImageUrl))
                {
                    Log("version 或 image_url 为空，退出");
                    return false;
                }

                var localVersion = GetLocalVersion();
                Log($"本地版本: {(localVersion ?? "(无 v.json)")}");

                if (!force && !string.IsNullOrEmpty(localVersion) && localVersion == server.Version)
                {
                    Log("版本一致，跳过下载");
                    return true;
                }

                Log("版本不一致或本地无版本文件，开始下载");
                var tempZip = Path.Combine(Path.GetTempPath(), $"showwrite_bootp_{server.Version}.zip");
                Log($"下载到: {tempZip}");
                await DownloadFileAsync(server.ImageUrl, tempZip);

                var zipInfo = new FileInfo(tempZip);
                Log($"下载完成，文件大小: {zipInfo.Length} 字节");

                var bootPath = Config.GetBootPath();
                Log($"启动图目录: {bootPath}");
                if (!Directory.Exists(bootPath))
                {
                    Directory.CreateDirectory(bootPath);
                    Log("创建启动图目录");
                }
                else
                {
                    Log("清空启动图目录");
                    ClearDirectory(bootPath);
                }

                Log("解压中...");
                ZipFile.ExtractToDirectory(tempZip, bootPath, overwriteFiles: true);
                Log("解压完成，文件列表:");
                foreach (var f in Directory.EnumerateFiles(bootPath))
                    Log($"  - {Path.GetFileName(f)}");

                SaveLocalVersion(server.Version);
                Log($"写入 v.json version={server.Version}");

                try { File.Delete(tempZip); Log("清理临时压缩包"); } catch (Exception ex) { Log($"清理临时压缩包失败: {ex.Message}"); }
                Log("=== 启动图更新完成 ===");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[失败] {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Log(ex.StackTrace ?? "(无堆栈)");
                return false;
            }
        }

        /// <summary>请求 API 获取远端启动图信息。</summary>
        public static async Task<BootInfo?> FetchServerInfoAsync(string? apiUrl = null)
        {
            try
            {
                using var resp = await HttpClient.GetAsync(GetApiUrl(apiUrl));
                Log($"API HTTP {(int)resp.StatusCode} {resp.StatusCode}");
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                Log($"API 响应: {json}");
                return JsonSerializer.Deserialize<BootInfo>(json);
            }
            catch (Exception ex)
            {
                Log($"FetchServerInfo 异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>读取本地 v.json 中的启动图版本。</summary>
        public static string? GetLocalVersion()
        {
            var vFile = Path.Combine(Config.GetBootPath(), "v.json");
            if (!File.Exists(vFile))
            {
                Log($"本地 v.json 不存在: {vFile}");
                return null;
            }
            try
            {
                var json = File.ReadAllText(vFile);
                Log($"本地 v.json 内容: {json}");
                var info = JsonSerializer.Deserialize<BootInfo>(json);
                return info?.Version;
            }
            catch (Exception ex)
            {
                Log($"读取 v.json 异常: {ex.Message}");
                return null;
            }
        }

        private static void SaveLocalVersion(string version)
        {
            var vFile = Path.Combine(Config.GetBootPath(), "v.json");
            var json = JsonSerializer.Serialize(new BootInfo { Version = version });
            File.WriteAllText(vFile, json);
        }

        private static async Task DownloadFileAsync(string url, string destPath)
        {
            try
            {
                using var resp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead);
                Log($"下载 HTTP {(int)resp.StatusCode} {resp.StatusCode}");
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(destPath);
                await resp.Content.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                Log($"DownloadFile 异常: {ex.Message}");
                throw;
            }
        }

        private static void ClearDirectory(string path)
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try { File.Delete(file); } catch (Exception ex) { Log($"删除文件失败 {file}: {ex.Message}"); }
            }
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try { Directory.Delete(dir, recursive: true); } catch (Exception ex) { Log($"删除目录失败 {dir}: {ex.Message}"); }
            }
        }

        private static void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFile, line);
            }
            catch { }
        }
    }

    /// <summary>
    /// 启动图远端配置 DTO。
    /// </summary>
    public class BootInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }

    /// <summary>
    /// 启动图目录文件（m.json）：按日历日期指向 bootP 目录内的图片。
    /// 日期格式：yyyy-MM-dd 为一次性日期，MM-dd 为每年循环；end 可选，表示日期区间（支持跨年如 12-25~01-01）。
    /// </summary>
    public class BootManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("days")]
        public List<BootDay> Days { get; set; } = new();

        /// <summary>从 bootP 目录读取 m.json，不存在或解析失败返回 null。</summary>
        public static BootManifest? Load()
        {
            var path = Path.Combine(Config.GetBootPath(), "m.json");
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonSerializer.Deserialize<BootManifest>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>返回今天命中的启动图完整路径（文件不存在则不算命中），未命中返回 null。</summary>
        public string? ResolveImageForDate(DateTime date)
        {
            foreach (var day in Days)
            {
                if (!day.Matches(date) || string.IsNullOrWhiteSpace(day.Image))
                    continue;

                var imagePath = Path.Combine(Config.GetBootPath(), day.Image);
                if (File.Exists(imagePath))
                    return imagePath;
            }
            return null;
        }
    }

    /// <summary>目录文件中的单个日期条目。</summary>
    public class BootDay
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("end")]
        public string? End { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        public bool Matches(DateTime today)
        {
            if (!TryParse(Date, out var startDate, out var startMd))
                return false;

            bool hasEnd = !string.IsNullOrWhiteSpace(End);
            if (hasEnd)
            {
                if (!TryParse(End, out var endDate, out var endMd))
                    return false;

                // 完整日期区间
                if (startDate != default && endDate != default)
                    return today.Date >= startDate && today.Date <= endDate;
                // 每年循环区间（MM-dd，支持跨年）
                if (startMd >= 0 && endMd >= 0)
                {
                    int todayMd = today.Month * 100 + today.Day;
                    return startMd <= endMd
                        ? todayMd >= startMd && todayMd <= endMd
                        : todayMd >= startMd || todayMd <= endMd;
                }
                return false;
            }

            // 单日
            if (startDate != default)
                return today.Date == startDate;
            if (startMd >= 0)
                return today.Month * 100 + today.Day == startMd;
            return false;
        }

        /// <summary>解析日期串：yyyy-MM-dd 输出 exact；MM-dd 输出 md（MM*100+dd）。</summary>
        private static bool TryParse(string? s, out DateTime exact, out int md)
        {
            exact = default;
            md = -1;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            var parts = s.Split('-');
            if (parts.Length == 3)
            {
                return DateTime.TryParseExact(s, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out exact);
            }
            if (parts.Length == 2
                && int.TryParse(parts[0], out int m) && int.TryParse(parts[1], out int d)
                && m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                md = m * 100 + d;
                return true;
            }
            return false;
        }
    }
}
