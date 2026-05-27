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
/// DB lives at: %LOCALAPPDATA%\IllyriaVault\vault.db
/// </summary>
public sealed class DatabaseService : IAsyncDisposable
{
    private static readonly string DbDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IllyriaVault");

    public static readonly string DbPath = Path.Combine(DbDirectory, "vault.db");

    // Sidecar file: stores salt + verification hash in plaintext so LoginViewModel
    // can verify the password BEFORE opening the encrypted DB.
    public static readonly string MetaPath = Path.ChangeExtension(DbPath, ".meta");

    private SqliteConnection? _conn;

    public bool VaultExists => File.Exists(DbPath) && File.Exists(MetaPath);

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public async Task OpenAsync(string hexKey)
    {
        Directory.CreateDirectory(DbDirectory);
        SQLitePCL.Batteries_V2.Init();

        _conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadWriteCreate");
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
    }

    // ── User ───────────────────────────────────────────────────────────────────

    public async Task SaveUserAsync(VaultUser user)
    {
        await ExecAsync("""
            INSERT OR REPLACE INTO Users
                (Id, DisplayName, PasswordSalt, VerificationHash, RecoveryKeyHash, CreatedAt)
            VALUES (1, @Name, @Salt, @Hash, @RecoveryHash, @Created);
            """,
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
            DisplayName      = reader.GetString(reader.GetOrdinal("DisplayName")),
            PasswordSalt     = (byte[])reader["PasswordSalt"],
            VerificationHash = (byte[])reader["VerificationHash"],
            RecoveryKeyHash  = reader.GetString(reader.GetOrdinal("RecoveryKeyHash")),
            CreatedAt        = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        };
    }

    // ── PasswordEntry CRUD ─────────────────────────────────────────────────────

    public async Task<List<PasswordEntry>> GetAllEntriesAsync()
    {
        var list = new List<PasswordEntry>();
        await using var cmd    = Cmd("SELECT * FROM PasswordEntries ORDER BY UpdatedAt DESC;");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(Map(reader));
        return list;
    }

    public async Task<long> InsertEntryAsync(PasswordEntry e)
    {
        e.CreatedAt = e.UpdatedAt = DateTime.UtcNow;
        await ExecAsync("""
            INSERT INTO PasswordEntries
                (Title, Username, EncryptedPassword, Url, Notes, Category, IsFavorite, CreatedAt, UpdatedAt)
            VALUES (@Title, @User, @Pw, @Url, @Notes, @Cat, @Fav, @Created, @Updated);
            """,
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
