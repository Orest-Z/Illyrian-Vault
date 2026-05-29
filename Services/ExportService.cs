/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.IO;
using System.Text;
using System.Text.Json;
using IllyrianVault.Models;

namespace IllyrianVault.Services;

public static class ExportService
{
    public static void ExportCsv(IEnumerable<(PasswordEntry Entry, string PlaintextPassword)> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Username,Password,URL,Notes,Category");
        foreach (var (e, pw) in rows)
            sb.AppendLine($"{Csv(e.Title)},{Csv(e.Username)},{Csv(pw)},{Csv(e.Url)},{Csv(e.Notes)},{Csv(e.Category)}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void ExportEncryptedJson(IEnumerable<PasswordEntry> entries, string path, byte[] key, EncryptionService crypto)
    {
        var json      = JsonSerializer.Serialize(entries.ToList());
        var encrypted = crypto.Encrypt(json, key);
        File.WriteAllText(path, encrypted, Encoding.UTF8);
    }

    private static string Csv(string v) => $"\"{v.Replace("\"", "\"\"")}\"";
}
