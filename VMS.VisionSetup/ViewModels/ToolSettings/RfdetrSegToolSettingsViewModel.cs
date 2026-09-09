using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    /// <summary>
    /// RF-DETR 인스턴스 분할 설정.
    ///
    /// <para>
    /// <see cref="YoloSegToolSettingsViewModel"/> 와 다른 점은 두 가지입니다.
    /// IoU·마스크 임계가 없고(집합 예측이라 NMS 를 쓰지 않고, 마스크는 로짓이라 0 에서 자릅니다),
    /// 대신 남길 인스턴스 상한이 있습니다. 그리고 입력 크기는 모델이 정합니다.
    /// </para>
    /// </summary>
    public class RfdetrSegToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private RfdetrSegTool TypedTool => (RfdetrSegTool)Tool;

        public RfdetrSegToolSettingsViewModel(RfdetrSegTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }

        /// <summary>
        /// 모델을 한 번 열면 ONNX 가 말해 주는 값으로 덮이고 <see cref="InputSizeEditable"/> 가 꺼집니다.
        /// RF-DETR 은 받는 변이 모델마다 다른 배수여야 해서(patch_size × num_windows), 사람이 맞추면
        /// 틀린 값을 넣어도 오류 없이 점수만 조용히 낮아집니다.
        /// </summary>
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }

        /// <summary>모델이 입력 크기를 말해 주기 전에만 고칠 수 있습니다.</summary>
        public bool InputSizeEditable => !TypedTool.InputSizeFromModel;

        /// <summary>
        /// 베이스는 도구가 알린 이름을 그대로 되풀이한다. 화면이 보는 것은 <see cref="InputSizeEditable"/> 인데
        /// 도구가 알리는 이름은 <c>InputSizeFromModel</c> 이라, 이어 주지 않으면 모델을 열어도 칸이 잠기지 않는다.
        /// </summary>
        protected override void OnToolPropertyChanged(string? propertyName)
        {
            base.OnToolPropertyChanged(propertyName);
            if (propertyName == nameof(RfdetrSegTool.InputSizeFromModel))
                OnPropertyChanged(nameof(InputSizeEditable));
        }

        public float ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public int MaxInstances { get => TypedTool.MaxInstances; set => TypedTool.MaxInstances = value; }
        public bool ShowOverlay { get => TypedTool.ShowOverlay; set => TypedTool.ShowOverlay = value; }
        public double OverlayOpacity { get => TypedTool.OverlayOpacity; set => TypedTool.OverlayOpacity = value; }
        public bool DrawBoxes { get => TypedTool.DrawBoxes; set => TypedTool.DrawBoxes = value; }
        public bool OutputMaskImage { get => TypedTool.OutputMaskImage; set => TypedTool.OutputMaskImage = value; }
    }
}
