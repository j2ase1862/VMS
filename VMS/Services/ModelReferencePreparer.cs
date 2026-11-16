using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VMS.Core.DeepLearning;
using VMS.Core.Services;
using VMS.Interfaces;

namespace VMS.Services
{
    /// <summary>
    /// VMS 메인이 레시피를 열 때 <c>model://…</c> 참조 모델을 받아 둔다 (VM-40).
    ///
    /// <para>
    /// 종전에는 이 준비를 VisionSetup 의 RecipeService 만 했다. 참조 → 파일 대응은 프로세스 메모리에만
    /// 있으므로, VisionSetup 이 같은 PC 에 받아 둔 파일도 VMS 는 찾지 못했고, 참조를 쓰는 도구는
    /// VMS 수동 검사 · 자동 운전에서 늘 "아직 내려받지 못했습니다" 로 불합격했다.
    /// </para>
    /// <para>
    /// 캐시 정리(용량 상한)도 여기서 한다 — 준비가 모두 성공해 쓰는 파일을 다 알 때만.
    /// 종전처럼 앱 시작 때 정리하면 아직 아무 참조도 풀지 않아 보호 목록이 비어 있었다.
    /// </para>
    /// </summary>
    public sealed class ModelReferencePreparer : IModelReferencePreparer
    {
        private const string LogSource = "Model";

        private readonly ISystemLogService? _log;
        private readonly long _cacheMaxBytes;
        private readonly Func<ModelReferenceResolver?> _resolver;

        public ModelReferencePreparer(ISystemLogService? log, long cacheMaxBytes,
            Func<ModelReferenceResolver?>? resolver = null)
        {
            _log = log;
            _cacheMaxBytes = cacheMaxBytes;
            _resolver = resolver ?? (() => ModelReferenceResolver.Current);
        }

        /// <summary>레시피의 모든 도구 ModelPath 중 <c>model://</c> 참조만, 중복 없이.</summary>
        public static IReadOnlyList<string> CollectReferences(Recipe recipe)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in recipe.Steps)
            {
                foreach (var tool in step.Tools)
                {
                    if (!tool.Parameters.TryGetValue("ModelPath", out var raw)) continue;
                    var value = raw switch
                    {
                        string s => s,
                        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                        _ => null
                    };
                    if (string.IsNullOrWhiteSpace(value) || !ModelReference.IsReference(value)) continue;
                    if (seen.Add(value!)) result.Add(value!);
                }
            }
            return result;
        }

        public async Task PrepareForRecipeAsync(Recipe recipe, bool onlyMissing = false)
        {
            try
            {
                var references = CollectReferences(recipe);
                var resolver = _resolver();
                if (references.Count == 0 || resolver == null)
                {
                    if (references.Count > 0)
                        Log($"레시피 '{recipe.Name}' 의 모델 참조 {references.Count}개를 준비할 수 없습니다 — " +
                            "모델 저장소 설정을 읽지 못했습니다. 이 모델을 쓰는 도구는 불합격 처리됩니다.", LogLevel.Warning);
                    return;
                }

                if (onlyMissing)
                    references = references.Where(r => resolver.ToLocalPath(r) == null).ToList();
                if (references.Count == 0) return;

                // 네트워크 대기로 UI 스레드를 잡지 않는다
                var (ok, failures) = await Task.Run(() => PrepareAllAsync(resolver, references)).ConfigureAwait(false);

                foreach (var (reference, reason) in failures)
                {
                    Log($"모델 참조 준비 실패: {reference} — {reason} " +
                        "이 모델을 쓰는 도구는 받을 때까지 불합격 처리됩니다.", LogLevel.Warning);
                }
                if (ok > 0)
                    Log($"레시피 '{recipe.Name}' 모델 참조 {ok}개 준비 완료", LogLevel.Info);

                if (failures.Count == 0)
                    TrimCache(resolver);
                else
                    Debug.WriteLine("[ModelPrep] 준비 실패가 있어 캐시 정리를 건너뜁니다 — 쓰는 파일을 다 알지 못한다.");
            }
            catch (Exception ex)
            {
                // 레시피 열기 · 운전을 멈출 이유가 아니다 — 남기고 끝낸다
                Log($"모델 참조 준비 중 오류: {ex.Message}", LogLevel.Warning);
            }
        }

        private static async Task<(int Ok, List<(string Reference, string Reason)> Failures)> PrepareAllAsync(
            ModelReferenceResolver resolver, IReadOnlyList<string> references)
        {
            int ok = 0;
            var failures = new List<(string, string)>();
            foreach (var reference in references)
            {
                try
                {
                    await resolver.PrepareAsync(reference).ConfigureAwait(false);
                    ok++;
                }
                catch (Exception ex)
                {
                    failures.Add((reference, ex.Message));
                }
            }
            return (ok, failures);
        }

        private void TrimCache(ModelReferenceResolver resolver)
        {
            if (_cacheMaxBytes <= 0) return;
            try
            {
                // 이 프로세스가 푼 참조의 파일은 모두 보호한다 (지금 레시피 + 앞서 연 레시피)
                var trimmed = resolver.Cache.Trim(_cacheMaxBytes, resolver.InUsePaths());
                if (trimmed > 0)
                    Log($"모델 캐시 정리: {trimmed}개 삭제", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModelPrep] 캐시 정리 실패: {ex.Message}");
            }
        }

        private void Log(string message, LogLevel level)
        {
            Debug.WriteLine($"[ModelPrep] {message}");
            _log?.Log(message, level, LogSource);
        }
    }
}
