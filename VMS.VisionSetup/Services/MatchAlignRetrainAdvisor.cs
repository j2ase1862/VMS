using System.Collections.Generic;
using System.Linq;
using System.Text;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.PatternMatching;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// Feature Match 재학습이 연결된 Match Align 의 기준(Origin) 포즈에 미치는 영향을 사용자에게 알린다 (2026-09-04).
    /// 재학습은 학습 원점을 바꾸거나(현재 이미지 기준) 템플릿만 바꾸므로(기준 유지), 얼라인 기준이
    /// 조용히 이동하거나 수동 등록 기준이 낡은 템플릿 기준으로 남는 사고를 막기 위한 안내.
    /// </summary>
    public static class MatchAlignRetrainAdvisor
    {
        /// <summary>
        /// 상태 표시줄에 덧붙일 짧은 안내(연결 없으면 null)와, 수동 기준을 쓰는 Match Align 이 있을 때
        /// 경고 창으로 띄울 상세 문구(없으면 null).
        /// </summary>
        /// <param name="connections">(소스 도구 Id, 타깃 도구 Id, 연결 종류) — VisionToolBase.Id 기준.</param>
        public static (string? StatusNote, string? WarningDialog) Advise(
            FeatureMatchTool trained, bool originKept,
            IEnumerable<(string SourceToolId, string TargetToolId, ConnectionType Type)> connections,
            IEnumerable<VisionToolBase> tools)
        {
            var toolList = tools.ToList();
            var aligns = connections
                .Where(c => c.SourceToolId == trained.Id && c.Type == ConnectionType.Result)
                .Select(c => toolList.FirstOrDefault(t => t.Id == c.TargetToolId) as MatchAlignTool)
                .Where(t => t != null)
                .Select(t => t!)
                .Distinct()
                .ToList();

            if (aligns.Count == 0)
                return (null, null);

            var trainedRef = aligns.Where(a => a.UseTrainedReference).Select(a => a.Name).ToList();
            var manualRef = aligns.Where(a => !a.UseTrainedReference).Select(a => a.Name).ToList();

            var status = new StringBuilder();
            if (trainedRef.Count > 0)
            {
                status.Append(originKept
                    ? $"연결된 Match Align({string.Join(", ", trainedRef)}) 기준 원점은 기준 이미지 그대로 유지됨"
                    : $"연결된 Match Align({string.Join(", ", trainedRef)}) 기준 원점이 현재 이미지 학습 위치로 갱신됨");
            }

            string? dialog = null;
            if (manualRef.Count > 0)
            {
                if (status.Length > 0) status.Append(" / ");
                status.Append($"수동 기준 Match Align({string.Join(", ", manualRef)})은 [현재 매칭을 기준으로 등록] 재등록 필요");

                dialog =
                    $"'{trained.Name}' 을(를) 재학습했습니다.\n\n" +
                    $"연결된 Match Align '{string.Join("', '", manualRef)}' 은(는) 수동 기준(Ref X/Y/θ)을 사용 중입니다.\n" +
                    "수동 기준은 이전 템플릿으로 매칭한 포즈를 기록한 값이라, 재학습으로 템플릿(학습 영역·각도)이 바뀌면 " +
                    "기준이 더 이상 현재 템플릿과 맞지 않을 수 있습니다.\n\n" +
                    "기준 부품을 놓고 Run 한 뒤 Match Align 설정의 [현재 매칭을 기준으로 등록]으로 원점을 다시 등록하세요.";
            }

            return (status.Length > 0 ? status.ToString() : null, dialog);
        }
    }
}
