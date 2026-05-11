using System;

namespace VMS.VisionSetup.Attributes
{
    /// <summary>
    /// LLM 기반 파라미터 자동 추정의 어려움 계층.
    /// </summary>
    public enum TuningTier
    {
        /// <summary>
        /// 의미 기반 매핑. 사용자 의도(텍스트)만으로 결정 가능.
        /// 예: ThresholdType.Binary vs BinaryInv, SegmentationPolarity.LightOnDark.
        /// </summary>
        Semantic = 0,

        /// <summary>
        /// 도메인 상식 기반 추정. 합리적 기본값/범위가 존재하며 사용자 의도로 좁힐 수 있음.
        /// 예: MinArea(작은/큰 결함), KernelSize, MaxEdges.
        /// </summary>
        DomainCommon = 1,

        /// <summary>
        /// 이미지 통계 의존. 실제 이미지 분석 없이는 정확한 값 추정 불가.
        /// 예: ThresholdValue(픽셀 분포), CannyThreshold(엣지 분포).
        /// </summary>
        ImageDependent = 2,
    }

    /// <summary>
    /// 비전 도구 프로퍼티 중 SLM이 자동 튜닝 대상으로 인식해야 하는 항목을 표시.
    /// 부착되지 않은 프로퍼티는 SLM Schema에서 제외됨.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class TunableParamAttribute : Attribute
    {
        /// <summary>
        /// 프로퍼티의 역할/의미를 LLM이 이해할 수 있는 한 문장 설명.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 추정 난이도 계층.
        /// </summary>
        public TuningTier Tier { get; set; } = TuningTier.Semantic;

        /// <summary>
        /// 추천 최소값 힌트. double.NaN이면 미지정.
        /// </summary>
        public double Min { get; set; } = double.NaN;

        /// <summary>
        /// 추천 최대값 힌트. double.NaN이면 미지정.
        /// </summary>
        public double Max { get; set; } = double.NaN;

        /// <summary>
        /// 합리적 기본 추정값 힌트(문자열로 표현, enum/숫자/bool 모두 허용).
        /// LLM이 시작점으로 참고함.
        /// </summary>
        public string? DefaultHint { get; set; }

        /// <summary>
        /// 이 프로퍼티가 활성화되려면 같은 도구의 어떤 프로퍼티가 true여야 하는지 명시.
        /// 예: BlockSize는 UseAdaptive=true일 때만 의미가 있음.
        /// null이면 조건 없음.
        /// </summary>
        public string? DependsOn { get; set; }
    }
}
