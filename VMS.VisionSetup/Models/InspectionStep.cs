using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using VMS.Camera.Models;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 검사 스텝 (촬영 포즈/조명 설정 단위)
    /// </summary>
    public class InspectionStep : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>
        /// 스텝 고유 ID (GUID)
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        /// <summary>
        /// 스텝 이름 — 저장값이 아니라 파생값. 현재 PC 카메라 레지스트리 기준으로
        /// StepNaming.RecomputeNames 가 로드/변경 시마다 재계산한다 (미등록 카메라 → "?-n").
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        private int _sequence;
        /// <summary>
        /// 실행 순서
        /// </summary>
        public int Sequence
        {
            get => _sequence;
            set => SetProperty(ref _sequence, value);
        }

        private string _cameraId = string.Empty;
        /// <summary>
        /// 사용할 카메라 ID (CameraInfo 참조)
        /// </summary>
        public string CameraId
        {
            get => _cameraId;
            set => SetProperty(ref _cameraId, value);
        }

        private double _resolution = 0.05;
        /// <summary>
        /// 해상도 (mm/pixel)
        /// </summary>
        public double Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, Math.Max(0, value));
        }

        #region Acquisition Settings

        private bool _use2DCameraDefault = true;
        /// <summary>
        /// 2D 노출/게인 카메라 설정 유지 — true(기본)면 Grab/Live 때 카메라의 현재
        /// 노출/게인을 건드리지 않는다 (Mech-Eye Viewer 등에서 튜닝한 값 보존).
        /// false 면 아래 Exposure/Gain 을 촬영 전에 카메라에 적용한다.
        /// 구버전 레시피(필드 없음)는 true 로 로드된다 — 3D 후처리의
        /// PointCloudPostProcessPreset.CameraDefault 와 동일한 규칙.
        /// </summary>
        public bool Use2DCameraDefault
        {
            get => _use2DCameraDefault;
            set => SetProperty(ref _use2DCameraDefault, value);
        }

        private double _exposure = 1000;
        /// <summary>
        /// 노출 시간 (μs)
        /// </summary>
        public double Exposure
        {
            get => _exposure;
            set
            {
                if (SetProperty(ref _exposure, Math.Max(1, value)))
                    OnPropertyChanged(nameof(ExposureMs));
            }
        }

        /// <summary>
        /// 노출 UI 입력용 ms 단위 — Mech-Eye Viewer 와 동일 단위 (200 입력 = 200ms).
        /// 레시피 JSON 은 µs(Exposure) 그대로 저장 — 기존 레시피 호환.
        /// </summary>
        [JsonIgnore]
        public double ExposureMs
        {
            get => Exposure / 1000.0;
            set => Exposure = value * 1000.0;
        }

        private double _gain = 1.0;
        /// <summary>
        /// 게인 값
        /// </summary>
        public double Gain
        {
            get => _gain;
            set => SetProperty(ref _gain, Math.Max(0, value));
        }

        private int _lightingChannel;
        /// <summary>
        /// 조명 컨트롤러 채널
        /// </summary>
        public int LightingChannel
        {
            get => _lightingChannel;
            set => SetProperty(ref _lightingChannel, Math.Max(0, value));
        }

        private int _lightingIntensity = 100;
        /// <summary>
        /// 조명 강도 (0-255)
        /// </summary>
        public int LightingIntensity
        {
            get => _lightingIntensity;
            set => SetProperty(ref _lightingIntensity, Math.Clamp(value, 0, 255));
        }

        // ── 3D 스캔 카메라 파라미터 (Mech-Eye Viewer 의 '포인트 클라우드 후처리'와 동일 개념) ──
        // 촬영 시점에 카메라(SDK)에 적용 — depth map/점군/후속 도구가 모두 정제된 데이터를 받는다.

        private PointCloudPostProcessPreset _pointCloudPostProcess = PointCloudPostProcessPreset.CameraDefault;
        /// <summary>
        /// 3D 후처리 강도 (표면 스무딩/노이즈·이상점 제거). CameraDefault = 카메라 설정 유지.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PointCloudPostProcessPreset PointCloudPostProcess
        {
            get => _pointCloudPostProcess;
            set => SetProperty(ref _pointCloudPostProcess, value);
        }

        private bool _useDepthRange;
        /// <summary>
        /// 뎁스 범위 제한 사용 여부 — false 면 카메라 현재 범위 유지
        /// </summary>
        public bool UseDepthRange
        {
            get => _useDepthRange;
            set => SetProperty(ref _useDepthRange, value);
        }

        private double _depthRangeMinMm;
        /// <summary>
        /// 뎁스 하한 (mm, 카메라 기준 거리)
        /// </summary>
        public double DepthRangeMinMm
        {
            get => _depthRangeMinMm;
            set => SetProperty(ref _depthRangeMinMm, Math.Max(0, value));
        }

        private double _depthRangeMaxMm = 3000;
        /// <summary>
        /// 뎁스 상한 (mm, 카메라 기준 거리)
        /// </summary>
        public double DepthRangeMaxMm
        {
            get => _depthRangeMaxMm;
            set => SetProperty(ref _depthRangeMaxMm, Math.Max(0, value));
        }

        #endregion

        /// <summary>
        /// 이 스텝에서 실행할 도구 목록
        /// </summary>
        public List<ToolConfig> Tools { get; set; } = new();

        /// <summary>
        /// 스텝 별칭 (선택적) — 사용자가 자유 입력하는 표시용 라벨 (예: "Node 3").
        /// 파생 이름(Name)과 달리 저장·보존되며 식별에는 쓰이지 않으므로 중복 가능.
        /// </summary>
        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set
            {
                if (SetProperty(ref _description, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(DisplayInfo));
                }
            }
        }

        /// <summary>
        /// 로봇 노드 번호 (선택적) — 로봇이 노드 index 를 보내오면 이 값이 일치하는
        /// 스텝으로 매칭한다 (RecipeService.FindStepByRobotNode). null 이면 순번(Sequence) 매칭.
        /// </summary>
        private int? _robotNodeIndex;
        public int? RobotNodeIndex
        {
            get => _robotNodeIndex;
            set => SetProperty(ref _robotNodeIndex, value);
        }

        /// <summary>
        /// 스텝 활성화 여부
        /// </summary>
        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 참조 이미지 경로 (테스트/시뮬레이션용)
        /// </summary>
        private string? _referenceImagePath;
        public string? ReferenceImagePath
        {
            get => _referenceImagePath;
            set => SetProperty(ref _referenceImagePath, value);
        }

        /// <summary>
        /// 로봇 웨이포인트 (null이면 로봇 이동 없이 고정 위치 촬영)
        /// </summary>
        private ScanWaypoint? _robotWaypoint;
        public ScanWaypoint? RobotWaypoint
        {
            get => _robotWaypoint;
            set => SetProperty(ref _robotWaypoint, value);
        }

        /// <summary>
        /// 표시용 이름 — 파생 이름에 별칭을 병기 (예: "1-3 — Node 3")
        /// </summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} — {Description}";

        /// <summary>
        /// 표시용 스텝 정보 문자열
        /// </summary>
        [JsonIgnore]
        public string DisplayInfo => RobotWaypoint != null ? $"{DisplayName} (Robot)" : DisplayName;

        public override string ToString() => DisplayInfo;
    }
}
