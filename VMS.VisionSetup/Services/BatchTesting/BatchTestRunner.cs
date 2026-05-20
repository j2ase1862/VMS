using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    /// <summary>
    /// 한 이미지 처리 결과 — CSV 작성용.
    /// </summary>
    public class BatchImageResult
    {
        public string ImagePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public double TotalMs { get; set; }
        public string? FailureReason { get; set; }
        /// <summary>도구 ID → VisionResult. CSV 생성기가 이걸 동적 컬럼으로 펼침.</summary>
        public Dictionary<string, VisionResult> ToolResults { get; set; } = new();

        /// <summary>임계치(Recipe.Criteria) 통과 여부 — 모든 도구 결과가 정의된 범위 안에 있을 때 true.</summary>
        public bool ThresholdsPassed { get; set; } = true;

        /// <summary>임계치 위반 메시지 목록 — Failure Browser/CSV 에서 사람이 읽는 용.</summary>
        public List<string> ThresholdViolations { get; set; } = new();

        /// <summary>DataGrid 표시용 — ImagePath의 파일명 부분.</summary>
        public string FileName => string.IsNullOrEmpty(ImagePath) ? "" : Path.GetFileName(ImagePath);
    }

    public class BatchTestConfig
    {
        public string ImageFolder { get; set; } = string.Empty;
        public bool RecurseSubfolders { get; set; }
        public string[] Extensions { get; set; } = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
        public string OutputCsvPath { get; set; } = string.Empty;
        public bool SaveFailureOverlays { get; set; } = true;
        public string? FailureOverlayDir { get; set; }

        /// <summary>도구별 Pass/Fail 임계치. null이면 임계치 평가 생략.</summary>
        public PassFailCriteria? Criteria { get; set; }
    }

    /// <summary>
    /// 폴더 이미지 일괄 검사 러너 — 현재 로드된 레시피(VisionService)를 각 이미지에 적용 후
    /// per-image 결과 + 도구별 결과 데이터를 수집. CSV writer가 후속으로 동적 컬럼 생성.
    /// </summary>
    public class BatchTestRunner
    {
        private readonly IVisionService _visionService;

        public event Action<int, int>? Progress;            // (current, total)
        public event Action<BatchImageResult>? ImageDone;   // 매 이미지 완료
        public event Action<string>? Log;

        public BatchTestRunner(IVisionService visionService)
        {
            _visionService = visionService;
        }

        public async Task<List<BatchImageResult>> RunAsync(BatchTestConfig cfg, CancellationToken ct = default)
        {
            return await Task.Run(() => Run(cfg, ct), ct);
        }

        public List<BatchImageResult> Run(BatchTestConfig cfg, CancellationToken ct = default)
        {
            var files = ListImages(cfg);
            Log?.Invoke($"발견: {files.Count}장");

            if (cfg.SaveFailureOverlays && !string.IsNullOrEmpty(cfg.FailureOverlayDir))
                Directory.CreateDirectory(cfg.FailureOverlayDir);

            var results = new List<BatchImageResult>();
            int idx = 0;
            foreach (var path in files)
            {
                if (ct.IsCancellationRequested) break;

                var item = new BatchImageResult { ImagePath = path };
                var sw = Stopwatch.StartNew();
                try
                {
                    using var img = Cv2.ImRead(path, ImreadModes.Color);
                    if (img.Empty())
                    {
                        item.Success = false;
                        item.FailureReason = "이미지 로드 실패";
                    }
                    else
                    {
                        _visionService.SetImage(img);
                        // ExecuteAllAsync는 내부적으로 Task.Run으로 동기 ExecuteAll 호출 — 우리는 이미 백그라운드.
                        var toolResults = _visionService.ExecuteAllAsync().GetAwaiter().GetResult();

                        // 도구 ID로 안전 매핑 — ExecuteAll은 topological sort 순서로 결과를 반환하므로
                        // 인덱스 기반 매핑(tools[i] ↔ toolResults[i])은 잘못된 도구에 결과가 붙는 버그를 만든다.
                        var resultMap = _visionService.LastExecutionResultsById;
                        var pairs = new List<(VisionToolBase tool, VisionResult res)>();
                        foreach (var tool in _visionService.Tools)
                        {
                            if (resultMap.TryGetValue(tool.Id, out var res))
                            {
                                item.ToolResults[tool.Id] = res;
                                pairs.Add((tool, res));
                            }
                        }

                        // 임계치 평가 (Recipe.Criteria 가 있을 때만)
                        var execSuccess = _visionService.LastRunSuccess && toolResults.All(r => r.Success);
                        if (cfg.Criteria != null)
                        {
                            var eval = PassFailEvaluator.EvaluateAll(pairs, cfg.Criteria);
                            item.ThresholdsPassed = eval.Pass;
                            item.ThresholdViolations = eval.Violations;
                        }

                        item.Success = execSuccess && item.ThresholdsPassed;
                        if (!item.Success)
                        {
                            if (!execSuccess)
                                item.FailureReason = toolResults.FirstOrDefault(r => !r.Success)?.Message ?? "도구 실행 실패";
                            else if (!item.ThresholdsPassed)
                                item.FailureReason = $"임계치 위반 {item.ThresholdViolations.Count}건: " +
                                    string.Join(" | ", item.ThresholdViolations.Take(2)) +
                                    (item.ThresholdViolations.Count > 2 ? " …" : "");
                        }

                        // 실패 오버레이 저장
                        if (!item.Success && cfg.SaveFailureOverlays && !string.IsNullOrEmpty(cfg.FailureOverlayDir))
                            SaveFailureOverlay(path, toolResults, cfg.FailureOverlayDir);
                    }
                }
                catch (Exception ex)
                {
                    item.Success = false;
                    item.FailureReason = $"예외: {ex.Message}";
                }
                sw.Stop();
                item.TotalMs = sw.Elapsed.TotalMilliseconds;

                results.Add(item);
                ImageDone?.Invoke(item);

                idx++;
                if (idx % Math.Max(1, files.Count / 50) == 0 || idx == files.Count)
                    Progress?.Invoke(idx, files.Count);
            }
            Progress?.Invoke(results.Count, files.Count);
            return results;
        }

        private List<string> ListImages(BatchTestConfig cfg)
        {
            if (!Directory.Exists(cfg.ImageFolder)) return new List<string>();
            var so = cfg.RecurseSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var set = new HashSet<string>(cfg.Extensions.Select(e => e.ToLowerInvariant()));
            return Directory.EnumerateFiles(cfg.ImageFolder, "*.*", so)
                .Where(p => set.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .OrderBy(p => p)
                .ToList();
        }

        private static void SaveFailureOverlay(string srcPath, List<VisionResult> results, string outDir)
        {
            // 마지막 도구의 OverlayImage 우선, 없으면 첫 OverlayImage
            Mat? overlay = null;
            for (int i = results.Count - 1; i >= 0; i--)
            {
                if (results[i].OverlayImage != null && !results[i].OverlayImage!.Empty())
                {
                    overlay = results[i].OverlayImage;
                    break;
                }
            }
            if (overlay == null) return;
            string fileName = Path.GetFileNameWithoutExtension(srcPath) + "_fail.png";
            string outPath = Path.Combine(outDir, fileName);
            try { Cv2.ImWrite(outPath, overlay); } catch { /* ignore */ }
        }
    }
}
