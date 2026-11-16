using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Models
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
            set
            {
                if (SetProperty(ref _result, value))
                    OnPropertyChanged(nameof(ResultText));
            }
        }

        /// <summary>Run Results P/F 칸에 보이는 글자 — Pass / Fail (bool 을 그대로 묶으면 True / False 로 나온다, VM-04).</summary>
        public string ResultText => _result ? "Pass" : "Fail";

        private string _resultValue = string.Empty;
        public string ResultValue
        {
            get => _resultValue;
            set => SetProperty(ref _resultValue, value);
        }

        private double _score;
        public double Score
        {
            get => _score;
            set => SetProperty(ref _score, value);
        }

        private double _threshold;
        public double Threshold
        {
            get => _threshold;
            set => SetProperty(ref _threshold, value);
        }

        private string _unit = string.Empty;
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        }
    }
}
