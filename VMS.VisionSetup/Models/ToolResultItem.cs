using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 도구 실행 결과 항목 (DataGrid 바인딩용)
    /// </summary>
    public class ToolResultItem : ObservableObject
    {
        private string _toolName = string.Empty;
        public string ToolName
        {
            get => _toolName;
            set => SetProperty(ref _toolName, value);
        }

        private bool _result;
        public bool Result
        {
            get => _result;
            set => SetProperty(ref _result, value);
        }

        private string _resultValue = string.Empty;
        public string ResultValue
        {
            get => _resultValue;
            set => SetProperty(ref _resultValue, value);
        }
    }
}
