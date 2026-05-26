using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using VMS.Core.Models;

namespace VMS.Core.Services
{
    /// <summary>
    /// D8 — In-Memory 순환 버퍼로 최근 검사 결과 보관. Web 끊겨도 작업자가 즉시 확인 가능.
    /// 프로세스 재시작 시 비워짐 (Web 의 InspectionHistory 가 영구 저장 역할).
    /// </summary>
    public class RecentInspectionsService : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        private static RecentInspectionsService? _instance;
        public static RecentInspectionsService Instance => _instance ??= new RecentInspectionsService();

        /// <summary>최대 보관 건수 — 200 건 초과 시 가장 오래된 것부터 제거.</summary>
        public int MaxItems { get; set; } = 200;

        /// <summary>최신이 [0] — UI 바인딩용. UI 스레드에서만 변경.</summary>
        public ObservableCollection<InspectionRecord> Items { get; } = new();

        private int _totalCount;
        private int _passCount;
        private int _ngCount;

        public int TotalCount => _totalCount;
        public int PassCount => _passCount;
        public int NgCount => _ngCount;
        public double PassRate => _totalCount > 0
            ? Math.Round((double)_passCount / _totalCount * 100, 1)
            : 0;

        public event Action<InspectionRecord>? RecordAdded;
        public event Action? StatsChanged;

        /// <summary>검사 1건 추가. 모든 스레드에서 호출 가능 — 내부에서 UI 스레드로 마샬링.</summary>
        public void Add(InspectionRecord record)
        {
            var app = Application.Current;
            if (app == null)
            {
                // UI 없는 컨텍스트 (테스트 등) — 컬렉션 직접 변경
                InsertCore(record);
                return;
            }
            app.Dispatcher.Invoke(() => InsertCore(record));
        }

        private void InsertCore(InspectionRecord record)
        {
            Items.Insert(0, record);
            while (Items.Count > MaxItems)
                Items.RemoveAt(Items.Count - 1);

            _totalCount++;
            if (record.IsPass) _passCount++; else _ngCount++;

            RecordAdded?.Invoke(record);
            StatsChanged?.Invoke();
            Raise(nameof(TotalCount));
            Raise(nameof(PassCount));
            Raise(nameof(NgCount));
            Raise(nameof(PassRate));
        }

        /// <summary>모든 기록과 카운터 초기화 — 작업자가 수동으로 클리어할 때.</summary>
        public void Clear()
        {
            var app = Application.Current;
            if (app == null) { ClearCore(); return; }
            app.Dispatcher.Invoke(ClearCore);
        }

        private void ClearCore()
        {
            Items.Clear();
            _totalCount = 0;
            _passCount = 0;
            _ngCount = 0;
            StatsChanged?.Invoke();
            Raise(nameof(TotalCount));
            Raise(nameof(PassCount));
            Raise(nameof(NgCount));
            Raise(nameof(PassRate));
        }
    }
}
