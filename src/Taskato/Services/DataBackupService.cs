using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SQLite;

namespace Taskato.Services
{
    /// <summary>
    /// 负责创建可迁移备份，并把恢复操作安全地延迟到下次数据库连接建立之前。
    /// </summary>
    public sealed class DataBackupService
    {
        private const string DatabaseEntryName = "taskato.db";
        private const string SettingsEntryName = "settings.json";
        private const string ManifestEntryName = "manifest.json";
        private const string PendingDirectoryName = "RestorePending";

        private readonly DatabaseService _database;
        private readonly SettingsService _settings;

        public DataBackupService(DatabaseService database, SettingsService settings)
        {
            _database = database;
            _settings = settings;
        }

        public async Task CreateArchiveAsync(string destinationPath)
        {
            var fullDestinationPath = Path.GetFullPath(destinationPath);
            var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
                ?? throw new InvalidOperationException("备份目标目录无效。");
            Directory.CreateDirectory(destinationDirectory);
            var temporaryArchivePath =
                $"{fullDestinationPath}.tmp-{Guid.NewGuid():N}";
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"TaskatoBackup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                var databaseSnapshotPath = Path.Combine(temporaryDirectory, DatabaseEntryName);
                await _database.CreateBackupCopyAsync(databaseSnapshotPath);

                if (File.Exists(_settings.ConfigPath))
                {
                    File.Copy(
                        _settings.ConfigPath,
                        Path.Combine(temporaryDirectory, SettingsEntryName),
                        overwrite: true);
                }

                var manifest = new BackupManifest(
                    FormatVersion: 1,
                    CreatedAt: DateTimeOffset.Now,
                    AppVersion: System.Reflection.Assembly.GetEntryAssembly()
                        ?.GetName().Version?.ToString(3) ?? "1.0.0");
                await File.WriteAllTextAsync(
                    Path.Combine(temporaryDirectory, ManifestEntryName),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                ZipFile.CreateFromDirectory(
                    temporaryDirectory,
                    temporaryArchivePath,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);
                // 先完整生成临时归档，再一次性替换目标，失败时不会破坏用户已有备份。
                File.Move(temporaryArchivePath, fullDestinationPath, overwrite: true);
            }
            finally
            {
                // 只删除本方法创建、名称带随机 GUID 的临时目录，不接触用户数据目录。
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
                if (File.Exists(temporaryArchivePath))
                    File.Delete(temporaryArchivePath);
            }
        }

        public static async Task StageRestoreAsync(string archivePath, string dataDirectory)
        {
            var pendingDirectory = Path.Combine(dataDirectory, PendingDirectoryName);
            var stagingDirectory = Path.Combine(
                dataDirectory,
                $"RestoreStaging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var allowedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DatabaseEntryName,
                    SettingsEntryName,
                    ManifestEntryName
                };
                foreach (var entry in archive.Entries)
                {
                    if (!allowedEntries.Contains(entry.FullName) ||
                        entry.FullName.Contains('/') ||
                        entry.FullName.Contains('\\'))
                    {
                        throw new InvalidDataException("备份包包含不支持的文件。");
                    }
                    var maximumLength = entry.FullName.Equals(DatabaseEntryName, StringComparison.OrdinalIgnoreCase)
                        ? 512L * 1024 * 1024
                        : entry.FullName.Equals(SettingsEntryName, StringComparison.OrdinalIgnoreCase)
                            ? 2L * 1024 * 1024
                            : 1024L * 1024;
                    if (entry.Length > maximumLength)
                        throw new InvalidDataException($"备份包中的 {entry.FullName} 超出安全大小限制。");
                    entry.ExtractToFile(Path.Combine(stagingDirectory, entry.FullName), overwrite: true);
                }

                var databasePath = Path.Combine(stagingDirectory, DatabaseEntryName);
                var manifestPath = Path.Combine(stagingDirectory, ManifestEntryName);
                if (!File.Exists(databasePath) || !File.Exists(manifestPath))
                    throw new InvalidDataException("备份包缺少数据库或清单文件。");

                var manifest = JsonSerializer.Deserialize<BackupManifest>(
                    await File.ReadAllTextAsync(manifestPath));
                if (manifest?.FormatVersion != 1)
                    throw new InvalidDataException("备份格式版本不受支持。");
                ValidateDatabase(databasePath);

                if (Directory.Exists(pendingDirectory))
                    Directory.Delete(pendingDirectory, recursive: true);
                Directory.Move(stagingDirectory, pendingDirectory);
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
        }

        public static void ApplyPendingRestore(string dataDirectory)
        {
            var pendingDirectory = Path.Combine(dataDirectory, PendingDirectoryName);
            if (!Directory.Exists(pendingDirectory)) return;

            var pendingDatabasePath = Path.Combine(pendingDirectory, DatabaseEntryName);
            var pendingSettingsPath = Path.Combine(pendingDirectory, SettingsEntryName);
            ValidateDatabase(pendingDatabasePath);

            var rollbackDirectory = Path.Combine(
                dataDirectory,
                $"RestoreRollback-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rollbackDirectory);
            CopyIfExists(
                Path.Combine(dataDirectory, DatabaseEntryName),
                Path.Combine(rollbackDirectory, DatabaseEntryName));
            CopyIfExists(
                Path.Combine(dataDirectory, SettingsEntryName),
                Path.Combine(rollbackDirectory, SettingsEntryName));

            File.Copy(
                pendingDatabasePath,
                Path.Combine(dataDirectory, DatabaseEntryName),
                overwrite: true);
            if (File.Exists(pendingSettingsPath))
            {
                File.Copy(
                    pendingSettingsPath,
                    Path.Combine(dataDirectory, SettingsEntryName),
                    overwrite: true);
            }

            Directory.Delete(pendingDirectory, recursive: true);
        }

        private static void ValidateDatabase(string databasePath)
        {
            if (!File.Exists(databasePath))
                throw new InvalidDataException("备份数据库不存在。");

            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly);
            var integrityResult = connection.ExecuteScalar<string>("PRAGMA integrity_check");
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"备份数据库完整性检查失败：{integrityResult}");
        }

        private static void CopyIfExists(string sourcePath, string destinationPath)
        {
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private sealed record BackupManifest(
            int FormatVersion,
            DateTimeOffset CreatedAt,
            string AppVersion);
    }
}
