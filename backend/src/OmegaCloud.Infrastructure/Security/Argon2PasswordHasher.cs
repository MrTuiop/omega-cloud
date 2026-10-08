using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Infrastructure.Security;

public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 3;
    private const int MemorySizeKb = 65536; // 64 MB
    private const int DegreeOfParallelism = 4;

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeArgon2id(password, salt, Iterations, MemorySizeKb, DegreeOfParallelism, HashSize);

        var saltBase64 = Convert.ToBase64String(salt);
        var hashBase64 = Convert.ToBase64String(hash);

        return $"$argon2id$v=19$m={MemorySizeKb},t={Iterations},p={DegreeOfParallelism}${saltBase64}${hashBase64}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            if (passwordHash.StartsWith("$argon2id$", StringComparison.OrdinalIgnoreCase))
            {
                var parts = passwordHash.Split('$');
                if (parts.Length != 6)
                {
                    return false;
                }

                // parts[0] is empty (leading $)
                // parts[1] is "argon2id"
                // parts[2] is "v=19"
                // parts[3] is "m=...,t=...,p=..."
                // parts[4] is saltBase64
                // parts[5] is hashBase64

                var paramParts = parts[3].Split(',');
                var m = MemorySizeKb;
                var t = Iterations;
                var p = DegreeOfParallelism;

                foreach (var param in paramParts)
                {
                    var kv = param.Split('=');
                    if (kv.Length == 2)
                    {
                        if (kv[0] == "m") int.TryParse(kv[1], out m);
                        else if (kv[0] == "t") int.TryParse(kv[1], out t);
                        else if (kv[0] == "p") int.TryParse(kv[1], out p);
                    }
                }

                var salt = Convert.FromBase64String(parts[4]);
                var expectedHash = Convert.FromBase64String(parts[5]);

                var computedHash = ComputeArgon2id(password, salt, t, m, p, expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
            }

            // Fallback for PBKDF2 hash format (e.g. $pbkdf2$iterations$salt$hash)
            if (passwordHash.StartsWith("$pbkdf2$", StringComparison.OrdinalIgnoreCase))
            {
                var parts = passwordHash.Split('$');
                if (parts.Length == 5)
                {
                    if (!int.TryParse(parts[2], out var iterations))
                    {
                        return false;
                    }
                    var salt = Convert.FromBase64String(parts[3]);
                    var expectedHash = Convert.FromBase64String(parts[4]);

                    var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.UTF8.GetBytes(password),
                        salt,
                        iterations,
                        HashAlgorithmName.SHA256,
                        expectedHash.Length);

                    return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeArgon2id(string password, byte[] salt, int iterations, int memorySizeKb, int parallelism, int outputLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memorySizeKb
        };

        return argon2.GetBytes(outputLength);
    }
}
