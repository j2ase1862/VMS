using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Core.Security;
using VMS.Core.Services;
using VMS.Interfaces;
using VMS.Services;

namespace VMS.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private readonly WebSsoConfig _ssoConfig;
        private readonly Func<string, WebAuthClient> _clientFactory;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isAuthenticated;

        public string Password { get; set; } = string.Empty;

        /// <summary>UI 가 "SSO 모드 활성" 안내 표시할 수 있도록 노출.</summary>
        public bool IsWebSsoEnabled => _ssoConfig.Enabled;

        /// <summary>운영 호출 — WebSsoConfig.LoadFromAppData() + 기본 WebAuthClient.</summary>
        public LoginViewModel(IUserService userService)
            : this(userService, WebSsoConfig.LoadFromAppData(), url => new WebAuthClient(url))
        {
        }

        /// <summary>
        /// 테스트 전용 — WebSsoConfig + WebAuthClient factory 명시 주입.
        /// factory 는 URL → WebAuthClient 변환. 테스트에서 StubHandler 기반 HttpClient 주입 가능.
        /// </summary>
        internal LoginViewModel(IUserService userService, WebSsoConfig ssoConfig,
            Func<string, WebAuthClient> clientFactory)
        {
            _userService = userService;
            _ssoConfig = ssoConfig;
            _clientFactory = clientFactory;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task LoginAsync()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "Username을 입력하세요.";
                return;
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Password를 입력하세요.";
                return;
            }

            // SSO Migration Plan §2.2 — 활성 시 Web 위임, 도달 불가시 local-admin 폴백만 허용.
            if (_ssoConfig.Enabled && !string.IsNullOrWhiteSpace(_ssoConfig.WebServerUrl))
            {
                // 비상 폴백: username 이 local-admin 이면 우회해 로컬 인증 시도 (Web 미호출).
                // 기타 username 은 Web 으로만 — Web 도달 불가 시 안내 메시지.
                if (string.Equals(Username, UserService.LocalFallbackUsername, StringComparison.OrdinalIgnoreCase))
                {
                    if (_userService.Authenticate(Username, Password))
                    {
                        IsAuthenticated = true;
                    }
                    else
                    {
                        ErrorMessage = "사용자명 또는 비밀번호가 올바르지 않습니다.";
                    }
                    return;
                }

                WebAuthClient client;
                try
                {
                    client = _clientFactory(_ssoConfig.WebServerUrl);
                }
                catch (InvalidOperationException ex)
                {
                    // InsecureUrlGuard — Production 모드에서 원격 http:// Web URL 거부.
                    // 자격증명 평문 송신을 막은 것이므로 로그인 실패로 표시하고 설정 안내.
                    ErrorMessage = $"보안 정책으로 Web 로그인이 차단되었습니다. {ex.Message}";
                    return;
                }

                using (client)
                {
                    if (await _userService.AuthenticateViaWebAsync(Username, Password, client))
                    {
                        IsAuthenticated = true;
                        return;
                    }
                }

                // Web 거부 / 도달 불가 — 일반 사용자에게는 폴백 미허용. 안내 메시지로 구분.
                ErrorMessage = "Web 인증에 실패했습니다. " +
                               $"네트워크 단절 시에는 비상 계정 '{UserService.LocalFallbackUsername}' 만 로그인 가능합니다.";
                return;
            }

            // SSO 비활성 또는 URL 미설정 — 기존 로컬 인증 경로
            if (_userService.Authenticate(Username, Password))
            {
                IsAuthenticated = true;
            }
            else
            {
                ErrorMessage = "사용자명 또는 비밀번호가 올바르지 않습니다.";
            }
        }
    }
}
