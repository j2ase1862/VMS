using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace VMS.VisionSetup.Models
{
    /// <summary>결과 상세 한 줄 (Key–Value) — Run Results 행 펼침(RowDetails)용.</summary>
    public class ResultEntry
    {
        public string Key { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }

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
        /// <summary>요약 한 줄 (툴별 핵심 값만 — 전체는 Entries/DetailText).</summary>
        public string ResultValue
        {
            get => _resultValue;
            set => SetProperty(ref _resultValue, value);
        }

        /// <summary>전체 결과 Key–Value 목록 — 행 선택 시 펼침 표시.</summary>
        public List<ResultEntry> Entries { get; set; } = new();

        /// <summary>전체 결과 멀티라인 텍스트 — 셀 툴팁·클립보드 복사용.</summary>
        public string DetailText { get; set; } = string.Empty;
    }
}
