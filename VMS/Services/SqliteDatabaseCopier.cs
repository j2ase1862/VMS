using Microsoft.Data.Sqlite;

namespace VMS.Services
{
    /// <summary>
    /// SQLite 온라인 백업으로 DB 를 복사한다 — VMS 가 같은 DB 연결을 열어 둔 상태에서도
    /// 일관된 사본을 만들고, 복원 때는 열린 DB 안으로 내용을 덮어쓴다.
    /// <see cref="VMS.Core.Backup.BackupRestoreService.DatabaseCopier"/> 로 등록해 쓴다.
    /// </summary>
    public static class SqliteDatabaseCopier
    {
        /// <summary>다른 연결이 쓰는 중이면 기다리는 최대 시간 (ms).</summary>
        private const int BusyTimeoutMs = 10000;

        public static void Copy(string sourcePath, string destPath)
        {
            var sourceCs = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            var destCs = new SqliteConnectionStringBuilder
            {
                DataSource = destPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            using var source = new SqliteConnection(sourceCs);
            using var dest = new SqliteConnection(destCs);
            source.Open();
            dest.Open();
            SetBusyTimeout(source);
            SetBusyTimeout(dest);
            source.BackupDatabase(dest);
        }

        private static void SetBusyTimeout(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMs};";
            cmd.ExecuteNonQuery();
        }
    }
}
