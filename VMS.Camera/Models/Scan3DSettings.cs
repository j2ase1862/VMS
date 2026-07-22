namespace VMS.Camera.Models
{
    /// <summary>
    /// 3D 스캔 포인트 클라우드 후처리 프리셋 — Mech-Eye Viewer 의
    /// 포인트 클라우드 후처리(Off/Weak/Normal/Strong) UX 를 따른다.
    /// CameraDefault 는 카메라의 현재 설정을 건드리지 않음 (기존 동작 유지).
    /// </summary>
    public enum PointCloudPostProcessPreset
    {
        CameraDefault,
        Off,
        Weak,
        Normal,
        Strong
    }

    /// <summary>
    /// 스텝별 3D 스캔 카메라 파라미터 — 촬영 시점에 카메라(SDK)에 적용되어
    /// 2D 이미지 / depth map / 점군이 모두 일관되게 정제된 데이터를 받는다.
    /// (레시피 처리용 VoxelGrid 다운샘플은 PointCloudFilterTool 담당 — 역할 분리)
    /// record: 값 동등성으로 동일 설정 재적용 스킵(캐시)에 사용.
    /// </summary>
    public sealed record Scan3DSettings
    {
        /// <summary>표면 스무딩/노이즈·이상점 제거 강도 (촬영 시 카메라 내부 적용)</summary>
        public PointCloudPostProcessPreset PostProcessPreset { get; init; }
            = PointCloudPostProcessPreset.CameraDefault;

        /// <summary>뎁스 범위 제한 사용 여부 — false 면 카메라 현재 범위 유지</summary>
        public bool UseDepthRange { get; init; }

        /// <summary>뎁스 하한 (mm, 카메라 기준 거리)</summary>
        public double DepthRangeMinMm { get; init; }

        /// <summary>뎁스 상한 (mm, 카메라 기준 거리)</summary>
        public double DepthRangeMaxMm { get; init; } = 3000;
    }
}
