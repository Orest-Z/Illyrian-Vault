using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace IllyrianVault.Services;

public static class BreachCheckService
{
    private const string HibpRangeBase = "https://api.pwnedpasswords.com/range/";

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

        // SHA-1 of the plaintext → 40-char uppercase hex string.
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(plaintextPassword));
        var hash      = Convert.ToHexString(hashBytes);   // already uppercase
        var prefix    = hash[..5];                         // sent to HIBP
        var suffix    = hash[5..];                         // stays local, used only for comparison

        string body;
        using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IllyrianVault/1.0");
            body = await client.GetStringAsync(HibpRangeBase + prefix);
        }

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
