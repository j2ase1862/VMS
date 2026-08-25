using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// license.lic 검증 + 상태 평가 — 순수 함수 (파일 IO / 시계 / 레지스트리 없음).
    /// 파일 로드는 LicenseFileStore, 머신 지문·현재 날짜는 호출자(LicenseBootCheck)가 주입.
    ///
    /// 검증 순서: 형식 → keyId 신뢰 → 서명 → 스키마 규칙 → 지문 → 만료 상태.
    /// 실패는 예외가 아니라 LicenseEvaluation 으로 반환 — 현장 무정지 원칙 (spec §1),
    /// 검증기 자체가 앱을 죽여서는 안 된다.
    /// </summary>
    public static class LicenseValidator
    {
        /// <summary>만료 전 경고 구간 (일) — spec §5.</summary>
        public const int WarnWindowDays = 30;
        /// <summary>구독 만료 후 유예 (일) — spec §5.</summary>
        public const int ExpiryGraceDays = 14;
        /// <summary>Internal 라이선스 최대 유효 기간 (일) — 유출 피해 최소화 (spec §3).</summary>
        public const int InternalMaxValidityDays = 90;

        public static LicenseEvaluation Validate(
            string licenseFileJson,
            string machineFingerprint,
            DateOnly today,
            IReadOnlyDictionary<string, string>? keyring = null)
        {
            keyring ??= LicenseKeyring.Default;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(licenseFileJson);
            }
            catch (JsonException ex)
            {
                return Invalid($"JSON 파싱 실패: {ex.Message}");
            }

            if (root is not JsonObject rootObj
                || rootObj["payload"] is not JsonObject payloadNode
                || rootObj["signature"] is not JsonValue signatureNode)
            {
                return Invalid("형식 오류 — payload/signature 필드 누락");
            }

            LicensePayload? payload;
            try
            {
                payload = payloadNode.Deserialize<LicensePayload>();
            }
            catch (JsonException ex)
            {
                return Invalid($"payload 역직렬화 실패: {ex.Message}");
            }
            if (payload == null) return Invalid("payload 비어 있음");

            if (payload.SchemaVersion != 1)
                return Invalid($"지원하지 않는 schemaVersion={payload.SchemaVersion} (SW 업데이트 필요)");

            if (!keyring.TryGetValue(payload.KeyId, out var publicKey))
                return Invalid($"신뢰하지 않는 서명 키 keyId='{payload.KeyId}'");

            // 서명은 payload 노드 원본(미지 필드 포함)의 canonical 바이트에 대해 검증 —
            // 신규 필드가 추가돼도 구버전 검증기에서 서명이 깨지지 않는다.
            var canonical = LicenseCanonicalJson.Serialize(payloadNode);
            if (!LicenseCrypto.Verify(publicKey, canonical, signatureNode.GetValue<string>()))
                return Invalid("서명 불일치 — 파일 변조 또는 잘못된 발급본");

            if (!Enum.TryParse<LicenseKind>(payload.Kind, ignoreCase: false, out var kind))
                return Invalid($"알 수 없는 kind='{payload.Kind}'");

            // kind 별 스키마 규칙 (발급 도구 실수에 대한 이중 방어)
            if (kind is LicenseKind.Internal or LicenseKind.Trial && payload.ExpiresAt == null)
                return Invalid($"{kind} 라이선스는 만료일(expiresAt) 필수");
            if (kind != LicenseKind.Internal && payload.Fingerprint == MachineFingerprint.Wildcard)
                return Invalid($"{kind} 라이선스는 지문 와일드카드 불가");
            if (payload.MaxClients < 1)
                return Invalid($"maxClients={payload.MaxClients} 는 유효하지 않음");

            if (!MachineFingerprint.Matches(payload.Fingerprint, machineFingerprint))
            {
                return new LicenseEvaluation
                {
                    Status = LicenseStatus.FingerprintMismatch,
                    Payload = payload,
                    Message = $"지문 불일치 — 라이선스 '{payload.Fingerprint}' vs 이 PC '{machineFingerprint}'. " +
                              "PC 교체/재구성 시 재발급 필요"
                };
            }

            return EvaluateDates(payload, today);
        }

        private static LicenseEvaluation EvaluateDates(LicensePayload payload, DateOnly today)
        {
            // 구독 만료 (expiresAt) — 실행 차단 축. 만료일 당일까지 유효.
            if (payload.ExpiresAt != null)
            {
                if (!TryParseDate(payload.ExpiresAt, out var expires))
                    return Invalid($"expiresAt 날짜 형식 오류: '{payload.ExpiresAt}'");

                if (today > expires.AddDays(ExpiryGraceDays))
                    return Result(LicenseStatus.Expired, payload,
                        $"라이선스 만료 ({payload.ExpiresAt}) + 유예 {ExpiryGraceDays}일 경과");
                if (today > expires)
                    return Result(LicenseStatus.ExpiredGrace, payload,
                        $"라이선스 만료 ({payload.ExpiresAt}) — 유예 종료 {expires.AddDays(ExpiryGraceDays):yyyy-MM-dd}, 갱신 필요");
                if (today > expires.AddDays(-WarnWindowDays))
                    return Result(LicenseStatus.Expiring, payload,
                        $"라이선스 만료 임박 ({payload.ExpiresAt})");
            }

            // 유지보수 만료 (maintenanceUntil) — 업데이트만 차단하는 소프트 축.
            if (payload.MaintenanceUntil != null)
            {
                if (!TryParseDate(payload.MaintenanceUntil, out var maintenance))
                    return Invalid($"maintenanceUntil 날짜 형식 오류: '{payload.MaintenanceUntil}'");

                if (today > maintenance)
                    return Result(LicenseStatus.MaintenanceExpired, payload,
                        $"유지보수 만료 ({payload.MaintenanceUntil}) — 실행 가능, SW 업데이트 제한");
                if (today > maintenance.AddDays(-WarnWindowDays))
                    return Result(LicenseStatus.MaintenanceExpiring, payload,
                        $"유지보수 만료 임박 ({payload.MaintenanceUntil})");
            }

            return Result(LicenseStatus.Valid, payload,
                $"{payload.Customer} / {payload.Edition} / 좌석 {payload.MaxClients} ({payload.LicenseId})");
        }

        private static bool TryParseDate(string value, out DateOnly date) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);

        private static LicenseEvaluation Invalid(string message) =>
            new() { Status = LicenseStatus.Invalid, Message = message };

        private static LicenseEvaluation Result(LicenseStatus status, LicensePayload payload, string message) =>
            new() { Status = status, Payload = payload, Message = message };
    }
}
