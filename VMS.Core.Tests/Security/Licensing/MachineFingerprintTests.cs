using System.Text.RegularExpressions;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// MachineFingerprint — 코드 형식 / 결정성 / 세그먼트 독립성 / 2-of-3 비교 규칙 (spec §4).
    /// 실제 레지스트리·NIC 는 건드리지 않고 BuildCode(internal) 로 요소를 주입.
    /// </summary>
    public class MachineFingerprintTests
    {
        private const string Guid1 = "9f0c2a7e-1111-2222-3333-444455556666";
        private const string Mac1 = "00155D010203";
        private const string Cpu1 = "GenuineIntel|Intel64 Family 6|Core i7-12700";

        [Fact]
        public void Code_format_is_three_crockford_segments()
        {
            var code = MachineFingerprint.BuildCode(Guid1, Mac1, Cpu1);
            // Crockford Base32 — 0/O·1/I 혼동 문자(I, L, O, U) 제외
            Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$"), code);
        }

        [Fact]
        public void Code_is_deterministic_and_case_insensitive_on_input()
        {
            Assert.Equal(
                MachineFingerprint.BuildCode(Guid1, Mac1, Cpu1),
                MachineFingerprint.BuildCode(Guid1.ToUpperInvariant(), Mac1.ToLowerInvariant(), Cpu1));
        }

        [Fact]
        public void Changing_one_component_changes_only_its_segment()
        {
            var baseline = MachineFingerprint.BuildCode(Guid1, Mac1, Cpu1).Split('-');
            var nicSwapped = MachineFingerprint.BuildCode(Guid1, "AABBCCDDEEFF", Cpu1).Split('-');

            Assert.Equal(baseline[0], nicSwapped[0]);
            Assert.NotEqual(baseline[1], nicSwapped[1]);
            Assert.Equal(baseline[2], nicSwapped[2]);
        }

        [Fact]
        public void Unavailable_component_is_deterministic()
        {
            Assert.Equal(
                MachineFingerprint.BuildCode(Guid1, null, Cpu1),
                MachineFingerprint.BuildCode(Guid1, "  ", Cpu1));
        }

        [Theory]
        [InlineData("AAAAA-BBBBB-CCCCC", "AAAAA-BBBBB-CCCCC", true)]   // 3/3
        [InlineData("AAAAA-BBBBB-CCCCC", "AAAAA-BBBBB-ZZZZZ", true)]   // 2/3 — NIC 교체 관용
        [InlineData("AAAAA-BBBBB-CCCCC", "ZZZZZ-BBBBB-CCCCC", true)]   // 2/3 — 재설치 관용
        [InlineData("AAAAA-BBBBB-CCCCC", "AAAAA-YYYYY-ZZZZZ", false)]  // 1/3
        [InlineData("AAAAA-BBBBB-CCCCC", "XXXXX-YYYYY-ZZZZZ", false)]  // 0/3
        [InlineData("AAAAA-BBBBB-CCCCC", "aaaaa-bbbbb-ccccc", true)]   // 대소문자 무시
        [InlineData("AAAAA-BBBBB-CCCCC", "AAAAA-BBBBB", false)]        // 형식 불일치
        [InlineData("AAAAA-BBBBB-CCCCC", "AAAA-BBBBB-CCCCCC", false)]  // 세그먼트 길이 불일치
        public void Match_requires_two_of_three_segments(string license, string machine, bool expected)
        {
            Assert.Equal(expected, MachineFingerprint.Matches(license, machine));
        }

        [Fact]
        public void Wildcard_matches_anything()
        {
            Assert.True(MachineFingerprint.Matches("*", "AAAAA-BBBBB-CCCCC"));
        }

        [Fact]
        public void GetCode_on_real_machine_returns_wellformed_code()
        {
            // 실제 수집 경로 스모크 — CI 러너에서도 레지스트리/NIC 일부가 없을 수 있으나
            // 실패 요소는 마커 해시로 대체되므로 형식은 항상 보장되어야 한다.
            var code = MachineFingerprint.GetCode();
            Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$"), code);
            Assert.Equal(code, MachineFingerprint.GetCode());  // 캐시 안정성
        }
    }
}
