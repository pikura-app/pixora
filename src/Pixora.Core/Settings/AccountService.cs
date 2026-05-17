using System.Text.Json;

namespace Pixora.Core.Settings;

/// <summary>
/// Manages multiple Pixiv account profiles.
/// Profiles are persisted to <c>%APPDATA%\Pixora\accounts.json</c>.
/// Sensitive fields are encrypted via <see cref="CredentialStore"/>.
/// Switching accounts updates the active <see cref="SettingsService"/> session
/// so all downstream services pick up the new credentials immediately.
/// </summary>
public sealed class AccountService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly SettingsService _settings;
    private readonly Lock _gate = new();

    private List<AccountProfile> _profiles = new();

    public IReadOnlyList<AccountProfile> Profiles => _profiles;
    public AccountProfile? ActiveProfile { get; private set; }

    public event EventHandler? ProfilesChanged;
    public event EventHandler? ActiveProfileChanged;

    public AccountService(SettingsService settings, string? overridePath = null)
    {
        _settings = settings;
        _path = overridePath ?? DefaultPath();
        Load();
        SyncFromSettings();
    }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixora", "accounts.json");

    // ── Profile CRUD ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds or updates a profile matching the current settings session.
    /// Called after a successful login.
    /// </summary>
    public AccountProfile UpsertFromCurrentSession()
    {
        lock (_gate)
        {
            var s = _settings.Current;
            var existing = _profiles.FirstOrDefault(p => p.UserId == s.UserId);
            if (existing != null)
            {
                existing.UserName     = s.UserName;
                existing.PhpSessId    = s.PhpSessId;
                existing.RefreshToken = s.RefreshToken;
                existing.LastUsedAt   = DateTime.UtcNow;
                ActiveProfile         = existing;
            }
            else
            {
                var profile = new AccountProfile
                {
                    UserId       = s.UserId,
                    UserName     = s.UserName,
                    PhpSessId    = s.PhpSessId,
                    RefreshToken = s.RefreshToken,
                    LastUsedAt   = DateTime.UtcNow,
                };
                _profiles.Add(profile);
                ActiveProfile = profile;
            }
            Save();
        }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        return ActiveProfile!;
    }

    /// <summary>Switches the active account and updates the session in <see cref="SettingsService"/>.</summary>
    public void SwitchTo(string profileId)
    {
        AccountProfile? profile;
        lock (_gate)
        {
            profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null) return;
            ActiveProfile = profile;
            profile.LastUsedAt = DateTime.UtcNow;
            Save();
        }

        _settings.Update(s =>
        {
            s.PhpSessId    = profile.PhpSessId;
            s.RefreshToken = profile.RefreshToken;
            s.UserId       = profile.UserId;
            s.UserName     = profile.UserName;
            if (!string.IsNullOrWhiteSpace(profile.DownloadRoot))
                s.DownloadRoot = profile.DownloadRoot;
        });

        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a profile. If it was active, clears the current session.</summary>
    public void Remove(string profileId)
    {
        lock (_gate)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null) return;
            _profiles.Remove(profile);
            if (ActiveProfile?.Id == profileId)
            {
                ActiveProfile = _profiles.FirstOrDefault();
                if (ActiveProfile != null)
                    SwitchTo(ActiveProfile.Id);
                else
                    _settings.Update(s => { s.PhpSessId = string.Empty; s.UserId = null; s.UserName = null; });
            }
            Save();
        }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return;
            try
            {
                using var fs = File.OpenRead(_path);
                var loaded = JsonSerializer.Deserialize<List<AccountProfile>>(fs, JsonOptions) ?? new();
                foreach (var p in loaded)
                {
                    p.PhpSessId    = CredentialStore.Unprotect(p.PhpSessId);
                    p.RefreshToken = CredentialStore.Unprotect(p.RefreshToken);
                }
                _profiles = loaded;
            }
            catch { _profiles = new(); }
        }
    }

    private void Save()
    {
        var toWrite = _profiles.Select(p => new AccountProfile
        {
            Id           = p.Id,
            UserId       = p.UserId,
            UserName     = p.UserName,
            PhpSessId    = CredentialStore.Protect(p.PhpSessId),
            RefreshToken = CredentialStore.Protect(p.RefreshToken),
            DownloadRoot = p.DownloadRoot,
            CreatedAt    = p.CreatedAt,
            LastUsedAt   = p.LastUsedAt,
        }).ToList();

        using var fs = File.Create(_path);
        JsonSerializer.Serialize(fs, toWrite, JsonOptions);
    }

    /// <summary>
    /// On first run there may be an existing session in settings.json with no
    /// accounts.json yet — migrate it into a profile automatically.
    /// </summary>
    private void SyncFromSettings()
    {
        var s = _settings.Current;
        if (!s.IsConfigured) return;
        if (_profiles.Any(p => p.UserId == s.UserId)) return;

        var profile = new AccountProfile
        {
            UserId       = s.UserId,
            UserName     = s.UserName,
            PhpSessId    = s.PhpSessId,
            RefreshToken = s.RefreshToken,
            LastUsedAt   = DateTime.UtcNow,
        };
        lock (_gate)
        {
            _profiles.Add(profile);
            ActiveProfile = profile;
            Save();
        }
    }
}
