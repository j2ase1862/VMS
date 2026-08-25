using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VMS.Camera.Models;

namespace VMS.VisionSetup.Views
{
    /// <summary>
    /// 미등록 카메라 재연결 다이얼로그 — 다른 PC 에서 작성한 레시피의 스텝 CameraId 를
    /// 이 PC 의 카메라로 다시 연결한다 (2026-08-25 현장 실증: 카메라를 재등록해도
    /// GUID 가 달라 해결되지 않던 문제의 정식 해결 경로).
    /// 표시/수집만 담당 — 실제 교체·저장은 호출자(MainViewModel)가 수행.
    /// </summary>
    public partial class CameraRemapDialog : Window
    {
        /// <summary>콤보 항목 — Id == null 이면 "매핑 안 함".</summary>
        public sealed class CameraOption
        {
            public string? Id { get; init; }
            public string Label { get; init; } = string.Empty;
        }

        /// <summary>미등록 카메라 1건의 행.</summary>
        public sealed class RemapRow
        {
            public string OldCameraId { get; init; } = string.Empty;
            public string OldIdDisplay { get; init; } = string.Empty;
            public string StepCountDisplay { get; init; } = string.Empty;
            public List<CameraOption> Options { get; init; } = new();
            public CameraOption? Selected { get; set; }
        }

        private readonly List<RemapRow> _rows;

        /// <summary>적용 시 채워지는 결과 — 구 CameraId → 이 PC 의 CameraInfo.Id.</summary>
        public Dictionary<string, string> Mapping { get; } = new();

        public CameraRemapDialog(
            IReadOnlyList<(string oldId, int stepCount)> unregistered,
            IReadOnlyList<CameraInfo> cameras)
        {
            InitializeComponent();

            var noMapping = new CameraOption { Id = null, Label = "매핑 안 함" };
            _rows = unregistered.Select((u, index) =>
            {
                var options = new List<CameraOption> { noMapping };
                options.AddRange(cameras.Select((c, i) => new CameraOption
                {
                    Id = c.Id,
                    Label = string.IsNullOrEmpty(c.Model) ? $"{i + 1}. {c.Name}" : $"{i + 1}. {c.Name} ({c.Model})",
                }));
                return new RemapRow
                {
                    OldCameraId = u.oldId,
                    OldIdDisplay = u.oldId,
                    StepCountDisplay = $"스텝 {u.stepCount}개 참조 (현재 '?-n' 표시)",
                    Options = options,
                    // 미등록 1대 + 등록 1대의 흔한 케이스는 바로 [적용]이 되도록 기본 선택
                    Selected = unregistered.Count == 1 && cameras.Count == 1 ? options[1] : noMapping,
                };
            }).ToList();

            RowsControl.ItemsSource = _rows;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            Mapping.Clear();
            foreach (var row in _rows)
            {
                if (row.Selected?.Id is string newId)
                    Mapping[row.OldCameraId] = newId;
            }
            DialogResult = true;
        }
    }
}
