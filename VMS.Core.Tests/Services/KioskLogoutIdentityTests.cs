using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 로그아웃 요청이 <b>내 세션임을 밝히는가</b> (Web W-005 의 VMS 쪽).
    ///
    /// <para>예전에는 본문에 라인 번호 하나만 담았다. 서버는 그것만 보고 해당 라인의 작업자
    /// 세션을 끝냈으므로, 라인 번호 0~99 를 훑는 것만으로 전 라인의 작업자를 반복
    /// 로그아웃시킬 수 있었다 — 그 뒤 올라오는 검사 이력의 작업자가 비어 추적성이 끊긴다.
    /// 이제 서버가 세션 id·사번을 현재 열린 세션과 대조하므로, 보내는 쪽이 그 값을 실어야 한다.</para>
    /// </summary>
    public class KioskLogoutIdentityTests
    {
        [Fact]
        public void Logout_request_carries_session_and_employee()
        {
            var svc = new OperatorAuthService("https://boda-vms.example", clientIndex: 3, clientApiKey: "k");
            SetCurrentSession(svc, new OperatorSessionDto
            {
                Id = 42,
                OperatorId = 7,
                EmployeeNumber = "E-100",
                ClientIndex = 3
            });

            var req = BuildLogoutRequest(svc, clientIndex: 3);

            Assert.Equal(3, req.ClientIndex);
            Assert.Equal(42, req.SessionId);
            Assert.Equal("E-100", req.EmployeeNumber);
        }

        [Fact]
        public void Logout_without_a_session_sends_no_identity()
        {
            var svc = new OperatorAuthService("https://boda-vms.example", clientIndex: 3, clientApiKey: "k");

            var req = BuildLogoutRequest(svc, clientIndex: 3);

            // 세션이 없으면 끝낼 것도 없다 — 서버는 204 를 준다.
            Assert.Null(req.SessionId);
            Assert.Null(req.EmployeeNumber);
        }

        [Fact]
        public void Identity_fields_are_sent_with_the_names_the_server_reads()
        {
            var json = JsonSerializer.Serialize(
                new KioskLogoutRequest { ClientIndex = 3, SessionId = 42, EmployeeNumber = "E-100" },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            Assert.Contains("\"sessionId\":42", json);
            Assert.Contains("\"employeeNumber\":\"E-100\"", json);
        }

        /// <summary>서비스가 실제로 쓰는 조립기를 그대로 부른다(테스트가 사본을 검증하지 않도록).</summary>
        private static KioskLogoutRequest BuildLogoutRequest(OperatorAuthService svc, int clientIndex)
            => OperatorAuthService.BuildLogoutRequest(clientIndex, svc.CurrentSession);

        private static void SetCurrentSession(OperatorAuthService svc, OperatorSessionDto session)
        {
            var prop = typeof(OperatorAuthService).GetProperty(
                nameof(OperatorAuthService.CurrentSession),
                BindingFlags.Instance | BindingFlags.Public);
            prop!.SetValue(svc, session);
        }
    }
}
