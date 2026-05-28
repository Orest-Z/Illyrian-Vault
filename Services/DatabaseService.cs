using System.IO;
using IllyriaVault.Models;
using Microsoft.Data.Sqlite;

namespace IllyriaVault.Services;

/// <summary>
/// All database I/O using SQLite + SQLCipher AES-256.
/// Java analogy: this is your DAO layer.
///   OpenAsync() ≈ DataSource.getConnection() with a JDBC URL
///   All CRUD    ≈ PreparedStatement + ResultSet mapping
///
/// Each user gets an isolated vault at:
///   %LOCALAPPDATA%\IllyriaVault\Profiles\{username}\vault.db
/// </summary>
public sealed class DatabaseService : IAsyncDisposable
{
    // ── Static profile helpers ─────────────────────────────────────────────────

    public static readonly string ProfilesRoot =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IllyriaVault", "Profiles");

    public static string GetProfileDir(string username) =>
        Path.Combine(ProfilesRoot, username);

    public static string GetDbPath(string username) =>
        Path.Combine(GetProfileDir(username), "vault.db");

    // Sidecar stores salt + verification hash so login can verify before opening the DB.
    public static string GetMetaPath(string username) =>
        Path.Combine(GetProfileDir(username), "vault.meta");

    public static bool ProfileExists(string username) =>
        !string.IsNullOrWhiteSpace(username) && Directory.Exists(GetProfileDir(username));

    public static bool AnyProfileExists() =>
        Directory.Exists(ProfilesRoot) && Directory.EnumerateDirectories(ProfilesRoot).Any();

    public static List<string> ListProfiles() =>
        Directory.Exists(ProfilesRoot)
            ? Directory.GetDirectories(ProfilesRoot).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToList()
            : [];

    // ── Instance state ─────────────────────────────────────────────────────────

    private string            _dbPath = string.Empty;
    private SqliteConnection? _conn;

    /// <summary>Must be called before OpenAsync/TryOpenAsync.</summary>
    public void SetProfile(string username) =>
        _dbPath = GetDbPath(username);

    public bool VaultExists => !string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public async Task OpenAsync(string hexKey)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Call SetProfile(username) before opening the database.");

        // Close any existing connection before opening a new one.
        // Without this, a stale connection from a prior session holds the WAL writer lock
        // and the new connection's first query throws SqliteException ("file is not a database").
        await DisposeAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        SQLitePCL.Batteries_V2.Init();

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate");
        await _conn.OpenAsync();

        // SQLCipher: key must be set as the very first command.
        // "x'hexstring'" = raw 256-bit key (skips SQLCipher's internal PBKDF2).
        await ExecAsync($"PRAGMA key = \"x'{hexKey}'\";");
        await ExecAsync("PRAGMA journal_mode = WAL;");
        await ExecAsync("PRAGMA foreign_keys = ON;");
        await ExecAsync("PRAGMA synchronous  = NORMAL;");

