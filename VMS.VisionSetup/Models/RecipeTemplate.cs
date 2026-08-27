using System;
using System.Collections.Generic;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 예제 템플릿의 툴 한 개 정의. ToolType 은 VisionService.CreateTool 의 키와 동일.
    /// Configure 로 템플릿별 파라미터 프리셋을 적용한다 (예: YoloSeg OutputMaskImage 켬).
    /// </summary>
    public class TemplateToolSpec
    {
        public string ToolType { get; init; } = string.Empty;

        /// <summary>워크스페이스에 표시할 이름 (null 이면 툴 기본 이름).</summary>
        public string? DisplayName { get; init; }

        public Action<VisionToolBase>? Configure { get; init; }
    }

    /// <summary>
    /// 예제 템플릿의 툴 간 연결 정의 — Tools 리스트의 인덱스로 참조.
    /// </summary>
    public class TemplateConnectionSpec
    {
        public int SourceIndex { get; init; }
        public int TargetIndex { get; init; }
        public ConnectionType Type { get; init; }
    }

    /// <summary>
    /// 다중 스텝 템플릿의 스텝 한 개 정의 — 스텝별 툴 체인 + 스텝 내부 연결.
    /// </summary>
    public class TemplateStepSpec
    {
        /// <summary>스텝 설명 (다이어그램·상태 메시지용, 예: "A 포인트 촬영").</summary>
        public string Title { get; init; } = string.Empty;

        public List<TemplateToolSpec> Tools { get; init; } = new();
        public List<TemplateConnectionSpec> Connections { get; init; } = new();
    }

    /// <summary>
    /// 레시피 예제 템플릿 — 자주 쓰는 툴 체인(툴 + 파라미터 + 연결)의 사전 정의.
    /// 오퍼레이터가 갤러리에서 선택하면 현재 스텝의 워크스페이스에 체인이 생성된다.
    /// Steps 가 비어있지 않으면 다중 스텝 템플릿 — 현재 워크스페이스 대신
    /// 레시피에 새 스텝들을 생성한다 (2-스텝 얼라인 등).
    /// 카탈로그는 RecipeTemplateCatalog 참조.
    /// </summary>
    public class RecipeTemplate
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;

        /// <summary>갤러리 탭 분류: "3D", "Deep Learning", "2D 측정", "식별" 등.</summary>
        public string Category { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        /// <summary>전제조건 배지 (예: "3D 카메라 필요", "ONNX 모델 필요"). 없으면 빈 배열.</summary>
        public string[] Prerequisites { get; init; } = Array.Empty<string>();

        public List<TemplateToolSpec> Tools { get; init; } = new();
        public List<TemplateConnectionSpec> Connections { get; init; } = new();

        /// <summary>다중 스텝 템플릿 정의 — 비어있지 않으면 Tools/Connections 대신 사용.</summary>
        public List<TemplateStepSpec> Steps { get; init; } = new();

        /// <summary>
        /// 다중 스텝 생성 직후(직렬화 전) 스텝 간 참조를 배선하는 훅 —
        /// 예: MultiStepAlign 의 소스 스텝/툴 Id 를 생성된 실제 Id 로 설정.
        /// 인자: (생성된 스텝들, 스텝별 툴 인스턴스들)
        /// </summary>
        public Action<IReadOnlyList<InspectionStep>, IReadOnlyList<IReadOnlyList<VisionToolBase>>>? ConfigureAcrossSteps { get; init; }

        /// <summary>체인 다이어그램 이미지 (어셈블리 리소스, 빌드 시 생성·동봉).</summary>
        public string DiagramUri => $"pack://application:,,,/Resources/Templates/{Id}.png";
    }
}
