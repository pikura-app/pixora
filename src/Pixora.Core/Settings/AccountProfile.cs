namespace Pixora.Core.Settings;

/// <summary>
/// Represents a single Pixiv account that has been signed into Pixora.
/// Sensitive fields (<see cref="PhpSessId"/>, <see cref="RefreshToken"/>) are
/// stored encrypted via <see cref="CredentialStore"/> — only plaintext values
/// are ever held in memory.
/// </summary>
public sealed class AccountProfile
{
    /// <summary>Stable identifier for this profile (generated once on creation).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Pixiv user ID resolved after successful login.</summary>
    public string? UserId { get; set; }

    /// <summary>Display name shown in the account switcher.</summary>
    public string? UserName { get; set; }

    /// <summary>Pixiv PHPSESSID cookie — plaintext in memory, encrypted on disk.</summary>
    public string PhpSessId { get; set; } = string.Empty;

    /// <summary>OAuth refresh token for App API — plaintext in memory, encrypted on disk.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Per-account download root folder. Null = use global setting.</summary>
    public string? DownloadRoot { get; set; }

    /// <summary>When this profile was first added.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this profile last signed in successfully.</summary>
    public DateTime? LastUsedAt { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PhpSessId);

    public string DisplayLabel => UserName ?? UserId ?? "Unknown account";
}
