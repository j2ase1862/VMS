using System;
using System.Collections.Generic;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 스텝 간 포즈 공유 저장소 — 여러 스텝(카메라/로봇 이동)에서 얻은 매칭 포즈를
    /// 이후 스텝의 MultiStepAlignTool 이 합산하기 위한 통로.
    ///
    /// 현재 구조는 툴 연결이 스텝 내부에서만 유효하므로, 실행 엔진(VisionService /
    /// VMS 메인 InspectionService)이 각 툴 실행 후 포즈(CenterX/Y 보유 결과)를 여기에
    /// 기록하고, MultiStepAlignTool 이 (스텝 Id, 툴 Id) 키로 꺼내 쓴다.
    ///
    /// 사이클 규칙:
    /// - VMS 메인 AUTO RUN 은 사이클 시작마다 BeginCycle() 을 호출 — 이전 사이클의
    ///   낡은 포즈가 이번 사이클 계산에 섞이지 않는다 (스텝 A 매칭 실패 시 명확히 실패).
    /// - VisionSetup 편집/수동 실행은 BeginCycle 을 호출하지 않아 마지막 값이 유지 —
    ///   스텝 A Run → 스텝 B Run 순서로 자유롭게 튜닝할 수 있다.
    /// </summary>
    public static class StepPoseStore
    {
        public sealed class PoseEntry
        {
            public string StepId { get; init; } = string.Empty;
            public string ToolId { get; init; } = string.Empty;
            public string ToolName { get; init; } = string.Empty;
            public double CenterX { get; init; }
            public double CenterY { get; init; }
            public double? XMm { get; init; }
            public double? YMm { get; init; }
            public long CycleId { get; init; }
            public DateTime RecordedAt { get; init; }
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<string, PoseEntry> _entries = new();
        private static long _currentCycleId;

        public static long CurrentCycleId
        {
            get { lock (_lock) return _currentCycleId; }
        }

        private static string Key(string stepId, string toolId) => stepId + "/" + toolId;

        /// <summary>AUTO RUN 사이클 시작 — 이후 기록만 이번 사이클로 인정된다.</summary>
        public static void BeginCycle()
        {
            lock (_lock) _currentCycleId++;
        }

        /// <summary>테스트 전용 — 저장소·사이클 초기화.</summary>
        internal static void ResetForTests()
        {
            lock (_lock)
            {
                _entries.Clear();
                _currentCycleId = 0;
            }
        }

        /// <summary>
        /// 툴 실행 결과의 포즈를 기록. CenterX/CenterY 가 없거나 실패 결과면 무시.
        /// mm 좌표는 기록 시점의 캘리브레이션(스텝 Resolution 폴백 포함)으로 변환해 함께 저장 —
        /// 스텝마다 캘리브레이션이 다를 수 있으므로 소비 시점 변환은 부정확하다.
        /// </summary>
        public static void Record(string? stepId, VisionToolBase tool, VisionResult result)
        {
            if (string.IsNullOrEmpty(stepId) || result == null || !result.Success) return;
            if (!TryGet(result, "CenterX", out var cx) || !TryGet(result, "CenterY", out var cy)) return;

            double? xMm = null, yMm = null;
            var cal = VisionService.Instance.EffectiveCalibration;
            if (cal != null)
            {
                var (mx, my) = cal.PixelToMm(cx, cy);
                xMm = mx; yMm = my;
            }

            lock (_lock)
            {
                _entries[Key(stepId, tool.Id)] = new PoseEntry
                {
                    StepId = stepId,
                    ToolId = tool.Id,
                    ToolName = tool.Name,
                    CenterX = cx,
                    CenterY = cy,
                    XMm = xMm,
                    YMm = yMm,
                    CycleId = _currentCycleId,
                    RecordedAt = DateTime.Now
                };
            }
        }

        /// <summary>기록된 포즈 조회. requireCurrentCycle 이면 이번 사이클 기록만 인정.</summary>
        public static PoseEntry? TryGet(string stepId, string toolId, bool requireCurrentCycle)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(Key(stepId, toolId), out var entry)) return null;
                if (requireCurrentCycle && entry.CycleId != _currentCycleId) return null;
                return entry;
            }
        }

        private static bool TryGet(VisionResult src, string key, out double value)
        {
            if (src.Data.TryGetValue(key, out var obj))
            {
                try
                {
                    value = Convert.ToDouble(obj);
                    return true;
                }
                catch { }
            }
            value = 0;
            return false;
        }
    }
}
