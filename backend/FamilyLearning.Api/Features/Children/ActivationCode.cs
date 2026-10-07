using System.Security.Cryptography;
using System.Text;

namespace FamilyLearning.Api.Features.Children;

/// <summary>
/// A device user code after RFC 8628 §6.1: eight letters from a vowel-free alphabet without look-alikes, shown as XXXX-XXXX and
/// read regardless of case, spaces or dashes, so a child can type it. Ten-minute expiry, single use and redemption rate limits
/// make guessing impractical; the stored SHA-256 hash keeps the code out of the database but does not resist offline guessing.
/// </summary>
internal static class ActivationCode
{
    private const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";
    private const int Length = 8;

    /// <summary>A new code for display, with the canonical letters its hash is taken from.</summary>
    public static (string Display, string Canonical) New()
    {
        var canonical = RandomNumberGenerator.GetString(Alphabet, Length);
        return ($"{canonical[..4]}-{canonical[4..]}", canonical);
    }

    /// <summary>The canonical letters of a typed code, or null when it cannot be a code.</summary>
    public static string? Normalize(string? typed)
    {
        if (typed is null || typed.Length > 4 * Length) return null;
        Span<char> letters = stackalloc char[Length];
        var count = 0;
        foreach (var character in typed)
        {
            if (character == '-' || char.IsWhiteSpace(character)) continue;
            var letter = char.ToUpperInvariant(character);
            if (count == Length || !Alphabet.Contains(letter)) return null;
            letters[count++] = letter;
        }
        return count == Length ? new string(letters) : null;
    }

    public static string Hash(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical)));
}
