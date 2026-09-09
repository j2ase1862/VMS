using System;
using System.IO;

namespace VMS.Core.Services
{
    /// <summary>
    /// 검사 결과에 함께 올리는 "이 판정을 한 모델" 식별자.
    ///
    /// <para><b>왜 이 클래스가 있는가.</b>
    /// 운영 웹의 <c>InspectionHistory.DlModelVersion</c> 은 <b>50자</b>까지만 받습니다
    /// (컬럼 제한이자 업로드 검증 규칙). 길이를 넘기면 FluentValidation 이 400 을 내고
    /// <b>검사 결과 업로드 전체가 거절됩니다</b> — 모델 이름만 잃는 것이 아니라 그 사이클의
    /// 측정값이 통째로 사라집니다.
    /// </para>
    /// <para>
    /// 예전 형식은 <c>{도구타입}:{파일명}</c> 이었습니다. 절대 경로에서는 짧았지만
    /// <c>model://</c> 참조가 들어오면서 넘치기 시작했습니다 —
    /// <c>DetectionTool:model://{36자 GUID}@production</c> 은 69자입니다.
    /// </para>
    /// <para><b>지금 규칙.</b>
    /// 참조를 풀어 실제 버전 id 를 알면 그것을 씁니다(<c>mv:{32자}</c> = 35자). 그 값만이
    /// MLOps 가 모델별 불량률을 집계할 때 이을 수 있는 키입니다. 알 수 없으면 사람이 읽을
    /// 힌트만 남기고 <see cref="MaxLength"/> 안으로 줄입니다 — 줄이더라도 업로드는 살아야 합니다.
    /// </para>
    /// </summary>
    public static class DlModelIdentity
    {
        /// <summary>운영 웹이 받는 상한. 이 값을 넘기면 업로드 전체가 거절된다.</summary>
        public const int MaxLength = 50;

        /// <summary>버전 id 로 만든 식별자임을 알리는 접두. MLOps 가 이 접두를 보고 이어 붙인다.</summary>
        public const string VersionPrefix = "mv:";

        /// <summary>
        /// 모델 버전 id 로 식별자를 만든다. <c>mv:{32자 16진}</c> — 35자.
        /// </summary>
        public static string ForVersion(Guid modelVersionId) => VersionPrefix + modelVersionId.ToString("N");

        /// <summary>
        /// <see cref="ForVersion"/> 이 만든 문자열에서 버전 id 를 되읽는다. 그 형식이 아니면 null.
        /// MLOps 쪽 집계가 쓴다.
        /// </summary>
        public static Guid? TryReadVersionId(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return null;
            var value = identity.Trim();
            if (!value.StartsWith(VersionPrefix, StringComparison.Ordinal)) return null;
            return Guid.TryParseExact(value[VersionPrefix.Length..], "N", out var id) ? id : null;
        }

        /// <summary>
        /// 버전 id 를 모를 때 쓰는 사람용 힌트. 상한을 넘으면 <b>뒤쪽을 남기고</b> 줄인다 —
        /// 앞은 도구 타입이라 어느 파일인지 가리는 것은 뒤쪽이다.
        /// </summary>
        /// <param name="toolType">예: DetectionTool</param>
        /// <param name="modelPath">모델 파일 경로 또는 참조 문자열</param>
        public static string? ForPath(string? toolType, string? modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath)) return null;

            var name = SafeFileName(modelPath!);
            var full = string.IsNullOrWhiteSpace(toolType) ? name : $"{toolType}:{name}";
            return Shorten(full);
        }

        /// <summary>
        /// 상한 안으로 줄인다. 넘칠 때는 앞을 잘라 내고 <c>…</c> 를 붙여 잘렸음을 남긴다 —
        /// 잘린 값이 온전한 이름처럼 보이면 나중에 그것으로 대조하려다 헛돈다.
        /// </summary>
        public static string Shorten(string value)
        {
            var v = (value ?? "").Trim();
            if (v.Length <= MaxLength) return v;
            return "…" + v[^(MaxLength - 1)..];
        }

        /// <summary>
        /// 경로에서 파일명만 뽑는다. <c>model://</c> 참조는 경로가 아니라 그대로 둔다 —
        /// Path.GetFileName 에 넣으면 <c>@production</c> 같은 꼬리만 남아 뜻이 사라진다.
        /// </summary>
        private static string SafeFileName(string modelPath)
        {
            var value = modelPath.Trim();
            if (value.StartsWith("model://", StringComparison.OrdinalIgnoreCase)) return value;

            try { return Path.GetFileName(value) is { Length: > 0 } name ? name : value; }
            catch (ArgumentException) { return value; }
        }
    }
}
