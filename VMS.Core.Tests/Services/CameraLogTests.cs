using System;
using System.IO;
using VMS.Camera.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// CameraLog 파일 기록/회전 검증 — 현장(Release)에서 SDK 파라미터 적용 실패를
    /// 추적하는 유일한 수단이므로 기록 실패·무한 증식이 없어야 한다.
    ///
    /// PathOverrideForTests 정적 상태를 만지므로 클래스 내 직렬 실행에 의존하고
    /// 각 테스트가 finally 에서 복원한다.
    /// </summary>
    public class CameraLogTests
    {
        [Fact]
        public void Write_AppendsTimestampedLine()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vms-cameralog-" + Guid.NewGuid().ToString("N"));
            var logPath = Path.Combine(dir, "camera.log");
            CameraLog.PathOverrideForTests = logPath;
            try
            {
                CameraLog.Write("[Test] 첫 줄");
                CameraLog.Write("[Test] 둘째 줄");

                var lines = File.ReadAllLines(logPath);
                Assert.Equal(2, lines.Length);
                Assert.Contains("[Test] 첫 줄", lines[0]);
                Assert.Contains("[Test] 둘째 줄", lines[1]);
                // 타임스탬프 접두 (yyyy-MM-dd HH:mm:ss.fff)
                Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", lines[0]);
            }
            finally
            {
                CameraLog.PathOverrideForTests = null;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Write_RotatesWhenExceedingMaxSize()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vms-cameralog-" + Guid.NewGuid().ToString("N"));
            var logPath = Path.Combine(dir, "camera.log");
            CameraLog.PathOverrideForTests = logPath;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(logPath, new byte[1_000_001]);   // MaxBytes 초과 상태로 시작

                CameraLog.Write("[Test] 회전 후 첫 줄");

                Assert.True(File.Exists(logPath + ".old"), "회전된 .old 파일이 있어야 함");
                var lines = File.ReadAllLines(logPath);
                Assert.Single(lines);
                Assert.Contains("[Test] 회전 후 첫 줄", lines[0]);
            }
            finally
            {
                CameraLog.PathOverrideForTests = null;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Write_SwallowsIoErrors()
        {
            // 잘못된 경로(폴더를 파일 이름으로 사용 불가)여도 예외가 밖으로 새면 안 됨
            CameraLog.PathOverrideForTests = Path.Combine(Path.GetTempPath(), "\0invalid");
            try
            {
                CameraLog.Write("[Test] 실패해도 조용히");
            }
            finally
            {
                CameraLog.PathOverrideForTests = null;
            }
        }
    }
}
