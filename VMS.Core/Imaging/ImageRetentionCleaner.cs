using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using VMS.Core.Security;

namespace VMS.Core.Imaging
{
    /// <summary>
    /// 저장된 검사 이미지의 보존 정책 적용 — BaseDir 아래 "yyyy-MM-dd" 폴더 중
    /// 보존 기간(RetentionDays)을 넘긴 것을 삭제. 폴더명이 유효한 날짜가 아니면
    /// 건드리지 않아(안전), 사용자가 만든 다른 폴더는 보호된다.
    /// WPF 비의존 — 시작 시 백그라운드에서 호출.
    /// </summary>
    public static class ImageRetentionCleaner
    {
        /// <summary>
        /// 기준일(오늘 - RetentionDays)보다 오래된 날짜 폴더를 삭제하고 삭제한 폴더 수를 반환.
        /// RetentionDays ≤ 0(무제한) 또는 BaseDir 미지정/부재 시 아무것도 하지 않음.
        /// 개별 폴더 삭제 실패는 격리(로그만) — 다른 폴더 정리는 계속.
        /// </summary>
        public static int Cleanup(ImageSaveOptions? options) => CleanupDetailed(options).Deleted.Count;

        /// <summary>정리 결과 — 지운 폴더 이름과 지우지 못한 폴더 이름.</summary>
        public sealed record Result(IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed);

        /// <summary>
        /// 정리하고 결과를 감사 로그(Configuration · "ImageRetentionCleanup")에 남긴다 (VM-41).
        /// 지운 것도 실패한 것도 없으면 기록하지 않는다 — 하루 한 번 도는 일이라 빈 기록은 소음이다.
        /// </summary>
        public static Result CleanupAndAudit(ImageSaveOptions? options, string trigger)
        {
            var result = CleanupDetailed(options);
            if (result.Deleted.Count == 0 && result.Failed.Count == 0) return result;

            try
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "ImageRetentionCleanup",
                    result.Failed.Count == 0 ? AuditOutcome.Success : AuditOutcome.Failure,
                    source: nameof(ImageRetentionCleaner),
                    details: $"Trigger={trigger}, BaseDir={options?.BaseDir}, RetentionDays={options?.RetentionDays}, " +
                             $"Deleted={result.Deleted.Count} [{string.Join(", ", result.Deleted)}]" +
                             (result.Failed.Count > 0 ? $", Failed={result.Failed.Count} [{string.Join(", ", result.Failed)}]" : ""));
            }
            catch { /* 감사 기록 실패가 정리를 무르지 않는다 */ }
            return result;
        }

        public static Result CleanupDetailed(ImageSaveOptions? options)
        {
            var deletedNames = new List<string>();
            var failedNames = new List<string>();
            if (options == null || options.RetentionDays <= 0) return new Result(deletedNames, failedNames);
            if (string.IsNullOrWhiteSpace(options.BaseDir) || !Directory.Exists(options.BaseDir))
                return new Result(deletedNames, failedNames);

            var cutoff = DateTime.Today.AddDays(-options.RetentionDays);

            try
            {
                foreach (var dir in Directory.GetDirectories(options.BaseDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!DateTime.TryParseExact(name, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var folderDate))
                    {
                        continue; // 날짜 폴더가 아니면 보호
                    }

                    if (folderDate >= cutoff) continue;

                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        deletedNames.Add(name);
                    }
                    catch (Exception ex)
                    {
                        failedNames.Add(name);
                        Debug.WriteLine($"[ImageRetentionCleaner] 폴더 삭제 실패 '{dir}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageRetentionCleaner] 정리 중 오류: {ex.Message}");
            }

            return new Result(deletedNames, failedNames);
        }
    }
}
