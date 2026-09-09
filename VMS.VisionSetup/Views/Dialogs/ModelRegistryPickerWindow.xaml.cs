using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VMS.Core.DeepLearning;
using VMS.Core.Services;

namespace VMS.VisionSetup.Views.Dialogs
{
    /// <summary>
    /// MLOps 모델 레지스트리에서 참조(<c>model://…</c>)를 고르는 창.
    ///
    /// <para>
    /// 이 창이 있는 이유는 하나다 — 사람이 GUID 를 손으로 타이핑하지 않게 하는 것.
    /// 고른 결과는 파일 경로가 아니라 참조 문자열이고, 실제 파일은 레시피를 열 때 라인 PC 가 받아 온다.
    /// </para>
    /// <para>
    /// 레지스트리 설정(주소·라인 토큰)이 없으면 창을 열지 않고 이유를 말한다.
    /// </para>
    /// </summary>
    public partial class ModelRegistryPickerWindow : Window
    {
        /// <summary>목록 한 줄이 화면에 보여 줄 것.</summary>
        private sealed class ModelRow
        {
            public RegistryModel Model { get; init; } = null!;
            public string Name => Model.Name;
            public string Summary
            {
                get
                {
                    var classes = Model.Classes.Length == 0
                        ? "클래스 없음"
                        : string.Join(", ", Model.Classes.Take(4)) + (Model.Classes.Length > 4 ? " …" : "");
                    return $"{Model.TaskType} · {classes}";
                }
            }
        }

        private sealed class VersionRow
        {
            public RegistryModelVersion Version { get; init; } = null!;
            public string Summary =>
                $"v{Version.Number} · {StageText(Version.Stage)} · " +
                $"{Version.SizeBytes / 1024 / 1024}MB · {Version.CreatedAt.ToLocalTime():yyyy-MM-dd}";

            private static string StageText(string stage) => stage?.ToLowerInvariant() switch
            {
                "production" => "운영",
                "staging" => "테스트",
                "candidate" => "후보",
                "archived" => "보관",
                _ => stage ?? "",
            };
        }

        private readonly string _registryUrl;
        private readonly string _lineToken;
        private readonly string? _taskType;

        /// <summary>[선택] 을 누르면 여기에 참조 문자열이 담긴다.</summary>
        public string? SelectedReference { get; private set; }

        public ModelRegistryPickerWindow(string registryUrl, string lineToken, string? taskType = null)
        {
            InitializeComponent();
            _registryUrl = registryUrl;
            _lineToken = lineToken;
            _taskType = taskType;
            Loaded += async (_, _) => await LoadModelsAsync();
        }

        /// <summary>
        /// 설정이 갖춰져 있으면 창을 열어 참조를 받아 온다. 아니면 이유를 알리고 null.
        /// 부르는 쪽이 설정 확인까지 하지 않아도 되게 여기서 함께 본다.
        /// </summary>
        public static string? PickReference(Window? owner, string? registryUrl, string? lineToken, string? taskType = null)
        {
            if (string.IsNullOrWhiteSpace(registryUrl) || string.IsNullOrWhiteSpace(lineToken))
            {
                MessageBox.Show(owner,
                    "MLOps 서버 주소와 라인 토큰이 설정되지 않았습니다.\n" +
                    "설정 → 시스템에서 MLOps 서버 주소와 라인 토큰을 먼저 지정하세요.",
                    "레지스트리 설정 없음", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var window = new ModelRegistryPickerWindow(registryUrl!, lineToken!, taskType);
            if (owner is not null) window.Owner = owner;
            return window.ShowDialog() == true ? window.SelectedReference : null;
        }

        private async System.Threading.Tasks.Task LoadModelsAsync()
        {
            StatusText.Text = "레지스트리에서 모델 목록을 받는 중…";
            try
            {
                using var client = new ModelRegistryClient(_registryUrl, _lineToken);
                var models = await client.ListModelsAsync(_taskType);
                var rows = models.Select(m => new ModelRow { Model = m }).ToList();
                ModelList.ItemsSource = rows;
                StatusText.Text = rows.Count == 0
                    ? "등록된 모델이 없습니다."
                    : $"모델 {rows.Count}개";
                if (rows.Count > 0) ModelList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private async void ModelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            VersionList.ItemsSource = null;
            UpdatePreview();
            if (ModelList.SelectedItem is not ModelRow row) return;

            try
            {
                using var client = new ModelRegistryClient(_registryUrl, _lineToken);
                var versions = await client.ListVersionsAsync(row.Model.Id);
                VersionList.ItemsSource = versions
                    .OrderByDescending(v => v.Number)
                    .Select(v => new VersionRow { Version = v })
                    .ToList();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            UpdatePreview();
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

        private void Target_Changed(object sender, RoutedEventArgs e)
        {
            if (VersionList is null || StageBox is null) return;   // XAML 로드 중에도 불린다
            bool byVersion = VersionOption.IsChecked == true;
            VersionList.IsEnabled = byVersion;
            StageBox.IsEnabled = !byVersion;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            SelectedReference = BuildReference();
            PreviewText.Text = SelectedReference ?? "—";
            OkButton.IsEnabled = SelectedReference is not null;
        }

        private string? BuildReference()
        {
            if (ModelList?.SelectedItem is not ModelRow row) return null;

            if (VersionOption?.IsChecked == true)
            {
                if (VersionList?.SelectedItem is not VersionRow version) return null;
                return ModelReference.ForVersion(row.Model.Id, version.Version.Number).ToString();
            }

            var stage = (StageBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "production";
            return ModelReference.ForStage(row.Model.Id, stage).ToString();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedReference is null) return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
