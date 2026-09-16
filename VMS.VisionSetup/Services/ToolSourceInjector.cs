using System;
using System.Collections.Generic;
using OpenCvSharp;
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

        /// <summary>
        /// Fixture(Coordinates) 변환 — 소스의 포즈(CenterX/CenterY[/Angle])만큼 타겟의
        /// ROI·SearchRegion 을 옮기고 <b>기울인다</b>.
        ///
        /// <para>양쪽 실행 엔진이 각자 같은 계산을 들고 있다가 갈라진 적이 있다
        /// (VMS 메인은 SearchRegion 시프트가 통째로 빠져 있었다 — AUTO RUN 에서만
        /// ShapeMatch/Color/OCV 의 탐색 영역이 안 따라갔다). 계산은 여기 한 곳에만 둔다.</para>
        ///
        /// <para><b>각도 규약</b> — 캔버스 각도는 화면 시계방향(+). ROI 중심을 기준점 둘레로
        /// +delta 회전시키는 기존 계산과 같은 부호를 ROIAngle 에도 쓴다. 부호를 뒤집으면
        /// 중심은 따라가는데 상자만 반대로 기울어 ROI 가 부품에서 떨어진다.</para>
        /// </summary>
        /// <param name="tool">변환을 받을 타겟 도구.</param>
        /// <param name="sourceData">소스 도구의 <see cref="VisionResult.Data"/>.</param>
        /// <returns>이 소스로 변환이 적용됐으면 true (좌표 키가 없으면 false).</returns>
        public static bool ApplyFixtureTransform(VisionToolBase tool, IDictionary<string, object> sourceData)
        {
            // NOTE: 과거 여기 있던 "center-based (non-fixture sources)" 폴백 분기는 제거됐다.
            // 초기 구현에서는 첫 분기가 TrainedCenterX/Y(FeatureMatch 전용 키)를 요구해
            // CenterX/CenterY만 가진 비-Fixture 소스(Blob 등)가 그 폴백으로 처리됐으나,
            // 32e75f0에서 첫 분기가 "첫 적용 시점 스냅샷(FixtureRef*)" 방식으로 일반화되며
            // CenterX/CenterY만 있어도 첫 분기가 처리하게 됐다 (사용자 ROI 미지정 시
            // FixtureBaseROI가 기준 좌표 중심으로 생성되어 폴백과 동일한 배치 결과).
            // → 조건이 첫 분기와 동일해져 도달 불가한 죽은 코드였다.
            if (!sourceData.TryGetValue("CenterX", out var cxObj) ||
                !sourceData.TryGetValue("CenterY", out var cyObj))
            {
                // 좌표가 없으면 BoundingRect 폴백 (CenterX/Y 를 안 내보내는 소스용)
                if (sourceData.TryGetValue("BoundingRect", out var rectObj) && rectObj is Rect boundingRect)
                {
                    tool.ROI = boundingRect;
                    tool.UseROI = true;
                    return true;
                }
                return false;
            }

            double foundX = Convert.ToDouble(cxObj);
            double foundY = Convert.ToDouble(cyObj);

            // 소스가 각도를 안 내보내면(Blob 등) 기울기는 손대지 않는다 —
            // 이동 전용으로 쓰던 기존 레시피가 갑자기 돌아가면 안 된다.
            bool hasAngle = sourceData.TryGetValue("Angle", out var angleObj);
            double currentAngle = hasAngle ? Convert.ToDouble(angleObj) : 0;

            // ── 최초 적용: 사용자가 세팅한 상태를 기준으로 붙잡아 둔다 ──
            if (!tool.HasFixtureBaseROI)
            {
                if (tool.UseROI && tool.ROI.Width > 0 && tool.ROI.Height > 0)
                {
                    tool.FixtureBaseROI = tool.ROI;
                }
                else
                {
                    // 사용자 ROI 가 없으면 기준 좌표 중심의 기본 ROI 를 만들어 패턴을 따라가게 한다
                    int defaultW = tool.ROI.Width > 0 ? tool.ROI.Width : 200;
                    int defaultH = tool.ROI.Height > 0 ? tool.ROI.Height : 200;
                    tool.FixtureBaseROI = new Rect(
                        (int)(foundX - defaultW / 2.0),
                        (int)(foundY - defaultH / 2.0),
                        defaultW, defaultH);
                }

                tool.HasFixtureBaseROI = true;
                tool.FixtureRefX = foundX;
                tool.FixtureRefY = foundY;
                tool.FixtureRefAngle = currentAngle;
                // 라이브 캔버스 도형이 있으면 그쪽이 진짜 기울기다 (GetAlignedROIImage 와 같은 우선순위)
                tool.FixtureBaseROIAngle = tool.AssociatedROIShape is RectangleAffineROI liveROI
                    ? liveROI.Angle : tool.ROIAngle;
            }

            double refX = tool.FixtureRefX;
            double refY = tool.FixtureRefY;
            double deltaAngle = currentAngle - tool.FixtureRefAngle;
            bool rotated = hasAngle && Math.Abs(deltaAngle) > 0.01;

            double baseCX = tool.FixtureBaseROI.X + tool.FixtureBaseROI.Width / 2.0;
            double baseCY = tool.FixtureBaseROI.Y + tool.FixtureBaseROI.Height / 2.0;
            var (newCX, newCY) = MapPoint(baseCX, baseCY, refX, refY, foundX, foundY, deltaAngle, rotated);

            int w = tool.FixtureBaseROI.Width > 0 ? tool.FixtureBaseROI.Width : 100;
            int h = tool.FixtureBaseROI.Height > 0 ? tool.FixtureBaseROI.Height : 100;
            tool.ROI = new Rect((int)(newCX - w / 2.0), (int)(newCY - h / 2.0), w, h);
            tool.UseROI = true;

            if (hasAngle)
            {
                // 상자도 부품과 같이 기운다 — 이게 빠져 있어서 "각도 전달이 안 된다" 였다.
                // 회전 중심을 함께 실어야 GetAlignedROIImage/Blob/Caliper 가 같은 축으로 편다.
                tool.ROIAngle = tool.FixtureBaseROIAngle + deltaAngle;
                tool.ROICenterX = newCX;
                tool.ROICenterY = newCY;
                // 이 뒤로 실행은 캔버스 도형이 아니라 이 각도를 본다 (EffectiveROIAngle).
                tool.HasFixtureAngle = true;

                // 캔버스 도형(AssociatedROIShape)은 여기서 건드리지 않는다 — ROIShape 는
                // UI 에 바인딩된 ObservableObject 인데 이 코드는 AUTO RUN 백그라운드
                // 스레드에서도 돈다. 화면 반영은 UI 계층이 맡는다(ROI 위치도 같은 규약).
            }

            // SearchRegion(Execute 용)도 같은 변환 — ShapeMatch/Color*/OCV 처럼
            // Training Region 과 별도 Search Region 을 갖는 도구에 적용.
            if (tool is ISearchRegionTool srt && srt.UseSearchRegion
                && srt.SearchRegion.Width > 0 && srt.SearchRegion.Height > 0)
            {
                if (!tool.HasFixtureBaseSearchRegion)
                {
                    tool.FixtureBaseSearchRegion = srt.SearchRegion;
                    tool.HasFixtureBaseSearchRegion = true;
                }

                double srBaseCX = tool.FixtureBaseSearchRegion.X + tool.FixtureBaseSearchRegion.Width / 2.0;
                double srBaseCY = tool.FixtureBaseSearchRegion.Y + tool.FixtureBaseSearchRegion.Height / 2.0;
                var (newSrCX, newSrCY) =
                    MapPoint(srBaseCX, srBaseCY, refX, refY, foundX, foundY, deltaAngle, rotated);

                int sw = tool.FixtureBaseSearchRegion.Width;
                int sh = tool.FixtureBaseSearchRegion.Height;
                srt.SearchRegion = new Rect(
                    (int)(newSrCX - sw / 2.0), (int)(newSrCY - sh / 2.0), sw, sh);
            }

            return true;
        }

        /// <summary>
        /// 기준점(refX,refY) 기준 상대 위치를 delta 만큼 회전시키고 찾은 위치로 옮긴다.
        /// 회전이 없으면 평행 이동만.
        /// </summary>
        private static (double X, double Y) MapPoint(
            double baseX, double baseY,
            double refX, double refY, double foundX, double foundY,
            double deltaAngle, bool rotated)
        {
            if (!rotated)
                return (baseX + (foundX - refX), baseY + (foundY - refY));

            double relX = baseX - refX;
            double relY = baseY - refY;
            double rad = deltaAngle * Math.PI / 180.0;
            return (foundX + relX * Math.Cos(rad) - relY * Math.Sin(rad),
                    foundY + relX * Math.Sin(rad) + relY * Math.Cos(rad));
        }
    }
}
