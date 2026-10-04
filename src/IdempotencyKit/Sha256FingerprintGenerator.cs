using System.Security.Cryptography;
using System.Text;

namespace IdempotencyKit;

public sealed class Sha256FingerprintGenerator : IIdempotencyFingerprintGenerator
{
    public string Generate(string method, string path, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(body);

        var prefix = $"{method.ToUpperInvariant()}\n{path}\n";
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var combined = new byte[prefixBytes.Length + body.Length];
        prefixBytes.CopyTo(combined, 0);
        body.CopyTo(combined, prefixBytes.Length);

        return Convert.ToHexStringLower(SHA256.HashData(combined));
    }
}
