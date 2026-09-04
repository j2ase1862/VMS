using System;
using System.Collections.Generic;
using VMS.Core.Retention;

namespace VMS.Services.LocalHistory
{
    /// <summary>
    /// 로컬 검사 이력 저장소 (SQLite). 쓰기는 큐에 넣고 즉시 반환 — 검사 택트 임계경로에
    /// 디스크 I/O 를 두지 않는다. 읽기는 호출 스레드에서 동기 실행.
    /// </summary>
    public interface ILocalInspectionHistoryStore : IDisposable
    {
        string DbPath { get; }

        /// <summary>검사/사이클 1건 기록 (비동기 큐). 실패는 삼키고 Debug 로그만.</summary>
        void Record(LocalInspectionEntry entry);

        /// <summary>
        /// 상관 키로 이미지 경로 갱신 (비동기 큐). 행이 아직 없으면(사이클 플러시 전) 보류했다가
        /// 같은 키의 행이 들어올 때 붙인다. 이미 NG 이미지가 있으면 OK 이미지로 덮지 않는다.
        /// </summary>
        void SetImagePath(string correlationKey, string imagePath, bool isNg);

        /// <summary>큐에 쌓인 쓰기를 모두 디스크에 반영할 때까지 대기 (테스트·종료용).</summary>
        bool Flush(TimeSpan timeout);

        IReadOnlyList<LocalInspectionEntry> Query(LocalInspectionQuery query);
        long Count(LocalInspectionQuery query);
        IReadOnlyList<LocalInspectionDailySummary> GetDailySummary(DateTime fromLocal, DateTime toLocalExclusive);
        IReadOnlyList<LocalNgCodeCount> GetNgCodeCounts(DateTime fromLocal, DateTime toLocalExclusive, int top);

        /// <summary>보존 일수보다 오래된 행 삭제. 결과를 AuditCategory.System 으로 기록.</summary>
        int PurgeOlderThan(int retentionDays);
        long CountOlderThan(int retentionDays);
        long FileSizeBytes { get; }
    }
}
