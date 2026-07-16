using VMS.AppSetup.Models;

namespace VMS.AppSetup.Interfaces
{
    public interface IConfigurationService
    {
        string ConfigFilePath { get; }

        /// <summary>직전 LoadConfiguration 실패 원인. null = 성공 또는 파일 없음(정상).</summary>
        string? LastLoadError { get; }

        /// <summary>파싱 실패한 원본 설정 파일의 보존 경로.</summary>
        string InvalidBackupPath { get; }

        bool SaveConfiguration(SetupConfiguration config);
        SetupConfiguration? LoadConfiguration();
        bool ConfigurationExists();
        bool ExportConfiguration(SetupConfiguration config, string exportPath);
    }
}
