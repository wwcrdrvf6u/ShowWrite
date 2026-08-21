using RapidOcrNet;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ShowWrite
{
    /// <summary>OCR 单行结果：文本 + 中心坐标(Cx,Cy) + 框高(≈字号) + 标题层级(0=普通正文)。</summary>
    public sealed record OcrLine(string Text, double Cx, double Cy, double BoxHeight, int Level = 0);

    /// <summary>
    /// OCR 服务（单例）。后端：基于 RapidOcrNet (PaddleOCR PP-OCRv4 ONNX)，轻量(~80MB)，det/cls NuGet 自带、rec+dict 下载到 AppData。
    /// </summary>
    public sealed class OcrService : IDisposable
    {
        public static OcrService Instance { get; } = new OcrService();

        // NuGet 自带（与 exe 同级的 models/v5/，语言无关）
        private const string BundledDetName = "ch_PP-OCRv5_mobile_det.onnx";
        private const string BundledClsName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";

        // 国内可直连的字典源（gitee 镜像）
        private const string DefaultDictUrl =
            "https://gitee.com/paddlepaddle/PaddleOCR/raw/release/2.6/ppocr/utils/ppocr_keys_v1.txt";
        private const string DefaultDictName = "ppocrv5_chinese_dict.txt";

        private static readonly string AppRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShowWrite", "ocr", "models");

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            // modelscope 对象存储有防盗链：默认 HttpClient 无 User-Agent 会被拒(403)。
            // 加浏览器 UA 后 modelscope(206) / gitee(200) 均可正常下载。
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return c;
        }

        /// <summary>一个可选模型集。</summary>
        public sealed class OcrModelSet
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public string Desc { get; set; } = "";
            public long SizeMB { get; set; }
            // det：DetName/DetUrl 为 null 表示用 NuGet 自带的 mobile 检测模型
            public string? DetName { get; set; }
            public string? DetUrl { get; set; }
            // cls：ClsName/ClsUrl 为 null 表示用 NuGet 自带的分类模型（发布时可能缺失）
            public string? ClsName { get; set; }
            public string? ClsUrl { get; set; }
            public string RecName { get; set; } = "";
            public string RecUrl { get; set; } = "";
            public string DictName { get; set; } = DefaultDictName;
            public string DictUrl { get; set; } = DefaultDictUrl;
            public bool DetBundled => DetUrl == null;
            public bool ClsBundled => ClsUrl == null;
        }

        /// <summary>可选模型集目录。采用 PP-OCRv4 中文（rec 与 ppocr_keys_v1.txt 字典配套，不乱码）。</summary>
        public static readonly OcrModelSet[] Catalog =
        {
            new OcrModelSet
            {
                Key = "v4-mobile",
                Name = "PP-OCRv4 手机版",
                Desc = "快速，约 10MB",
                SizeMB = 10,
                DetName = "ch_PP-OCRv4_det_mobile.onnx",
                DetUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx",
                ClsName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                ClsUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                RecName = "ch_PP-OCRv4_rec_mobile.onnx",
                RecUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_mobile.onnx",
            },
            new OcrModelSet
            {
                Key = "v4-server",
                Name = "PP-OCRv4 服务器版",
                Desc = "高精度，约 80MB",
                SizeMB = 80,
                DetName = "ch_PP-OCRv4_det_server.onnx",
                DetUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_server.onnx",
                ClsName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                ClsUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                RecName = "ch_PP-OCRv4_rec_server.onnx",
                RecUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_server.onnx",
            },
            new OcrModelSet
            {
                // 混合方案：RapidOcrNet 4.0.2 对 server det 后处理有兼容性问题（det 跑完但 0 框），
                // 用 mobile det（已知能工作）+ server rec（高精度识别）兼顾速度与精度。
                Key = "v4-hybrid",
                Name = "PP-OCRv4 混合版",
                Desc = "mobile检测+server识别，约 90MB",
                SizeMB = 90,
                DetName = "ch_PP-OCRv4_det_mobile.onnx",
                DetUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/det/ch_PP-OCRv4_det_mobile.onnx",
                ClsName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                ClsUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
                RecName = "ch_PP-OCRv4_rec_server.onnx",
                RecUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv4/rec/ch_PP-OCRv4_rec_server.onnx",
            },
        };

        public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "models", "v5");
        public static string ModelsRoot => AppRoot;

        private static string SetDir(string key) => Path.Combine(AppRoot, key);
        private static string BundledDetPath => Path.Combine(BundledDir, BundledDetName);
        private static string BundledClsPath => Path.Combine(BundledDir, BundledClsName);

        private static OcrModelSet? FindSet(string key)
        {
            foreach (var s in Catalog) if (s.Key == key) return s;
            return null;
        }

        /// <summary>取当前配置选定的模型集（custom 返回 null）。</summary>
        public OcrModelSet? ActiveSet => FindSet(Config.Load().Ocr.ModelSet);

        // ---------- 路径解析（活动模型） ----------
        public string DetPath => ResolveDet();
        public string ClsPath => ResolveCls();
        public string RecPath => ResolveRec();
        public string DictPath => ResolveDict();

        private string ResolveDet()
        {
            var cfg = Config.Load().Ocr;
            if (cfg.ModelSet == "custom")
                return !string.IsNullOrWhiteSpace(cfg.CustomDetPath) ? cfg.CustomDetPath! : BundledDetPath;
            var set = FindSet(cfg.ModelSet) ?? Catalog[0];
            return set.DetBundled ? BundledDetPath : Path.Combine(SetDir(set.Key), set.DetName!);
        }
        private string ResolveCls()
        {
            var cfg = Config.Load().Ocr;
            if (cfg.ModelSet == "custom" && !string.IsNullOrWhiteSpace(cfg.CustomClsPath))
                return cfg.CustomClsPath!;
            var set = FindSet(cfg.ModelSet) ?? Catalog[0];
            // 模型集配了 cls 下载 URL：优先用 SetDir 里下载的，找不到才回退 NuGet 自带的
            if (!set.ClsBundled)
            {
                var path = Path.Combine(SetDir(set.Key), set.ClsName!);
                if (File.Exists(path)) return path;
            }
            return BundledClsPath;
        }
        private string ResolveRec()
        {
            var cfg = Config.Load().Ocr;
            if (cfg.ModelSet == "custom")
                return cfg.CustomRecPath ?? "";
            var set = FindSet(cfg.ModelSet) ?? Catalog[0];
            return Path.Combine(SetDir(set.Key), set.RecName);
        }
        private string ResolveDict()
        {
            var cfg = Config.Load().Ocr;
            if (cfg.ModelSet == "custom")
                return cfg.CustomDictPath ?? "";
            var set = FindSet(cfg.ModelSet) ?? Catalog[0];
            return Path.Combine(SetDir(set.Key), set.DictName);
        }

        public bool DetReady => File.Exists(DetPath);
        public bool ClsReady => File.Exists(ClsPath);
        public bool RecReady => File.Exists(RecPath);
        public bool DictReady => File.Exists(DictPath);
        public bool IsModelReady => DetReady && ClsReady && RecReady && DictReady;

        /// <summary>某模型集是否已下载到 AppData（自带的 det/cls 视为就绪）。</summary>
        public bool IsSetDownloaded(string key)
        {
            var set = FindSet(key);
            if (set == null) return false;
            var dir = SetDir(key);
            if (!set.DetBundled && !File.Exists(Path.Combine(dir, set.DetName!))) return false;
            if (!File.Exists(Path.Combine(dir, set.RecName))) return false;
            if (!File.Exists(Path.Combine(dir, set.DictName))) return false;
            return true;
        }

        /// <summary>活动模型文件状态（供设置页展示）：列出 det/cls/rec/dict。</summary>
        public IEnumerable<(string Name, string Path, bool Ready, long Size)> GetModelStatus()
        {
            (string, string, bool, long) Stat(string label, string path)
            {
                var fi = File.Exists(path) ? new FileInfo(path) : null;
                return (label, path, fi != null, fi?.Length ?? 0);
            }
            yield return Stat("检测模型 (det)", DetPath);
            yield return Stat("方向分类 (cls)", ClsPath);
            yield return Stat("中文识别 (rec)", RecPath);
            yield return Stat("中文字典 (dict)", DictPath);
        }

        /// <summary>下载指定模型集到 AppData（已存在则跳过）。</summary>
        public async Task<bool> DownloadSetAsync(string key,
            IProgress<(int Percent, string Status)>? progress, CancellationToken ct)
        {
            var set = FindSet(key);
            if (set == null)
            {
                progress?.Report((100, "未知模型集"));
                return false;
            }
            Directory.CreateDirectory(SetDir(key));

            var tasks = new List<(string Url, string Dest, string Label)>();
            if (!set.DetBundled)
                tasks.Add((set.DetUrl!, Path.Combine(SetDir(key), set.DetName!), "检测模型"));
            if (!set.ClsBundled)
                tasks.Add((set.ClsUrl!, Path.Combine(SetDir(key), set.ClsName!), "方向分类"));
            tasks.Add((set.RecUrl, Path.Combine(SetDir(key), set.RecName), "识别模型"));
            tasks.Add((set.DictUrl, Path.Combine(SetDir(key), set.DictName), "中文字典"));

            int totalFiles = tasks.Count;
            int doneFiles = 0;
            foreach (var (url, dest, label) in tasks)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(dest)) { doneFiles++; continue; }
                var basePct = doneFiles * 100 / totalFiles;
                await DownloadFileAsync(url, dest, label,
                    new Progress<(int Percent, string Status)>(p =>
                        progress?.Report((basePct + p.Percent / totalFiles, $"下载{label} {p.Percent}%"))),
                    ct);
                doneFiles++;
            }

            // 引擎若已初始化，文件可能变化，强制重建
            lock (_initLock)
            {
                if (_initialized) { _ocr?.Dispose(); _ocr = null; _initialized = false; }
            }
            progress?.Report((100, $"{set.Name} 下载完成"));
            return IsSetDownloaded(key);
        }

        /// <summary>确保当前活动模型集就绪：下载/选择模型。</summary>
        public async Task<bool> EnsureModelsAsync(IProgress<(int Percent, string Status)>? progress, CancellationToken ct)
        {
            var cfg = Config.Load().Ocr;
            if (cfg.ModelSet == "custom")
            {
                if (IsModelReady) return true;
                progress?.Report((100, "自定义模型路径无效，请在设置中检查"));
                return false;
            }
            if (IsModelReady) return true;
            var key = string.IsNullOrEmpty(cfg.ModelSet) ? "v4-mobile" : cfg.ModelSet;
            var set = FindSet(key);
            // 模型集没配 cls 下载 URL 时，必须有 NuGet 自带的 cls（发布时可能缺失）
            if (set != null && set.ClsBundled && !File.Exists(BundledClsPath))
            {
                progress?.Report((100, $"缺少自带分类模型: {BundledDir}"));
                return false;
            }
            try
            {
                return await DownloadSetAsync(key, progress, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                progress?.Report((100, $"下载失败: {ex.Message}"));
                return false;
            }
        }

        private static async Task DownloadFileAsync(string url, string destPath, string label,
            IProgress<(int Percent, string Status)>? progress, CancellationToken ct)
        {
            progress?.Report((0, $"正在下载{label}..."));
            var tmpPath = destPath + ".part";
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n, ct);
                    read += n;
                    if (total > 0)
                    {
                        var pct = (int)(read * 100 / total);
                        progress?.Report((pct, $"下载{label} {pct}%"));
                    }
                }
            }
            catch { TryDelete(tmpPath); throw; }
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmpPath, destPath);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }

        private readonly object _initLock = new();
        private RapidOcr? _ocr;
        private bool _initialized;
        private bool _disposed;

        /// <summary>OCR 引擎是否已加载（持锁查询，供 UI 决定是否需要触发惰性重载）。</summary>
        public bool IsInitialized
        {
            get
            {
                lock (_initLock) { return _initialized; }
            }
        }

        /// <summary>初始化 OCR 引擎（线程安全、惰性）。</summary>
        public bool TryInitialize(out string error)
        {
            error = string.Empty;
            lock (_initLock)
            {
                if (_initialized) return true;
                if (!IsModelReady)
                {
                    error = "模型未就绪，请在 设置 → OCR 中下载/选择模型";
                    return false;
                }
                try
                {
                    _ocr = new RapidOcr();
                    LogDebug($"[InitModels 前] 模型={Config.Load().Ocr.ModelSet} det={DetPath} cls={ClsPath} rec={RecPath} dict={DictPath}");
                    _ocr.InitModels(detPath: DetPath, clsPath: ClsPath, recPath: RecPath, keysPath: DictPath);
                    _initialized = true;
                    LogDebug($"[InitModels 后] 成功加载引擎");
                    return true;
                }
                catch (Exception ex)
                {
                    _ocr?.Dispose();
                    _ocr = null;
                    error = $"初始化 OCR 引擎失败: {ex.Message}";
                    LogDebug($"[InitModels 异常] {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>异步初始化 OCR 引擎：把同步的 RapidOcr 构造+InitModels 加载下到线程池，
        /// 避免阻塞 UI 线程（否则摄像头预览靠 UI 线程 DispatcherTimer 驱动会失帧冻结）。</summary>
        public Task<(bool Ok, string Error)> TryInitializeAsync()
        {
            return Task.Run(() =>
            {
                bool ok = TryInitialize(out var err);
                return (ok, err);
            });
        }

        /// <summary>
        /// 释放已加载的 OCR 引擎（ONNX Runtime 占用大，模型常驻进程内存即使不推理也吃几百 MB）。
        /// 下次 OCR 触发时 TryInitializeAsync 会惰性重新加载。线程安全。
        /// </summary>
        public void ReleaseEngine()
        {
            lock (_initLock)
            {
                if (!_initialized) return;
                try { _ocr?.Dispose(); } catch { }
                _ocr = null;
                _initialized = false;
            }
        }

        /// <summary>调试日志：写到 AppData\ShowWrite\ocr\debug.log，定位 v4-server 识别空的原因。</summary>
        private static void LogDebug(string message)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ShowWrite", "ocr", "debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>对单张图片 OCR。
        /// 返回按从上到下、从左到右排序后的结构化文本行（含中心坐标与框高，框高代表字号）。</summary>
        public async Task<List<OcrLine>> RecognizeAsync(string imagePath, CancellationToken ct)
        {
            return await RecognizeWithPaddleAsync(imagePath, ct);
        }

        /// <summary>
        /// 根据当前模型集构造推理参数。以 RapidOcrOptions.Default 为基础（保留 det/cls/rec 后处理的
        /// 所有正确阈值：BoxScoreThresh=0.5/BoxThresh=0.3/UnClipRatio=1.6/MinHeight=30/TextScore=0.5 等），
        /// 仅覆盖 DoAngle/MostAngle 跳过角度分类。
        /// 关键：LimitSideLen 必须匹配 det 模型训练时的 resize_long：
        /// - v4-mobile 训练时 limit_side_len=736（RapidOcrOptions.Default 即此值）
        /// - v4-server 训练时 resize_long=960，用 736 会导致预处理尺寸不匹配，正文区域概率响应掉到 0，
        ///   det 检测不到任何文本框，识别结果为空（参考 PaddleOCR issue #17974）
        /// 注意：不能 new RapidOcrOptions() 只设部分属性——实例默认值全是 0/false，会让后处理异常。
        /// </summary>
        private RapidOcrOptions BuildInferenceOptions()
        {
            var src = RapidOcrOptions.Default;
            // server det 训练用 resize_long=960；mobile det 用 736（Default 即此值）。
            // v4-hybrid 用 mobile det，所以用 736。只有纯 v4-server 用 960。
            int limitSideLen = ActiveSet?.Key == "v4-server" ? 960 : src.LimitSideLen;
            return new RapidOcrOptions
            {
                Padding = src.Padding,
                ImgResize = src.ImgResize,
                LimitSideLen = limitSideLen,
                MaxSideLen = src.MaxSideLen,
                MinSideLen = src.MinSideLen,
                WidthHeightRatio = src.WidthHeightRatio,
                MinHeight = src.MinHeight,
                TextScore = src.TextScore,
                ClsThresh = src.ClsThresh,
                ClsPreserveAspectRatio = src.ClsPreserveAspectRatio,
                BoxScoreThresh = src.BoxScoreThresh,
                BoxThresh = src.BoxThresh,
                UnClipRatio = src.UnClipRatio,
                DoAngle = false,       // 跳过角度分类（拍摄正向文档）
                MostAngle = false,
                ReturnWordBox = src.ReturnWordBox,
                ReturnSingleCharBox = src.ReturnSingleCharBox,
            };
        }

        /// <summary>PaddleOCR 后端识别。</summary>
        public async Task<List<OcrLine>> RecognizeWithPaddleAsync(string imagePath, CancellationToken ct)
        {
            var result = new List<OcrLine>();
            if (!File.Exists(imagePath)) return result;
            RapidOcr? engine;
            lock (_initLock) { engine = _ocr; }
            if (engine == null) return result;

            // 整段（图片解码+ORT推理+后处理）下到线程池：避免 SKBitmap.Decode 与推理同步占用 UI 线程，
            // 导致摄像头预览的 DispatcherTimer 失帧、画面停住。
            // 每次构造 options：根据当前模型集（v4-server vs v4-mobile）选正确的 LimitSideLen
            var options = BuildInferenceOptions();
            return await Task.Run(async () =>
            {
                using var bmp = SKBitmap.Decode(imagePath);
                if (bmp == null) return result;

                var progress = new Progress<(int Completed, int Total)>();
                OcrResult? ocrResult = null;
                try
                {
                    ocrResult = await engine.DetectAsync(bmp, options, progress, ct);
                }
                catch (Exception ex)
                {
                    LogDebug($"[v4-server 推理异常] 模型={ActiveSet?.Key} 图={imagePath} 异常={ex.GetType().Name}: {ex.Message}");
                    return result;
                }

                // 诊断日志：记录推理结果状态，定位是 det 没检测到框还是 rec 识别不出文字
                int blockCount = ocrResult?.TextBlocks?.Length ?? 0;
                int nonEmptyText = 0;
                if (ocrResult?.TextBlocks != null)
                    foreach (var b in ocrResult.TextBlocks)
                        if (!string.IsNullOrWhiteSpace(b.Text)) nonEmptyText++;
                LogDebug($"[推理结果] 模型={ActiveSet?.Key} LimitSideLen={options.LimitSideLen} 图尺寸={bmp.Width}x{bmp.Height} " +
                         $"TextBlocks={blockCount} 有文字={nonEmptyText} DbNetTime={ocrResult?.DbNetTime} DetectTime={ocrResult?.DetectTime}");

                if (ocrResult?.TextBlocks == null) return result;

                foreach (var b in ocrResult.TextBlocks)
                {
                    if (string.IsNullOrWhiteSpace(b.Text)) continue;
                    var p = b.BoxPoints;
                    if (p == null || p.Length < 4) { result.Add(new OcrLine(b.Text.Trim(), 0, 0, 0)); continue; }
                    double cx = (p[0].X + p[1].X + p[2].X + p[3].X) / 4.0;
                    double cy = (p[0].Y + p[1].Y + p[2].Y + p[3].Y) / 4.0;
                    double minY = Math.Min(Math.Min(p[0].Y, p[1].Y), Math.Min(p[2].Y, p[3].Y));
                    double maxY = Math.Max(Math.Max(p[0].Y, p[1].Y), Math.Max(p[2].Y, p[3].Y));
                    result.Add(new OcrLine(b.Text.Trim(), cx, cy, maxY - minY));
                }
                SortLines(result);
                return result;
            }, ct);
        }

        /// <summary>排序：从上到下，同行从左到右(容差 5%)。</summary>
        private static void SortLines(List<OcrLine> result)
        {
            result.Sort((a, b) =>
            {
                if (Math.Abs(a.Cy - b.Cy) <= Math.Max(a.Cy, b.Cy) * 0.05) return a.Cx.CompareTo(b.Cx);
                return a.Cy.CompareTo(b.Cy);
            });
        }

        /// <summary>从 OCR 行提取标题条目并判定层级（1=大标题，2=中，3=小）。
        /// 优先按编号正则；若全无编号，则按框高（字号）聚类分档。</summary>
        public static List<OcrLine> ExtractTitles(List<OcrLine> lines)
        {
            var titled = new List<OcrLine>();
            foreach (var ln in lines)
            {
                int lv = LevelByNumber(ln.Text);
                if (lv > 0) titled.Add(new OcrLine(ln.Text, ln.Cx, ln.Cy, ln.BoxHeight, lv));
            }
            if (titled.Count > 0) return titled;

            // 无编号：按框高（字号）聚类，取字号较大的若干行作为标题
            if (lines.Count == 0) return titled;
            var sorted = new List<OcrLine>(lines);
            sorted.Sort((a, b) => b.BoxHeight.CompareTo(a.BoxHeight));
            // 阈值 = 中位框高 *1.1，高于阈值的视为标题（level 2）
            double median = sorted[sorted.Count / 2].BoxHeight;
            double thr = median * 1.1;
            foreach (var ln in lines)
                if (ln.BoxHeight >= thr)
                    titled.Add(new OcrLine(ln.Text, ln.Cx, ln.Cy, ln.BoxHeight, 2));
            return titled;
        }

        private static readonly System.Text.RegularExpressions.Regex[] _h1Patterns =
        {
            H(@"^第[一二三四五六七八九十百千零]+[章节篇部课]"),
            H(@"^[一二三四五六七八九十百千零]+[、.．]"),
            H(@"^Part\s+\d+", "i"),
        };
        private static readonly System.Text.RegularExpressions.Regex _h2Num = H(@"^\d+[、.．]");
        private static readonly System.Text.RegularExpressions.Regex _h2Dot = H(@"^\d+(\.\d+)+[、.．]?");
        private static readonly System.Text.RegularExpressions.Regex _h3Paren = H(@"^[（(]\s*\d+\s*[)）.、]");

        private static System.Text.RegularExpressions.Regex H(string pattern, string? options = null)
            => string.IsNullOrEmpty(options)
                ? new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled)
                : new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>按编号判断层级：1/2/3，0=非编号标题。</summary>
        private static int LevelByNumber(string text)
        {
            var t = text.Trim();
            if (t.Length == 0) return 0;
            foreach (var p in _h1Patterns) if (p.IsMatch(t)) return 1;
            if (_h2Dot.IsMatch(t)) return System.Text.RegularExpressions.Regex.Matches(t, @"\d").Count > 0
                ? (t.Split('.', '．').Length - 1) + 1 : 2; // 1.1→2, 1.1.1→3
            if (_h2Num.IsMatch(t)) return 2;
            if (_h3Paren.IsMatch(t)) return 3;
            return 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_initLock)
            {
                _ocr?.Dispose();
                _ocr = null;
            }
        }
    }
}
