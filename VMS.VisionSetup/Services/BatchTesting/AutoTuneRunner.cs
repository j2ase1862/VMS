using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    /// <summary>한 파라미터의 sweep 정의.</summary>
    public class AutoTuneSweep
    {
        public VisionToolBase Tool { get; set; } = null!;
        public TunableParameterDescriptor Parameter { get; set; } = null!;
        public List<object> Values { get; set; } = new();
        public string Label => $"{Tool.Name}.{Parameter.Name}";
    }

    /// <summary>한 조합(= 모든 sweep에서 값 하나씩 뽑은 것)의 batch 실행 결과.</summary>
    public class AutoTuneCombinationResult
    {
        /// <summary>이 조합의 (sweep label → 적용된 값) — Apply Best 에서 다시 setter 호출용.</summary>
        public List<(AutoTuneSweep Sweep, object Value)> Bindings { get; set; } = new();

        public int Total { get; set; }
        public int Pass { get; set; }
        public int Fail { get; set; }
        public double AvgMs { get; set; }
        public double PassRate => Total > 0 ? (double)Pass / Total : 0;
        public string PassRateText => $"{PassRate * 100:F1}%";
        public bool IsBest { get; set; }

        /// <summary>DataGrid 표시용 — "Tool.Param=v, Tool2.Param2=v2" 형식.</summary>
        public string CombinationText => string.Join(", ",
            Bindings.Select(b => $"{b.Sweep.Label}={b.Sweep.Parameter.FormatValue(b.Value)}"));
    }

    public class AutoTuneConfig
    {
        public string ImageFolder { get; set; } = string.Empty;
        public bool RecurseSubfolders { get; set; }
        public string[] Extensions { get; set; } = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

        public List<AutoTuneSweep> Sweeps { get; set; } = new();

        /// <summary>null이면 Grid (전체 cartesian product). 값이 있으면 그 횟수만큼 random sampling.</summary>
        public int? MaxRandomSamples { get; set; }

        /// <summary>Random seed (재현 가능성 위해, null이면 시간 기반).</summary>
        public int? RandomSeed { get; set; }

        public PassFailCriteria? Criteria { get; set; }
    }

    public class AutoTuneRunner
    {
        private readonly IVisionService _visionService;

        public event Action<int, int>? Progress;
        public event Action<AutoTuneCombinationResult>? CombinationDone;
        public event Action<string>? Log;

        public AutoTuneRunner(IVisionService visionService)
        {
            _visionService = visionService;
        }

        public async Task<List<AutoTuneCombinationResult>> RunAsync(AutoTuneConfig cfg, CancellationToken ct = default)
        {
            return await Task.Run(() => Run(cfg, ct), ct);
        }

        public List<AutoTuneCombinationResult> Run(AutoTuneConfig cfg, CancellationToken ct = default)
        {
            var results = new List<AutoTuneCombinationResult>();
            if (cfg.Sweeps.Count == 0) return results;

            // 각 sweep의 원래 값 백업 (끝에 복원)
            var originalValues = cfg.Sweeps
                .Select(s => (s, s.Parameter.GetValue(s.Tool)))
                .ToList();

            try
            {
                var combinations = BuildCombinations(cfg.Sweeps, cfg.MaxRandomSamples, cfg.RandomSeed);
                int total = combinations.Count;
                int idx = 0;

                foreach (var combo in combinations)
                {
                    if (ct.IsCancellationRequested) break;

                    // 적용
                    foreach (var (sweep, value) in combo)
                        sweep.Parameter.SetValue(sweep.Tool, value);

                    Log?.Invoke($"[{idx + 1}/{total}] " + string.Join(", ",
                        combo.Select(p => $"{p.Sweep.Label}={p.Sweep.Parameter.FormatValue(p.Value)}")));

                    // Batch 실행
                    var batchCfg = new BatchTestConfig
                    {
                        ImageFolder = cfg.ImageFolder,
                        RecurseSubfolders = cfg.RecurseSubfolders,
                        Extensions = cfg.Extensions,
                        OutputCsvPath = "",
                        SaveFailureOverlays = false,
                        Criteria = cfg.Criteria
                    };
                    var runner = new BatchTestRunner(_visionService);
                    var batchResults = runner.Run(batchCfg, ct);

                    var comb = new AutoTuneCombinationResult
                    {
                        Bindings = combo,
                        Total = batchResults.Count,
                        Pass = batchResults.Count(r => r.Success),
                        Fail = batchResults.Count(r => !r.Success),
                        AvgMs = batchResults.Count > 0 ? batchResults.Average(r => r.TotalMs) : 0
                    };
                    results.Add(comb);
                    CombinationDone?.Invoke(comb);

                    idx++;
                    Progress?.Invoke(idx, total);
                }

                var best = results
                    .OrderByDescending(r => r.PassRate)
                    .ThenBy(r => r.AvgMs)
                    .FirstOrDefault();
                if (best != null) best.IsBest = true;
            }
            finally
            {
                // 원래 값 복원
                foreach (var (sweep, original) in originalValues)
                    sweep.Parameter.SetValue(sweep.Tool, original);
            }

            return results;
        }

        /// <summary>Grid (cartesian product) 또는 Random sampling으로 조합 리스트 생성.</summary>
        public static List<List<(AutoTuneSweep Sweep, object Value)>> BuildCombinations(
            IList<AutoTuneSweep> sweeps, int? maxRandomSamples, int? randomSeed)
        {
            var result = new List<List<(AutoTuneSweep, object)>>();
            if (sweeps.Count == 0) return result;

            // 전체 grid 크기 계산
            long gridSize = 1;
            foreach (var s in sweeps)
            {
                if (s.Values.Count == 0) return result;
                gridSize *= s.Values.Count;
                if (gridSize > 100_000) { gridSize = 100_000; break; }  // overflow guard
            }

            if (maxRandomSamples.HasValue && maxRandomSamples.Value < gridSize)
            {
                // Random sampling — 중복 허용 (samplingSize가 작을 때 더 자연스러움)
                var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
                var seen = new HashSet<string>();
                int target = maxRandomSamples.Value;
                int safety = target * 5;
                while (result.Count < target && safety-- > 0)
                {
                    var combo = new List<(AutoTuneSweep, object)>();
                    foreach (var s in sweeps)
                        combo.Add((s, s.Values[rng.Next(s.Values.Count)]));
                    var key = string.Join("|", combo.Select(c => $"{c.Item1.Label}={c.Item2}"));
                    if (seen.Add(key)) result.Add(combo);
                }
            }
            else
            {
                // Grid — cartesian product
                IEnumerable<List<(AutoTuneSweep, object)>> acc = new List<List<(AutoTuneSweep, object)>>
                    { new List<(AutoTuneSweep, object)>() };
                foreach (var s in sweeps)
                {
                    var local = s;
                    acc = acc.SelectMany(a => local.Values.Select(v =>
                    {
                        var copy = new List<(AutoTuneSweep, object)>(a) { (local, v) };
                        return copy;
                    }));
                }
                result.AddRange(acc);
            }

            return result;
        }

        public static List<object> BuildNumericRange(Type type, double from, double to, double step)
        {
            var list = new List<object>();
            if (step <= 0 || from > to) return list;
            for (double v = from; v <= to + 1e-9; v += step)
            {
                if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                    list.Add(Convert.ChangeType(Math.Round(v), type, CultureInfo.InvariantCulture));
                else
                    list.Add(Convert.ChangeType(v, type, CultureInfo.InvariantCulture));
                if (list.Count > 200) break;
            }
            return list;
        }
    }
}
