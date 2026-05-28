using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// PLC / IO 보드 결과 매핑 항목 (1:N 매핑용)
    /// DataGrid 바인딩을 위해 ObservableObject 상속
    /// </summary>
    public class PlcResultMapping : ObservableObject
    {
        private string _resultKey = "Success";
        /// <summary>
        /// VisionResult.Data 딕셔너리 키 또는 "Success"
        /// </summary>
        public string ResultKey
        {
            get => _resultKey;
            set => SetProperty(ref _resultKey, value);
        }

        private string _deviceId = "MainPLC";
        /// <summary>
        /// 출력 대상 디바이스 식별자 (Phase B).
        ///   • "MainPLC" = 기본 PLC (후방호환 — 기존 레시피의 매핑이 누락 시 이 값으로 fallback)
        ///   • "ADLink_1" / "Advantech_Main" 등 = SystemConfiguration.IoBoards 의 DeviceId
        /// SequenceEngine.WriteToolResultsAsync 가 _ioRegistry 에서 lookup 후 PLC / 보드 분기.
        /// </summary>
        public string DeviceId
        {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        private string _plcAddress = string.Empty;
        /// <summary>
        /// 출력 주소.
        ///   • PLC: 어드레스 문자열 (예: "D100", "M100")
        ///   • IO 보드: 채널 번호 문자열 (예: "0", "5")
        /// </summary>
        public string PlcAddress
        {
            get => _plcAddress;
            set => SetProperty(ref _plcAddress, value);
        }

        private PlcDataType _dataType = PlcDataType.Bit;
        /// <summary>
        /// 데이터 타입. IO 보드는 Bit 만 지원 — 그 외는 SequenceEngine 이 경고 후 skip.
        /// </summary>
        public PlcDataType DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }
    }
}
