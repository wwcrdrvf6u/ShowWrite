using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;

namespace ShowWrite
{
    public class LicenseManager
    {
        private const string API_URL = "http://sxvillage.dpdns.org/uuid/";
        private const string API_KEY = "dbd26f55bc894afab916cb71fc678f54";
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShowWrite",
            "license.config");

        private readonly HttpClient _httpClient;
        private static LicenseManager? _instance;
        private static readonly object _lock = new();

        public string? CurrentUuid { get; private set; }
        public string? MotherboardSerial { get; private set; }

        public static LicenseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LicenseManager();
                    }
                }
                return _instance;
            }
        }

        private LicenseManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<string> GetOrCreateLicenseAsync()
        {
            Debug.WriteLine("[LicenseManager] 开始获取或创建许可证");

            string localUuid = ReadLocalLicense();
            if (!string.IsNullOrEmpty(localUuid))
            {
                Debug.WriteLine($"[LicenseManager] 找到本地缓存的 UUID: {localUuid}");
                CurrentUuid = localUuid;
                return localUuid;
            }

            Debug.WriteLine("[LicenseManager] 未找到本地缓存，开始获取主板序列号");

            string motherboardSn = GetMotherboardSerialNumber();
            MotherboardSerial = motherboardSn;
            if (string.IsNullOrEmpty(motherboardSn))
            {
                Debug.WriteLine("[LicenseManager] 错误：无法获取主板序列号");
                throw new Exception("无法获取主板序列号");
            }

            Debug.WriteLine($"[LicenseManager] 主板序列号: {motherboardSn}");

            Debug.WriteLine("[LicenseManager] 开始请求服务器获取 UUID");
            string uuid = await RequestLicenseFromServerAsync(motherboardSn);
            CurrentUuid = uuid;

            Debug.WriteLine($"[LicenseManager] 成功获取 UUID: {uuid}");

            SaveLocalLicense(uuid);
            Debug.WriteLine("[LicenseManager] UUID 已保存到本地");

            return uuid;
        }

        // OEM 机器未烧录序列号时返回的占位符（统一小写比较）
        private static readonly string[] PlaceholderSerials =
        {
            "to be filled by o.e.m",
            "to be filled by o.e.m.",
            "to be filled",
            "none",
            "default string",
            "not specified",
            "not available",
            "unknown",
            "system serial number"
        };

        public string GetMotherboardSerialNumber()
        {
            string serial = QueryWmiSerial("SELECT SerialNumber FROM Win32_BaseBoard");
            if (!IsPlaceholderSerial(serial))
            {
                return serial;
            }

            Debug.WriteLine("[LicenseManager] 主板序列号为占位符，回退到 BIOS 序列号");
            serial = QueryWmiSerial("SELECT SerialNumber FROM Win32_BIOS");
            if (!IsPlaceholderSerial(serial))
            {
                return serial;
            }

            Debug.WriteLine("[LicenseManager] BIOS 序列号也为占位符，回退到注册表 MachineGuid");
            try
            {
                using var key = Microsoft.Win32.RegistryKey
                    .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return guid;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string QueryWmiSerial(string query)
        {
            try
            {
                var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get())
                {
                    var serial = obj["SerialNumber"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial))
                    {
                        return serial.Trim();
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private static bool IsPlaceholderSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
            {
                return true;
            }

            string normalized = serial.Trim().ToLowerInvariant();
            if (PlaceholderSerials.Contains(normalized))
            {
                return true;
            }

            // 全为重复字符的序列号（如 "xxxxxxxx"）也视为占位符
            if (normalized.Length > 1 && normalized.All(c => c == normalized[0]))
            {
                return true;
            }

            return false;
        }

        private string ReadLocalLicense()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string content = File.ReadAllText(ConfigFilePath);
                    return Decrypt(content);
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private void SaveLocalLicense(string uuid)
        {
            try
            {
                string? directory = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string encrypted = Encrypt(uuid);
                File.WriteAllText(ConfigFilePath, encrypted);

                File.SetAttributes(ConfigFilePath, FileAttributes.Hidden);
            }
            catch
            {
            }
        }

        private async Task<string> RequestLicenseFromServerAsync(string motherboardSn)
        {
            var requestData = new
            {
                motherboard_sn = motherboardSn
            };

            string json = JsonSerializer.Serialize(requestData);
            Debug.WriteLine($"[LicenseManager] 请求数据: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", API_KEY);

            try
            {
                Debug.WriteLine($"[LicenseManager] 发送 POST 请求到: {API_URL}");
                HttpResponseMessage response = await _httpClient.PostAsync(API_URL, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"[LicenseManager] 响应状态码: {(int)response.StatusCode}");
                Debug.WriteLine($"[LicenseManager] 响应内容: {responseBody}");

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("uuid", out var uuidElement))
                    {
                        return uuidElement.GetString() ?? string.Empty;
                    }
                    Debug.WriteLine("[LicenseManager] 响应中未找到 uuid 字段");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("请求过于频繁，请稍后重试 (429)");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception("API Key 验证失败 (401)");
                }
                else
                {
                    throw new Exception($"服务器错误：{response.StatusCode} - {responseBody}");
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[LicenseManager] 网络请求异常: {ex.Message}");
                throw new Exception($"网络请求失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseManager] 未知异常: {ex.Message}");
                throw;
            }

            throw new Exception("获取 UUID 失败");
        }

        private static string Encrypt(string plainText)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainBytes);
        }

        private static string Decrypt(string cipherText)
        {
            var cipherBytes = Convert.FromBase64String(cipherText);
            return Encoding.UTF8.GetString(cipherBytes);
        }
    }
}
