using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShowWrite
{
    /// <summary>
    /// PostHog 埋点服务（US Cloud），distinct_id 使用 LicenseManager 获取的 UUID
    /// </summary>
    public static class PostHogService
    {
        private const string BatchUrl = "https://us.i.posthog.com/batch/";
        private const string ApiKey = "phc_BFoiOytJcLdEPWvPmsK53q882dDxOJXxrOfX8EooNAQ";

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            return client;
        }

        /// <summary>
        /// 上报事件（fire-and-forget 安全，失败只记录日志不抛异常）
        /// </summary>
        public static async Task CaptureAsync(string eventName, object? properties = null)
        {
            string? distinctId = LicenseManager.Instance.CurrentUuid;
            if (string.IsNullOrEmpty(distinctId))
            {
                Debug.WriteLine($"[PostHog] 跳过事件 {eventName}：UUID 尚未获取");
                return;
            }

            await CaptureWithDistinctIdAsync(distinctId, eventName, properties);
        }

        /// <summary>
        /// 使用指定 distinct_id 上报事件
        /// </summary>
        public static async Task CaptureWithDistinctIdAsync(string distinctId, string eventName, object? properties = null)
        {
            try
            {
                var payload = new
                {
                    api_key = ApiKey,
                    batch = new object[]
                    {
                        new
                        {
                            @event = eventName,
                            distinct_id = distinctId,
                            properties = properties ?? new { },
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(BatchUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[PostHog] 上报失败: {(int)response.StatusCode} - {body}");
                }
                else
                {
                    Debug.WriteLine($"[PostHog] 事件已上报: {eventName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PostHog] 上报异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 同步触发上报（内部异步，不阻塞调用方）
        /// </summary>
        public static void Capture(string eventName, object? properties = null)
        {
            _ = Task.Run(async () => await CaptureAsync(eventName, properties));
        }
    }
}
