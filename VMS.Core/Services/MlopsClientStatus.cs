using System;
using System.Collections.Generic;

namespace VMS.Core.Services
{
    /// <summary>
    /// MLOps 연동 클라이언트(NG 수집 송신부·모델 레지스트리)가 만들어지지 못한 사유를 한 곳에 모은다.
    ///
    /// 왜: 이 클라이언트들은 시작 시점에 생성자에서 예외(대표적으로 <c>InsecureUrlGuard</c> 의 "Production 모드에서
    /// http:// 거부")를 내면 호출자가 삼키고 null 로 두어 기능만 조용히 빠졌다. 현장에서는 "설정은 다 맞는데
    /// NG 가 안 올라간다" 로만 보이고 큐 폴더조차 생기지 않아 원인을 찾을 수 없었다 (2026-09-10 실증).
    /// 여기 기록한 사유를 이미지 저장 설정 창 힌트와 메인 헤더 칩이 보여 준다.
    ///
    /// 정적인 이유: 생성 실패는 앱 시작(App.xaml.cs)과 Core 의 팩토리(ModelReferenceResolver)에서 일어나고,
    /// 읽는 쪽은 ViewModel 이다. DI 컨테이너가 없는 이 솔루션에서 셋을 잇는 가장 작은 경로다.
    /// </summary>
    public static class MlopsClientStatus
    {
        public const string NgCollector = "NG 이미지 수집";
        public const string ModelRegistry = "모델 레지스트리";

        private static readonly object Gate = new();
        private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal);

        /// <summary>구성 요소(예: "NG 이미지 수집", "모델 레지스트리")가 비활성인 사유를 기록한다. 같은 요소는 마지막 사유만 남긴다.</summary>
        public static void ReportDisabled(string component, Exception ex)
        {
            if (string.IsNullOrWhiteSpace(component) || ex is null) return;
            lock (Gate) Reasons[component] = Describe(ex);
        }

        /// <summary>정상 생성됐을 때 이전 사유를 지운다 (설정을 고쳐 재시작한 경우).</summary>
        public static void ReportEnabled(string component)
        {
            if (string.IsNullOrWhiteSpace(component)) return;
            lock (Gate) Reasons.Remove(component);
        }

        public static bool HasDisabled
        {
            get { lock (Gate) return Reasons.Count > 0; }
        }

        /// <summary>사람이 읽을 한 줄. 비활성 요소가 없으면 null.</summary>
        public static string? Summary
        {
            get
            {
                lock (Gate)
                {
                    if (Reasons.Count == 0) return null;
                    var parts = new List<string>(Reasons.Count);
                    foreach (var kv in Reasons) parts.Add($"{kv.Key}: {kv.Value}");
                    return string.Join(" / ", parts);
                }
            }
        }

        /// <summary>테스트용 초기화.</summary>
        public static void Reset()
        {
            lock (Gate) Reasons.Clear();
        }

        /// <summary>
        /// 예외를 현장 문구로. InsecureUrlGuard 의 메시지는 webServerUrl 을 가리키므로 MLOps 맥락의 조치를 덧붙인다.
        /// </summary>
        internal static string Describe(Exception ex)
        {
            var msg = ex.Message ?? ex.GetType().Name;
            if (msg.Contains("HTTPS", StringComparison.OrdinalIgnoreCase) && msg.Contains("Production", StringComparison.OrdinalIgnoreCase))
                return "보안 정책(Production 모드는 HTTPS 필수)이 http:// MLOps 서버 주소를 거부했습니다. " +
                       "MLOps 서버를 https:// 로 열거나, 사내 실증이면 설정 마법사(AppSetup)의 보안 모드를 Development 로 바꾼 뒤 VMS 를 다시 시작하세요.";
            return msg;
        }
    }
}