        await CreateSchemaAsync();
    }

    /// <summary>Opens the DB and runs a probe query. Returns false if the key is wrong.</summary>
    public async Task<bool> TryOpenAsync(string hexKey)
    {
        try
        {
            await OpenAsync(hexKey);
            await ExecAsync("SELECT count(*) FROM sqlite_master;");
            return true;
        }
        catch (SqliteException)
        {
            await DisposeAsync();
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null)
        {
            await _conn.CloseAsync();
            await _conn.DisposeAsync();
            _conn = null;
        }
    }

    // ── Schema ─────────────────────────────────────────────────────────────────

    private async Task CreateSchemaAsync()
    {
        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS Users (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                Username         TEXT NOT NULL DEFAULT '',
                DisplayName      TEXT NOT NULL DEFAULT 'Local Profile',
                PasswordSalt     BLOB NOT NULL,
                VerificationHash BLOB NOT NULL,
                RecoveryKeyHash  TEXT NOT NULL DEFAULT '',
                CreatedAt        TEXT NOT NULL
            );
            """);

        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS PasswordEntries (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId            INTEGER NOT NULL DEFAULT 1,
                Title             TEXT    NOT NULL,
                Username          TEXT    NOT NULL DEFAULT '',
                EncryptedPassword TEXT    NOT NULL DEFAULT '',
                Url               TEXT    NOT NULL DEFAULT '',
                Notes             TEXT    NOT NULL DEFAULT '',
                Category          TEXT    NOT NULL DEFAULT 'Login',
                IsFavorite        INTEGER NOT NULL DEFAULT 0,
                CreatedAt         TEXT    NOT NULL,
                UpdatedAt         TEXT    NOT NULL
            );
            """);

        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS PasswordHistory (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                EntryId           INTEGER NOT NULL REFERENCES PasswordEntries(Id) ON DELETE CASCADE,
                EncryptedPassword TEXT    NOT NULL DEFAULT '',
                CreatedAt         TEXT    NOT NULL
            );
            """);

        // Migrate older vaults that were created before Phase 2.
        try { await ExecAsync("ALTER TABLE Users ADD COLUMN Username TEXT NOT NULL DEFAULT '';"); }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
        try { await ExecAsync("ALTER TABLE PasswordEntries ADD COLUMN UserId INTEGER NOT NULL DEFAULT 1;"); }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
    }

    // ── User ───────────────────────────────────────────────────────────────────

    public async Task SaveUserAsync(VaultUser user)
    {
        await ExecAsync("""
            INSERT OR REPLACE INTO Users
                (Id, Username, DisplayName, PasswordSalt, VerificationHash, RecoveryKeyHash, CreatedAt)
            VALUES (1, @Username, @Name, @Salt, @Hash, @RecoveryHash, @Created);
            """,
            P("@Username",    user.Username),
            P("@Name",        user.DisplayName),
            P("@Salt",        user.PasswordSalt),
            P("@Hash",        user.VerificationHash),
            P("@RecoveryHash",user.RecoveryKeyHash),
            P("@Created",     Iso(user.CreatedAt)));
    }

    public async Task<VaultUser?> GetUserAsync()
    {
        await using var cmd    = Cmd("SELECT * FROM Users WHERE Id = 1;");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultUser
        {
            Id               = reader.GetInt64(reader.GetOrdinal("Id")),
            Username         = reader.GetString(reader.GetOrdinal("Username")),
            DisplayName      = reader.GetString(reader.GetOrdinal("DisplayName")),
            PasswordSalt     = (byte[])reader["PasswordSalt"],
            VerificationHash = (byte[])reader["VerificationHash"],
            RecoveryKeyHash  = reader.GetString(reader.GetOrdinal("RecoveryKeyHash")),
            CreatedAt        = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        };
    }

    // ── PasswordEntry CRUD ─────────────────────────────────────────────────────

    public async Task<List<PasswordEntry>> GetAllEntriesAsync(long userId = 1)
    {
        var list = new List<PasswordEntry>();
        await using var cmd    = Cmd("SELECT * FROM PasswordEntries WHERE UserId = @Uid ORDER BY UpdatedAt DESC;", P("@Uid", userId));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<long> InsertEntryAsync(PasswordEntry e)
    {
        e.CreatedAt = e.UpdatedAt = DateTime.UtcNow;
        await ExecAsync("""
            INSERT INTO PasswordEntries
                (UserId, Title, Username, EncryptedPassword, Url, Notes, Category, IsFavorite, CreatedAt, UpdatedAt)
            VALUES (@Uid, @Title, @User, @Pw, @Url, @Notes, @Cat, @Fav, @Created, @Updated);
            """,
            P("@Uid",     e.UserId),
            P("@Title",   e.Title),
            P("@User",    e.Username),
            P("@Pw",      e.EncryptedPassword),
            P("@Url",     e.Url),
            P("@Notes",   e.Notes),
            P("@Cat",     e.Category),
            P("@Fav",     e.IsFavorite ? 1 : 0),
            P("@Created", Iso(e.CreatedAt)),
            P("@Updated", Iso(e.UpdatedAt)));

        await using var lastId = Cmd("SELECT last_insert_rowid();");
        return (long)(await lastId.ExecuteScalarAsync() ?? 0L);
    }

    public async Task UpdateEntryAsync(PasswordEntry e)
    {
        e.UpdatedAt = DateTime.UtcNow;
        await ExecAsync("""
            UPDATE PasswordEntries SET
                Title = @Title, Username = @User, EncryptedPassword = @Pw,
                Url = @Url, Notes = @Notes, Category = @Cat,
                IsFavorite = @Fav, UpdatedAt = @Updated
            WHERE Id = @Id;
            """,
            P("@Id",      e.Id),
            P("@Title",   e.Title),
            P("@User",    e.Username),
            P("@Pw",      e.EncryptedPassword),
            P("@Url",     e.Url),
            P("@Notes",   e.Notes),
            P("@Cat",     e.Category),
            P("@Fav",     e.IsFavorite ? 1 : 0),
            P("@Updated", Iso(e.UpdatedAt)));
    }

    public async Task DeleteEntryAsync(long id) =>
        await ExecAsync("DELETE FROM PasswordEntries WHERE Id = @Id;", P("@Id", id));

    public async Task SetFavoriteAsync(long id, bool isFavorite) =>
        await ExecAsync(
            "UPDATE PasswordEntries SET IsFavorite = @Fav, UpdatedAt = @Now WHERE Id = @Id;",
            P("@Fav", isFavorite ? 1 : 0),
            P("@Now", Iso(DateTime.UtcNow)),
            P("@Id",  id));

    // ── PasswordHistory ────────────────────────────────────────────────────────

    public async Task InsertPasswordHistoryAsync(long entryId, string encryptedPassword)
    {
        await ExecAsync("""
            INSERT INTO PasswordHistory (EntryId, EncryptedPassword, CreatedAt)
            VALUES (@EntryId, @Pw, @Created);
            """,
            P("@EntryId", entryId),
            P("@Pw",      encryptedPassword),
            P("@Created", Iso(DateTime.UtcNow)));
    }

    public async Task<List<IllyriaVault.Models.PasswordHistory>> GetPasswordHistoryAsync(long entryId)
    {
        var list = new List<IllyriaVault.Models.PasswordHistory>();
        await using var cmd = Cmd(
            "SELECT * FROM PasswordHistory WHERE EntryId = @Id ORDER BY CreatedAt DESC;",
            P("@Id", entryId));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new IllyriaVault.Models.PasswordHistory
            {
                Id                = reader.GetInt64(reader.GetOrdinal("Id")),
                EntryId           = reader.GetInt64(reader.GetOrdinal("EntryId")),
                EncryptedPassword = reader.GetString(reader.GetOrdinal("EncryptedPassword")),
                CreatedAt         = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            });
        return list;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private SqliteCommand Cmd(string sql, params SqliteParameter[] ps)
    {
        var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in ps) cmd.Parameters.Add(p);
        return cmd;
    }

    private async Task ExecAsync(string sql, params SqliteParameter[] ps)
    {
        await using var cmd = Cmd(sql, ps);
        await cmd.ExecuteNonQueryAsync();
    }

    private static SqliteParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);

    private static string Iso(DateTime dt) => dt.ToString("O");

    private static PasswordEntry Map(SqliteDataReader r) => new()
    {
        Id                = r.GetInt64(r.GetOrdinal("Id")),
        UserId            = r.GetInt64(r.GetOrdinal("UserId")),
        Title             = r.GetString(r.GetOrdinal("Title")),
        Username          = r.GetString(r.GetOrdinal("Username")),
        EncryptedPassword = r.GetString(r.GetOrdinal("EncryptedPassword")),
        Url               = r.GetString(r.GetOrdinal("Url")),
        Notes             = r.GetString(r.GetOrdinal("Notes")),
        Category          = r.GetString(r.GetOrdinal("Category")),
        IsFavorite        = r.GetInt32(r.GetOrdinal("IsFavorite")) == 1,
        CreatedAt         = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        UpdatedAt         = DateTime.Parse(r.GetString(r.GetOrdinal("UpdatedAt"))),
    };
}
