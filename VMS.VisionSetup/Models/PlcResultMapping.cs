using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// PLC 결과 매핑 항목 (1:N 매핑용)
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

        private string _plcAddress = string.Empty;
        /// <summary>
        /// PLC 주소 문자열 (예: "D100", "M100")
        /// </summary>
        public string PlcAddress
        {
            get => _plcAddress;
            set => SetProperty(ref _plcAddress, value);
        }

        private PlcDataType _dataType = PlcDataType.Bit;
        /// <summary>
        /// PLC에 기록할 데이터 타입
        /// </summary>
        public PlcDataType DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }
    }
}
