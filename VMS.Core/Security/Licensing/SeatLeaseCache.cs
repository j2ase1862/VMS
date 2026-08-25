using System;
using System.IO;
using System.Text.Json;
using VMS.Camera.Configuration;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// Web 서버 좌석 임대 응답의 로컬 캐시 — 서버 다운 시 유예 운전의 근거 (spec §5b).
    ///
    /// 서명 없는 평문 JSON 이다: 전환기(경고만)에는 위조 유인이 없고, 위조 위협은
    /// 시계 되돌리기와 같은 등급이라 spec §1 원칙(계약 관리, 최소 방어)에 따라
    /// 강제 모드 전환 시 서명 토큰 도입을 검토한다.
    /// VMS.Core 전용 — Web 서버로 복제하지 않는다 (클라이언트 측 개념).
    /// </summary>
    public sealed class SeatLeaseCache
    {
        public const string FileName = "license_lease.json";

        public string? LicenseId { get; init; }
        public string? Customer { get; init; }
        public int SeatsUsed { get; init; }
        public int MaxClients { get; init; }
        public bool OverCapacity { get; init; }
        /// <summary>이 시각까지 서버 미도달이어도 좌석 유효 (서버가 부여한 유예).</summary>
        public DateTime ValidUntilUtc { get; init; }
        public DateTime LeasedAtUtc { get; init; }
        public string ServerUrl { get; init; } = string.Empty;

        public static string DefaultPath => Path.Combine(AppDataPaths.Root, FileName);

        public bool IsValidAt(DateTime utcNow) => utcNow <= ValidUntilUtc;

        /// <summary>캐시 저장 — 실패는 무해 (다음 임대에서 재시도).</summary>
        public static void Save(SeatLeaseCache lease, string? path = null)
        {
            try
            {
                var target = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, JsonSerializer.Serialize(lease,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
                // best-effort — 캐시 없이는 서버 도달 시에만 좌석 확인
            }
        }

        /// <summary>캐시 로드 — 부재/손상 시 null.</summary>
        public static SeatLeaseCache? TryLoad(string? path = null)
        {
            try
            {
                var target = path ?? DefaultPath;
                if (!File.Exists(target)) return null;
                return JsonSerializer.Deserialize<SeatLeaseCache>(File.ReadAllText(target));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
