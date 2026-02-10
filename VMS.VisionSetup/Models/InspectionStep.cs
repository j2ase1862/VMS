using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        /// 스텝 이름 (예: "Point A", "Top View")
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
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

        private double _exposure = 1000;
        /// <summary>
        /// 노출 시간 (μs)
        /// </summary>
        public double Exposure
        {
            get => _exposure;
            set => SetProperty(ref _exposure, Math.Max(1, value));
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

        #endregion

        /// <summary>
        /// 이 스텝에서 실행할 도구 목록
        /// </summary>
        public List<ToolConfig> Tools { get; set; } = new();

        /// <summary>
        /// 스텝 설명 (선택적)
        /// </summary>
        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
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
        /// 표시용 스텝 정보 문자열
        /// </summary>
        [JsonIgnore]
        public string DisplayInfo => $"{Name}";
    }
}
