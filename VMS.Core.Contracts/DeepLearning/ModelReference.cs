using System;
using System.Globalization;

namespace VMS.Core.DeepLearning
{
    /// <summary>모델 참조가 가리키는 것 — 특정 버전 번호이거나, "지금 그 단계에 있는 것" 이다.</summary>
    public enum ModelReferenceKind
    {
        /// <summary>버전 번호를 못 박았다. 언제 풀어도 같은 파일이 나온다.</summary>
        Version,
        /// <summary>단계를 가리킨다. 승격·롤백이 일어나면 가리키는 파일이 바뀐다.</summary>
        Stage,
    }

    /// <summary>
    /// 검사 도구가 모델을 가리키는 방법 — <c>model://{modelId}@{version|stage}</c>.
    ///
    /// <para>
    /// 지금까지 도구는 <c>D:\models\best.onnx</c> 같은 절대 경로를 레시피에 그대로 담았다.
    /// 그래서 모델을 바꾸려면 사람이 파일을 라인 PC 마다 복사하고 경로를 다시 입력해야 했고,
    /// 어느 PC 가 어느 버전을 쓰는지도 알 수 없었다.
    /// 참조로 담으면 레시피는 "이 모델의 운영 버전" 이라고만 말하고, 실제 파일은 라인 PC 가
    /// 레지스트리에서 받아 캐시에 둔다. 롤백은 레지스트리에서 단계를 되돌리는 것으로 끝난다.
    /// </para>
    /// <para>
    /// 예: <c>model://3f2c…@7</c> (7번 버전 고정) · <c>model://3f2c…@production</c> (운영 단계)
    /// </para>
    /// <para>
    /// 이 형식은 MLOps 서버와 공유하는 규약이라 규약 패키지에 둔다.
    /// 참조를 실제 파일로 바꾸는 일(HTTP·캐시)은 여기서 하지 않는다 — 그것은 VMS.Core 의 몫이다.
    /// </para>
    /// </summary>
    public sealed class ModelReference : IEquatable<ModelReference>
    {
        public const string Scheme = "model://";

        private ModelReference(Guid modelId, ModelReferenceKind kind, int version, string stage)
        {
            ModelId = modelId;
            Kind = kind;
            Version = version;
            Stage = stage;
        }

        public Guid ModelId { get; }
        public ModelReferenceKind Kind { get; }

        /// <summary><see cref="ModelReferenceKind.Version"/> 일 때만 뜻이 있다.</summary>
        public int Version { get; }

        /// <summary><see cref="ModelReferenceKind.Stage"/> 일 때만 뜻이 있다 (production·staging·candidate).</summary>
        public string Stage { get; }

        /// <summary>이 문자열이 모델 참조처럼 생겼는가. 파일 경로와 가르는 데 쓴다.</summary>
        public static bool IsReference(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value!.TrimStart().StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

        public static ModelReference ForVersion(Guid modelId, int version)
        {
            if (modelId == Guid.Empty) throw new ArgumentException("모델 ID 가 비어 있습니다.", nameof(modelId));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version), "버전 번호는 1 이상입니다.");
            return new ModelReference(modelId, ModelReferenceKind.Version, version, string.Empty);
        }

        public static ModelReference ForStage(Guid modelId, string stage)
        {
            if (modelId == Guid.Empty) throw new ArgumentException("모델 ID 가 비어 있습니다.", nameof(modelId));
            var normalized = NormalizeStage(stage);
            if (normalized is null) throw new ArgumentException($"알 수 없는 단계입니다: {stage}", nameof(stage));
            return new ModelReference(modelId, ModelReferenceKind.Stage, 0, normalized);
        }

        /// <summary>파싱에 실패해도 예외를 던지지 않는다 — 레시피에는 사람이 손으로 넣은 값도 들어온다.</summary>
        public static bool TryParse(string? value, out ModelReference? reference)
        {
            reference = null;
            if (!IsReference(value)) return false;

            var body = value!.Trim().Substring(Scheme.Length);
            int at = body.LastIndexOf('@');
            if (at <= 0 || at == body.Length - 1) return false;

            if (!Guid.TryParse(body.Substring(0, at), out var modelId) || modelId == Guid.Empty) return false;
            var qualifier = body.Substring(at + 1).Trim();
            if (qualifier.Length == 0) return false;

            if (int.TryParse(qualifier, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            {
                if (version <= 0) return false;
                reference = new ModelReference(modelId, ModelReferenceKind.Version, version, string.Empty);
                return true;
            }

            var stage = NormalizeStage(qualifier);
            if (stage is null) return false;
            reference = new ModelReference(modelId, ModelReferenceKind.Stage, 0, stage);
            return true;
        }

        /// <summary>
        /// 레지스트리가 쓰는 소문자 단계 이름으로 맞춘다. 모르는 값이면 null.
        ///
        /// <para>
        /// 레지스트리의 단계는 candidate · staging · production · retired 네 가지지만,
        /// 레시피가 가리킬 수 있는 것은 앞의 셋뿐이다. retired 는 "이제 쓰지 말라" 는 뜻이라
        /// 라인이 그것을 가리키는 것 자체가 사고다. 여기서 막으면 레시피를 저장할 때 걸리고,
        /// 통과시키면 라인에서 검사가 시작될 때야 드러난다.
        /// </para>
        /// <para>
        /// 예전에 archived 를 받아 줬는데 레지스트리에 없는 이름이라 서버가 400 으로 거절했다.
        /// 클라이언트가 서버에 없는 단계를 만들어 내면 안 된다.
        /// </para>
        /// </summary>
        private static string? NormalizeStage(string? stage)
        {
            if (string.IsNullOrWhiteSpace(stage)) return null;
            switch (stage.Trim().ToLowerInvariant())
            {
                case "production": return "production";
                case "staging": return "staging";
                case "candidate": return "candidate";
                default: return null;
            }
        }

        /// <summary>참조 문자열. 레시피에 저장되는 형태다.</summary>
        public override string ToString() =>
            Kind == ModelReferenceKind.Version
                ? Scheme + ModelId.ToString("D") + "@" + Version.ToString(CultureInfo.InvariantCulture)
                : Scheme + ModelId.ToString("D") + "@" + Stage;

        /// <summary>사람에게 보여 줄 짧은 표기 — 화면에 GUID 전체를 늘어놓지 않기 위한 것.</summary>
        public string ToDisplayString() =>
            Kind == ModelReferenceKind.Version
                ? ModelId.ToString("D").Substring(0, 8) + " · v" + Version.ToString(CultureInfo.InvariantCulture)
                : ModelId.ToString("D").Substring(0, 8) + " · " + Stage;

        public bool Equals(ModelReference? other) =>
            other is not null && ModelId == other.ModelId && Kind == other.Kind
            && Version == other.Version
            && string.Equals(Stage, other.Stage, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as ModelReference);

        public override int GetHashCode() =>
            HashCode.Combine(ModelId, Kind, Version, Stage);
    }
}
