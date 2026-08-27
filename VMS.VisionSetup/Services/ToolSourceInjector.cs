using System.Collections.Generic;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 실행 직전 소스 결과 주입 — VisionSetup(VisionService)과 VMS 메인(InspectionService)
    /// 두 실행 엔진이 공유하는 단일 정의처.
    ///
    /// 배경: VMS 메인 InspectionService 는 자체 실행 루프를 갖고 있어 VisionService 에만
    /// 주입을 넣으면 AUTO RUN 에서 소스가 비는 채로 실행된다 (GeometryTool 이 실제로
    /// 이 상태였음 — 2026-08-27 발견). 새 소스 소비 도구는 반드시 여기에 추가하고
    /// 양쪽 엔진에서 호출할 것.
    /// </summary>
    public static class ToolSourceInjector
    {
        /// <summary>Result 연결 소스 하나 — 연결 생성 순서 유지가 중요 (2점 모드의 포인트 순서).</summary>
        public readonly record struct ResultSource(
            string SourceId, VisionResult Result, string Name, string ToolType);

        /// <summary>
        /// MatchAlignTool 소스 주입 — 포즈(CenterX/Y)를 가진 소스를 연결 순서대로
        /// 1번/2번 포인트로 채운다 (1점 모드는 1번만 사용).
        /// </summary>
        public static void InjectMatchAlign(MatchAlignTool tool, IEnumerable<ResultSource> resultSources)
        {
            tool.SourceMatchResult = null;
            tool.SourceToolName = string.Empty;
            tool.SourceMatchResult2 = null;
            tool.SourceToolName2 = string.Empty;

            foreach (var src in resultSources)
            {
                if (!src.Result.Data.ContainsKey("CenterX") ||
                    !src.Result.Data.ContainsKey("CenterY"))
                    continue;

                if (tool.SourceMatchResult == null)
                {
                    tool.SourceMatchResult = src.Result;
                    tool.SourceToolName = src.Name;
                }
                else if (tool.SourceMatchResult2 == null)
                {
                    tool.SourceMatchResult2 = src.Result;
                    tool.SourceToolName2 = src.Name;
                    return;
                }
            }
        }

        /// <summary>
        /// GeometryTool 소스 주입 — 소스 도구 타입별 기하 요소(점/선/원) 변환.
        /// </summary>
        public static void InjectGeometry(GeometryTool tool, IEnumerable<ResultSource> resultSources)
        {
            tool.SourceGeometries.Clear();
            foreach (var src in resultSources)
            {
                var geo = GeometryTool.ExtractGeometry(src.SourceId, src.Name, src.ToolType, src.Result);
                if (geo != null)
                    tool.SourceGeometries.Add(geo);
            }
        }
    }
}
