using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 검사 레시피 (제품 검사 설정 컨테이너)
    /// </summary>
    public class Recipe : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>
        /// 레시피 고유 ID (GUID)
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        /// <summary>
        /// 레시피 이름 (예: "Mainboard Model A")
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _description = string.Empty;
        /// <summary>
        /// 레시피 설명
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private string _version = "1.0.0";
        /// <summary>
        /// 레시피 버전
        /// </summary>
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        private DateTime _createdAt = DateTime.UtcNow;
        /// <summary>
        /// 생성 일시
        /// </summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        private DateTime _modifiedAt = DateTime.UtcNow;
        /// <summary>
        /// 수정 일시
        /// </summary>
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set => SetProperty(ref _modifiedAt, value);
        }

        private string _author = string.Empty;
        /// <summary>
        /// 작성자
        /// </summary>
        public string Author
        {
            get => _author;
            set => SetProperty(ref _author, value);
        }

        /// <summary>
        /// 이 레시피에서 사용하는 카메라 ID 목록 (글로벌 레지스트리 참조)
        /// </summary>
        public List<string> UsedCameraIds { get; set; } = new();

        /// <summary>
        /// 검사 스텝 목록
        /// </summary>
        public List<InspectionStep> Steps { get; set; } = new();

        /// <summary>
        /// Pass/Fail 판정 기준
        /// </summary>
        public PassFailCriteria? Criteria { get; set; }

        /// <summary>
        /// 레시피 태그 (분류용)
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// 표시용 정보 문자열
        /// </summary>
        [JsonIgnore]
        public string DisplayInfo => $"{Name} (v{Version})";

        /// <summary>
        /// 마지막 수정 시간 표시용
        /// </summary>
        [JsonIgnore]
        public string ModifiedAtDisplay => ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 총 도구 수
        /// </summary>
        [JsonIgnore]
        public int TotalToolCount
        {
            get
            {
                int count = 0;
                foreach (var step in Steps)
                    count += step.Tools.Count;
                return count;
            }
        }
    }

    /// <summary>
    /// Pass/Fail 판정 기준
    /// </summary>
    public class PassFailCriteria : ObservableObject
    {
        private bool _requireAllToolsPass = true;
        /// <summary>
        /// 모든 도구가 Pass여야 전체 Pass
        /// </summary>
        public bool RequireAllToolsPass
        {
            get => _requireAllToolsPass;
            set => SetProperty(ref _requireAllToolsPass, value);
        }

        /// <summary>
        /// 도구별 개별 Pass 기준 (키: 도구 ID)
        /// </summary>
        public Dictionary<string, ToolPassCriteria> ToolCriteria { get; set; } = new();

        /// <summary>
        /// 전체 검사 시간 제한 (ms, 0이면 무제한)
        /// </summary>
        private double _maxExecutionTime;
        public double MaxExecutionTime
        {
            get => _maxExecutionTime;
            set => SetProperty(ref _maxExecutionTime, Math.Max(0, value));
        }
    }

    /// <summary>
    /// 개별 도구 Pass 기준
    /// </summary>
    public class ToolPassCriteria : ObservableObject
    {
        /// <summary>
        /// 수치 범위 기준 (키: 측정값 이름, 값: 범위)
        /// </summary>
        public Dictionary<string, RangeCriteria> Ranges { get; set; } = new();

        /// <summary>
        /// 필수 성공 여부
        /// </summary>
        private bool _mustSucceed = true;
        public bool MustSucceed
        {
            get => _mustSucceed;
            set => SetProperty(ref _mustSucceed, value);
        }
    }

    /// <summary>
    /// 범위 기준
    /// </summary>
    public class RangeCriteria
    {
        /// <summary>
        /// 최소값 (null이면 하한 없음)
        /// </summary>
        public double? Min { get; set; }

        /// <summary>
        /// 최대값 (null이면 상한 없음)
        /// </summary>
        public double? Max { get; set; }

        /// <summary>
        /// 기준값 (선택적)
        /// </summary>
        public double? Nominal { get; set; }

        /// <summary>
        /// 공차 (기준값 ± 공차)
        /// </summary>
        public double? Tolerance { get; set; }

        /// <summary>
        /// 값이 범위 내에 있는지 확인
        /// </summary>
        public bool IsInRange(double value)
        {
            if (Min.HasValue && value < Min.Value)
                return false;
            if (Max.HasValue && value > Max.Value)
                return false;
            return true;
        }
    }

    /// <summary>
    /// 레시피 목록 표시용 정보 (간략화)
    /// </summary>
    public class RecipeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime ModifiedAt { get; set; }
        public string Author { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int StepCount { get; set; }
        public int ToolCount { get; set; }

        public string ModifiedAtDisplay => ModifiedAt.ToString("yyyy-MM-dd HH:mm");
    }
}
