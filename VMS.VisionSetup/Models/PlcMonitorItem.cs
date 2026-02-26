using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// PLC I/O 모니터 패널 각 행의 Observable 모델
    /// </summary>
    public partial class PlcMonitorItem : ObservableObject
    {
        /// <summary>원본 주소 문자열 ("M100", "D200")</summary>
        public string Address { get; }

        /// <summary>파싱된 PLC 주소</summary>
        public PlcAddress ParsedAddress { get; }

        /// <summary>데이터 타입 (Bit/Int16/Int32/Float)</summary>
        public PlcDataType DataType { get; }

        /// <summary>입출력 방향</summary>
        public PlcIoDirection Direction { get; }

        /// <summary>이 주소를 참조하는 노드 이름 목록</summary>
        [ObservableProperty]
        private string _referencedNodes = string.Empty;

        /// <summary>현재값 표시 ("ON"/"OFF"/숫자/"---")</summary>
        [ObservableProperty]
        private string _currentValue = "---";

        /// <summary>조건 충족 여부 (null=미평가, true=충족, false=미충족)</summary>
        [ObservableProperty]
        private bool? _conditionMet;

        /// <summary>에러 메시지</summary>
        [ObservableProperty]
        private string? _errorMessage;

        /// <summary>마지막 갱신 시각</summary>
        [ObservableProperty]
        private string _lastUpdated = string.Empty;

        /// <summary>조건 상태 표시 색상</summary>
        public string ConditionColor => ConditionMet switch
        {
            true => "#4CAF50",
            false => "#F44336",
            _ => "#666666"
        };

        public PlcMonitorItem(string address, PlcAddress parsedAddress, PlcDataType dataType, PlcIoDirection direction)
        {
            Address = address;
            ParsedAddress = parsedAddress;
            DataType = dataType;
            Direction = direction;
        }

        partial void OnConditionMetChanged(bool? value)
        {
            OnPropertyChanged(nameof(ConditionColor));
        }
    }

    /// <summary>
    /// PLC I/O 방향
    /// </summary>
    public enum PlcIoDirection
    {
        Input,
        Output
    }
}
