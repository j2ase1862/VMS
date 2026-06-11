using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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
        public static int Cleanup(ImageSaveOptions? options)
        {
            if (options == null || options.RetentionDays <= 0) return 0;
            if (string.IsNullOrWhiteSpace(options.BaseDir) || !Directory.Exists(options.BaseDir)) return 0;

            var cutoff = DateTime.Today.AddDays(-options.RetentionDays);
            int deleted = 0;

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
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ImageRetentionCleaner] 폴더 삭제 실패 '{dir}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageRetentionCleaner] 정리 중 오류: {ex.Message}");
            }

            return deleted;
        }
    }
}
