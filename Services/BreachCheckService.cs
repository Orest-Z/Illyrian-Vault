/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace IllyrianVault.Services;

public static class BreachCheckService
{
    private const string HibpRangeBase = "https://api.pwnedpasswords.com/range/";

    private static readonly HttpClient _client;

    static BreachCheckService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("IllyrianVault/1.0");
    }

    public record Result(bool IsBreached, bool IsNetworkUnavailable, int BreachCount, string Message);

    /// <summary>
    /// K-anonymity check: only the first 5 characters of the SHA-1 hash are sent over the wire.
    /// The full password and full hash never leave the device.
    /// </summary>
    public static async Task<Result> CheckPasswordAsync(string plaintextPassword)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
            return new Result(false, true, 0,
                "Cannot check for breaches. Please connect to the internet and try again.");

        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(plaintextPassword));
        var hash      = Convert.ToHexString(hashBytes);
        CryptographicOperations.ZeroMemory(hashBytes);
        var prefix    = hash[..5];
        var suffix    = hash[5..];

        string body = await _client.GetStringAsync(HibpRangeBase + prefix);

        // Response format per line: SUFFIX:COUNT  (suffix is 35 uppercase hex chars)
        foreach (var rawLine in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line  = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;

            var lineSuffix = line[..colon];
            if (!lineSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            if (!int.TryParse(line[(colon + 1)..], out var count)) continue;
            return new Result(true, false, count,
                $"Warning: This password has appeared in {count:N0} data breaches!");
        }

        return new Result(false, false, 0,
            "This password was not found in any known public data breaches.");
    }
}
