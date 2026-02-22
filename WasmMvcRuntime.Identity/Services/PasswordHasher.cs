using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;

namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// Password hasher service interface
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a password
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a password against a hash
    /// </summary>
    bool VerifyPassword(string hashedPassword, string providedPassword);
}

/// <summary>
/// Password hasher implementation using PBKDF2
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 128 / 8; // 16 bytes
    private const int KeySize = 256 / 8; // 32 bytes
    private const int Iterations = 100000;
    private static readonly HashAlgorithmName _hashAlgorithmName = HashAlgorithmName.SHA256;
    private const char Delimiter = ';';

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        // Generate random salt
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Hash password
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            _hashAlgorithmName,
            KeySize
        );

        // Return format: hash;salt;iterations
        return string.Join(
            Delimiter,
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            Iterations
        );
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
            throw new ArgumentNullException(nameof(hashedPassword));
        
        if (string.IsNullOrEmpty(providedPassword))
            return false;

        try
        {
            // Split stored hash
            var parts = hashedPassword.Split(Delimiter);
            if (parts.Length != 3)
                return false;

            var hash = Convert.FromBase64String(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var iterations = int.Parse(parts[2]);

            // Hash provided password with same salt
            var testHash = Rfc2898DeriveBytes.Pbkdf2(
                providedPassword,
                salt,
                iterations,
                _hashAlgorithmName,
                KeySize
            );

            // Compare hashes (constant-time comparison)
            return CryptographicOperations.FixedTimeEquals(hash, testHash);
        }
        catch
        {
            return false;
        }
    }
}
