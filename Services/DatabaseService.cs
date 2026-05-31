/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.IO;
using IllyrianVault.Models;
using Microsoft.Data.Sqlite;

namespace IllyrianVault.Services;

/// <summary>
/// All database I/O using SQLite + SQLCipher AES-256.
/// Java analogy: this is your DAO layer.
///   OpenAsync() ≈ DataSource.getConnection() with a JDBC URL
///   All CRUD    ≈ PreparedStatement + ResultSet mapping
///
/// Each user gets an isolated vault at:
///   %LOCALAPPDATA%\IllyrianVault\Profiles\{username}\vault.db
/// </summary>
public sealed class DatabaseService : IAsyncDisposable
{
    // ── Static profile helpers ─────────────────────────────────────────────────

    public static readonly string ProfilesRoot =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IllyriaVault", "Profiles");

    /// <summary>
    /// Only alphanumerics, underscores, and hyphens are allowed in a username.
    /// This prevents path-traversal attacks where a crafted username like
    /// "../../../Windows/System32/evil" escapes the Profiles directory.
    /// </summary>
    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username) &&
        username.Length <= 64 &&
        System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9_\-]+$");

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

    /// <summary>
    /// Opens and configures the SQLCipher database using raw 32-byte key material.
    ///
    /// The rawKey byte[] is converted to a hex string ONLY for the PRAGMA command
    /// and in the narrowest possible scope — the local variable becomes GC-eligible
    /// the moment ExecAsync returns. The string itself cannot be zeroed (managed
    /// String is immutable) but it never leaves this method's stack frame.
    ///
    /// PRAGMA EXECUTION ORDER (matters — must precede any schema operations):
    ///   1. key                      — unlocks the cipher layer.
    ///   2. cipher_page_size         — 4096 bytes: larger pages mean fewer total
    ///                                 encrypted blocks and better I/O throughput.
    ///                                 Must be set before the first table access.
    ///   3. cipher_hmac_algorithm    — SHA-512 HMAC for page authentication.
    ///                                 SHA-256 is the SQLCipher default; SHA-512
    ///                                 is stronger and still faster than AES-GCM
    ///                                 authenticated encryption on most hardware.
    ///   4. cipher_kdf_algorithm     — not used (we pass a raw x'hex' key, so
    ///                                 SQLCipher's internal KDF is bypassed), but
    ///                                 set explicitly so the cipher metadata block
    ///                                 written to the DB header is consistent.
    ///   5. cipher_memory_security   — ON: SQLCipher zeroes its internal page
    ///                                 cache and key schedule before returning
    ///                                 memory to the allocator.
    ///                                 Trade-off: adds ~5–10 % overhead on
    ///                                 page read/write. Acceptable for a password
    ///                                 manager with a small DB (typically < 1 MB).
    ///   6. cipher_plaintext_header_size = 0 — no unencrypted header bytes.
    /// </summary>
    public async Task OpenAsync(byte[] rawKey)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Call SetProfile(username) before opening the database.");

        // Close any existing connection before opening a new one.
        // Without this, a stale connection from a prior session holds the WAL writer lock
        // and the new connection's first query throws SqliteException ("file is not a database").
        await DisposeAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate");
        await _conn.OpenAsync();

        // Build the key PRAGMA in the smallest possible scope. The string is
        // GC-eligible as soon as ExecAsync returns. Never store it in a field.
        await ExecAsync(SecureMemory.BuildSqlCipherKeyPragma(rawKey));

        // Hardened cipher configuration — all must precede CreateSchemaAsync.
        await ExecAsync("PRAGMA cipher_page_size          = 4096;");
        await ExecAsync("PRAGMA cipher_hmac_algorithm     = HMAC_SHA512;");
        await ExecAsync("PRAGMA cipher_kdf_algorithm      = PBKDF2_HMAC_SHA512;");
        await ExecAsync("PRAGMA cipher_memory_security    = ON;");
        await ExecAsync("PRAGMA cipher_plaintext_header_size = 0;");

        await ExecAsync("PRAGMA journal_mode = WAL;");
        await ExecAsync("PRAGMA foreign_keys = ON;");
        await ExecAsync("PRAGMA synchronous  = NORMAL;");

        await CreateSchemaAsync();
    }

    /// <summary>Opens the DB and runs a probe query. Returns false if the key is wrong.</summary>
    public async Task<bool> TryOpenAsync(byte[] rawKey)
    {
        try
        {
            await OpenAsync(rawKey);
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

    /// <summary>
    /// Re-encrypts the SQLCipher database with a new raw 32-byte key.
    /// Must be called while the connection is open with the OLD key.
    /// </summary>
    public async Task RekeyAsync(byte[] newKey)
    {
        await ExecAsync(SecureMemory.BuildSqlCipherRekeyPragma(newKey));
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
                UpdatedAt         TEXT    NOT NULL,
                EncryptedPayload  TEXT    NOT NULL DEFAULT ''
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
        try { await ExecAsync("ALTER TABLE PasswordEntries ADD COLUMN EncryptedPayload TEXT NOT NULL DEFAULT '';"); }
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
                (UserId, Title, Username, EncryptedPassword, Url, Notes, Category, IsFavorite, CreatedAt, UpdatedAt, EncryptedPayload)
            VALUES (@Uid, @Title, @User, @Pw, @Url, @Notes, @Cat, @Fav, @Created, @Updated, @Payload);
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
            P("@Updated", Iso(e.UpdatedAt)),
            P("@Payload", e.EncryptedPayload));

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
                IsFavorite = @Fav, UpdatedAt = @Updated, EncryptedPayload = @Payload
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
            P("@Updated", Iso(e.UpdatedAt)),
            P("@Payload", e.EncryptedPayload));
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

    public async Task<List<IllyrianVault.Models.PasswordHistory>> GetPasswordHistoryAsync(long entryId)
    {
        var list = new List<IllyrianVault.Models.PasswordHistory>();
        await using var cmd = Cmd(
            "SELECT * FROM PasswordHistory WHERE EntryId = @Id ORDER BY CreatedAt DESC;",
            P("@Id", entryId));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new IllyrianVault.Models.PasswordHistory
            {
                Id                = reader.GetInt64(reader.GetOrdinal("Id")),
                EntryId           = reader.GetInt64(reader.GetOrdinal("EntryId")),
                EncryptedPassword = reader.GetString(reader.GetOrdinal("EncryptedPassword")),
                CreatedAt         = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            });
        return list;
    }

    public async Task<List<IllyrianVault.Models.PasswordHistory>> GetAllPasswordHistoryAsync()
    {
        var list = new List<IllyrianVault.Models.PasswordHistory>();
        await using var cmd    = Cmd("SELECT * FROM PasswordHistory;");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new IllyrianVault.Models.PasswordHistory
            {
                Id                = reader.GetInt64(reader.GetOrdinal("Id")),
                EntryId           = reader.GetInt64(reader.GetOrdinal("EntryId")),
                EncryptedPassword = reader.GetString(reader.GetOrdinal("EncryptedPassword")),
                CreatedAt         = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            });
        return list;
    }

    public async Task UpdatePasswordHistoryEncryptedValueAsync(long id, string encryptedPassword)
    {
        await ExecAsync(
            "UPDATE PasswordHistory SET EncryptedPassword = @Pw WHERE Id = @Id;",
            P("@Pw", encryptedPassword),
            P("@Id", id));
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
        EncryptedPayload  = r.GetString(r.GetOrdinal("EncryptedPayload")),
    };
}
