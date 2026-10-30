using System;
using System.Collections.Generic;
using System.Linq;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 라이선스 확장 옵션 키 — license.lic payload.options 에 적는 값.
    /// 기본 구성(GS 인증 범위)에 없는 기능을 고객별로 켠다.
    /// </summary>
    public static class LicenseOptionKeys
    {
        /// <summary>3D 카메라 실촬영 · 촬영 파라미터 설정. 저장 점군(.vpc) 열기 · 3D 검사 도구는 기본 구성.</summary>
        public const string Camera3D = "3d-camera";

        /// <summary>로봇 연동 · 핸드아이 캘리브레이션 · 로봇 멀티뷰 스캔.</summary>
        public const string Robot = "robot";

        /// <summary>MLOps 서버 연동 (서버 주소 · 라인 토큰, 레지스트리 모델 선택).</summary>
        public const string MLOps = "mlops";

        public static IReadOnlyList<string> All { get; } = new[] { Camera3D, Robot, MLOps };

        /// <summary>발급 화면 · 상태 표시용 이름.</summary>
        public static string DisplayName(string key) => key switch
        {
            Camera3D => "3D 카메라 촬영",
            Robot => "로봇 연동 · 멀티뷰",
            MLOps => "MLOps 서버 연동",
            _ => key
        };
    }

    /// <summary>
    /// 이 PC 에서 켜진 확장 옵션 — 순수 값 객체 (파일 IO 없음, AppSetup · LicGen 에 Linked Compile).
    /// 서명이 유효한 라이선스가 options 를 적어 둔 경우에만 그 목록을 따르고,
    /// 그 밖(라이선스 없음 · 서명 불일치 · options 미지정 옛 발급분)은 전부 켠다 —
    /// 업데이트만으로 현장 기능이 사라지면 안 된다.
    /// </summary>
    public sealed class LicenseOptions
    {
        /// <summary>null = 전부 켬.</summary>
        private readonly HashSet<string>? _enabled;

        private LicenseOptions(HashSet<string>? enabled) => _enabled = enabled;

        public static LicenseOptions AllEnabled { get; } = new(null);

        public static LicenseOptions Of(IEnumerable<string> keys) =>
            new(new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase));

        public static LicenseOptions From(LicenseEvaluation? evaluation)
        {
#if DEBUG
            // 개발 빌드 전용 — 운영 라이선스를 건드리지 않고 기본 구성 화면을 확인 · 캡처한다.
            // VMS_LICENSE_OPTIONS=none (기본 구성) | 3d-camera,robot,mlops 중 일부. Release 빌드에는 없다.
            var simulated = Environment.GetEnvironmentVariable("VMS_LICENSE_OPTIONS");
            if (simulated != null)
            {
                return simulated.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? Of(Array.Empty<string>())
                    : Of(simulated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
#endif
            return evaluation?.Payload?.Options is { } keys ? Of(keys) : AllEnabled;
        }

        /// <summary>라이선스가 옵션을 지정하지 않아 전부 켜진 상태인가.</summary>
        public bool IsUnrestricted => _enabled == null;

        public bool Has(string key) => _enabled == null || _enabled.Contains(key);

        public bool Camera3D => Has(LicenseOptionKeys.Camera3D);
        public bool Robot => Has(LicenseOptionKeys.Robot);
        public bool MLOps => Has(LicenseOptionKeys.MLOps);

        /// <summary>상태 표시용 — "전부" 또는 켜진 옵션 이름 목록, 하나도 없으면 "기본 구성".</summary>
        public string Describe()
        {
            if (_enabled == null) return "확장 옵션 전부";
            var names = LicenseOptionKeys.All.Where(Has).Select(LicenseOptionKeys.DisplayName).ToList();
            return names.Count == 0 ? "기본 구성 (확장 옵션 없음)" : string.Join(" · ", names);
        }
    }
}
