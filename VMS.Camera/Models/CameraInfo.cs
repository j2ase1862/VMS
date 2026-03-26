using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 카메라 정보 (글로벌 카메라 레지스트리용)
    /// </summary>
    public class CameraInfo : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>
        /// 카메라 고유 ID (GUID)
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        /// <summary>
        /// 카메라 표시 이름 (예: "Top Camera", "Camera 1")
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _manufacturer = string.Empty;
        /// <summary>
        /// 제조사 (예: "Basler", "IDS", "HIK")
        /// </summary>
        public string Manufacturer
        {
            get => _manufacturer;
            set => SetProperty(ref _manufacturer, value);
        }

        private string _model = string.Empty;
        /// <summary>
        /// 카메라 모델명 (예: "acA1920-40gm")
        /// </summary>
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        private string _serialNumber = string.Empty;
        /// <summary>
        /// 시리얼 번호
        /// </summary>
        public string SerialNumber
        {
            get => _serialNumber;
            set => SetProperty(ref _serialNumber, value);
        }

        private int _width = 1920;
        /// <summary>
        /// 이미지 해상도 - 너비
        /// </summary>
        public int Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        private int _height = 1080;
        /// <summary>
        /// 이미지 해상도 - 높이
        /// </summary>
        public int Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        private string _connectionString = string.Empty;
        /// <summary>
        /// 연결 문자열 (IP 주소 또는 디바이스 경로)
        /// </summary>
        public string ConnectionString
        {
            get => _connectionString;
            set => SetProperty(ref _connectionString, value);
        }

        private bool _isEnabled = true;
        /// <summary>
        /// 카메라 활성화 여부
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private CameraType _cameraType = CameraType.AreaScan2D;
        /// <summary>
        /// 카메라 타입 (Area Scan 2D/3D, Line Scan 2D/3D)
        /// </summary>
        public CameraType CameraType
        {
            get => _cameraType;
            set => SetProperty(ref _cameraType, value);
        }

        /// <summary>
        /// 보드 번호 (프레임 그래버: M_DEV0=0, M_DEV1=1, ...)
        /// </summary>
        public int BoardNumber { get; set; }

        /// <summary>
        /// 디지타이저(채널) 번호 (프레임 그래버: CH0=0, CH1=1, ...)
        /// </summary>
        public int DigitizerNumber { get; set; }

        /// <summary>
        /// DCF 파일 경로 (Camera Link 카메라 설정 파일)
        /// </summary>
        public string DcfFilePath { get; set; } = string.Empty;

        /// <summary>
        /// 라인스캔 스캔 길이 (라인 수)
        /// </summary>
        public int ScanLength { get; set; } = 4096;

        /// <summary>
        /// 라인스캔 라인 레이트 (lines/sec)
        /// </summary>
        public double LineRate { get; set; } = 10000;

        /// <summary>
        /// 트리거 소스 (Internal, Encoder)
        /// </summary>
        public string TriggerSource { get; set; } = "Internal";

        /// <summary>
        /// 표시용 해상도 문자열
        /// </summary>
        [JsonIgnore]
        public string Resolution => $"{Width} x {Height}";

        /// <summary>
        /// 표시용 카메라 정보 문자열
        /// </summary>
        [JsonIgnore]
        public string DisplayInfo => $"{Manufacturer} {Model}";
    }

    /// <summary>
    /// 카메라 레지스트리 파일 저장용 컨테이너
    /// </summary>
    public class CameraRegistry
    {
        public List<CameraInfo> Cameras { get; set; } = new();
    }
}
