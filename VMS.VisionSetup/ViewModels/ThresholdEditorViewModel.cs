using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>한 도구에 추가된 한 임계치 행 — 키 + Min/Max + 마지막 실행 값.</summary>
    public partial class ThresholdRow : ObservableObject
    {
        public string Key { get; set; } = string.Empty;
        public string LastValueText { get; set; } = string.Empty;
        [ObservableProperty] private string _minText = string.Empty;
        [ObservableProperty] private string _maxText = string.Empty;
    }

    /// <summary>도구 한 개의 임계치 카드 — MustSucceed + Threshold 행들 + 추가 가능한 키 목록.</summary>
    public partial class ThresholdToolEntry : ObservableObject
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string ToolType { get; set; } = string.Empty;

        [ObservableProperty] private bool _mustSucceed = true;

        /// <summary>현재 추가된 임계치 행들.</summary>
        public ObservableCollection<ThresholdRow> Rows { get; } = new();

        /// <summary>아직 추가 가능한 키 목록 (전체 키 - 이미 추가된 키). 카드 하단 ComboBox 항목.</summary>
        public ObservableCollection<string> AvailableKeysForAdd { get; } = new();

        /// <summary>이 도구의 전체 결과 키 풀 (GetAvailableResultKeys + LastResult.Data union).</summary>
        public List<string> AllKnownKeys { get; set; } = new();

        /// <summary>키 → 마지막 실행 값 문자열 (UI 가이드용).</summary>
        public Dictionary<string, string> LastValues { get; set; } = new();

        [ObservableProperty] private string? _selectedKeyToAdd;

        /// <summary>Add 후 AvailableKeysForAdd 갱신.</summary>
        public void RefreshAvailableKeys()
        {
            var used = new HashSet<string>(Rows.Select(r => r.Key));
            AvailableKeysForAdd.Clear();
            foreach (var k in AllKnownKeys)
                if (!used.Contains(k)) AvailableKeysForAdd.Add(k);
        }
    }

    /// <summary>
    /// Recipe.Criteria 편집 UI — 카드형 + 키 드롭다운 + 마지막 실행 값 가이드.
    /// 현재 활성 Step의 도구들만 편집 가능 (VisionService.Tools 기준).
    /// </summary>
    public partial class ThresholdEditorViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly IVisionService _visionService;
        private readonly Recipe _recipe;

        public ThresholdEditorViewModel(IRecipeService recipeService, IVisionService visionService, Recipe recipe)
        {
            _recipeService = recipeService;
            _visionService = visionService;
            _recipe = recipe;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => CancelRequested?.Invoke());

            BuildEntries();
        }

        public ObservableCollection<ThresholdToolEntry> Entries { get; } = new();

        [ObservableProperty] private string _statusMessage = "";

        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public event Action? Saved;
        public event Action? CancelRequested;

        private void BuildEntries()
        {
            Entries.Clear();

            var criteria = _recipe.Criteria;
            var lastResultMap = _visionService.LastExecutionResultsById;

            // 현재 활성 Step의 도구 인스턴스를 기준으로 (GetAvailableResultKeys + LastResult 접근 가능)
            foreach (var tool in _visionService.Tools)
            {
                var entry = new ThresholdToolEntry
                {
                    ToolId = tool.Id,
                    ToolName = string.IsNullOrEmpty(tool.Name) ? tool.ToolType : tool.Name,
                    ToolType = tool.ToolType
                };

                // 전체 키 풀 = 선언된 키 ∪ 마지막 결과에 나타난 키
                var keyPool = new List<string>();
                try
                {
                    foreach (var k in tool.GetAvailableResultKeys())
                        if (k != "Success" && !keyPool.Contains(k)) keyPool.Add(k);
                }
                catch { /* skip */ }

                if (lastResultMap.TryGetValue(tool.Id, out var lastRes) && lastRes?.Data != null)
                {
                    foreach (var k in lastRes.Data.Keys)
                        if (k != "Success" && !keyPool.Contains(k)) keyPool.Add(k);

                    // 마지막 값 캐싱
                    foreach (var kv in lastRes.Data)
                    {
                        entry.LastValues[kv.Key] = FormatLastValue(kv.Value);
                    }
                }
                entry.AllKnownKeys = keyPool;

                // 기존 Criteria 적용
                if (criteria?.ToolCriteria != null && criteria.ToolCriteria.TryGetValue(tool.Id, out var tc) && tc != null)
                {
                    entry.MustSucceed = tc.MustSucceed;
                    if (tc.Ranges != null)
                    {
                        foreach (var kv in tc.Ranges)
                        {
                            var row = new ThresholdRow
                            {
                                Key = kv.Key,
                                LastValueText = entry.LastValues.TryGetValue(kv.Key, out var lv) ? lv : "-",
                                MinText = kv.Value?.Min?.ToString("G6", CultureInfo.InvariantCulture) ?? "",
                                MaxText = kv.Value?.Max?.ToString("G6", CultureInfo.InvariantCulture) ?? ""
                            };
                            entry.Rows.Add(row);
                        }
                    }
                }

                entry.RefreshAvailableKeys();
                Entries.Add(entry);
            }

            if (Entries.Count == 0)
                StatusMessage = "현재 활성 Step에 도구가 없습니다. Recipe → Step을 먼저 선택하세요.";
        }

        [RelayCommand]
        private void AddThresholdRow(ThresholdToolEntry? entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.SelectedKeyToAdd)) return;
            var key = entry.SelectedKeyToAdd;
            entry.Rows.Add(new ThresholdRow
            {
                Key = key,
                LastValueText = entry.LastValues.TryGetValue(key, out var lv) ? lv : "-"
            });
            entry.SelectedKeyToAdd = null;
            entry.RefreshAvailableKeys();
        }

        [RelayCommand]
        private void RemoveThresholdRow(ThresholdRow? row)
        {
            if (row == null) return;
            var entry = Entries.FirstOrDefault(e => e.Rows.Contains(row));
            if (entry == null) return;
            entry.Rows.Remove(row);
            entry.RefreshAvailableKeys();
        }

        private void Save()
        {
            var criteria = _recipe.Criteria ?? new PassFailCriteria();
            criteria.ToolCriteria ??= new Dictionary<string, ToolPassCriteria>();

            // 편집 중인 도구만 업데이트 (다른 Step의 도구 entry는 그대로 유지)
            foreach (var entry in Entries)
            {
                var ranges = new Dictionary<string, RangeCriteria>();
                foreach (var row in entry.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
                {
                    var range = new RangeCriteria
                    {
                        Min = ParseNullable(row.MinText),
                        Max = ParseNullable(row.MaxText)
                    };
                    if (range.Min.HasValue || range.Max.HasValue)
                        ranges[row.Key] = range;
                }

                if (ranges.Count == 0 && entry.MustSucceed)
                {
                    // 빈 entry — Criteria 에서도 제거 (MustSucceed가 기본이므로 의미 없음)
                    criteria.ToolCriteria.Remove(entry.ToolId);
                    continue;
                }

                criteria.ToolCriteria[entry.ToolId] = new ToolPassCriteria
                {
                    MustSucceed = entry.MustSucceed,
                    Ranges = ranges
                };
            }

            _recipe.Criteria = criteria;
            try
            {
                _recipeService.SaveRecipe(_recipe);
                StatusMessage = "임계치 저장 완료.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"저장 실패: {ex.Message}";
                return;
            }

            Saved?.Invoke();
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        private static string FormatLastValue(object? v)
        {
            return v switch
            {
                null => "-",
                double d => d.ToString("G6", CultureInfo.InvariantCulture),
                float f => f.ToString("G6", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => v.ToString() ?? "-"
            };
        }
    }
}
