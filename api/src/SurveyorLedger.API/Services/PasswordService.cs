namespace SurveyorLedger.API.Services;

/// <summary>
/// Interface for password hashing and verification using BCrypt.
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// Hash a plaintext password using BCrypt.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verify a plaintext password against a BCrypt hash.
    /// </summary>
    bool VerifyPassword(string password, string hash);
}

/// <summary>
/// BCrypt-based password hashing service.
/// </summary>
public class PasswordService : IPasswordService
{
    private const int BcryptCost = 12;

    /// <summary>
    /// Hash a plaintext password using BCrypt with cost factor 12.
    /// </summary>
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BcryptCost);
    }

    /// <summary>
    /// Verify a plaintext password against a BCrypt hash.
    /// </summary>
    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // BCrypt.Verify throws on invalid hash format, return false on error
            return false;
        }
    }
}
