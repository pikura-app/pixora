using System.Text.Json;
using Pixora.Core.Models;

namespace Pixora.Core.Services;

/// <summary>
/// Persists a local "favorites" list of artworks to a JSON file in %APPDATA%\Pixora.
/// This is entirely app-side — no Pixiv API involved.
/// </summary>
public sealed class LocalFavoritesService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Lock _gate = new();
    private List<LocalFavoriteEntry> _entries = [];

    public event EventHandler? Changed;

    public LocalFavoritesService(string? overridePath = null)
    {
        _path = overridePath ?? DefaultPath();
        Load();
    }

    public static string DefaultPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixora");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "local_favorites.json");
    }

    public IReadOnlyList<LocalFavoriteEntry> GetAll()
    {
        lock (_gate) return _entries.ToList();
    }

    public string? GetFolder(string artworkId)
    {
        lock (_gate) return _entries.FirstOrDefault(e => e.Id == artworkId)?.FolderName;
    }

    public void SetFolder(string artworkId, string? folder)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == artworkId);
            if (entry == null) return;
            entry.FolderName = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> GetAllFolders()
    {
        lock (_gate)
            return _entries
                .Where(e => !string.IsNullOrWhiteSpace(e.FolderName))
                .Select(e => e.FolderName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToList();
    }

    public bool IsFavorite(string artworkId)
    {
        lock (_gate) return _entries.Any(e => e.Id == artworkId);
    }

    public void Add(ArtworkPreview artwork)
    {
        lock (_gate)
        {
            if (_entries.Any(e => e.Id == artwork.Id)) return;
            _entries.Add(LocalFavoriteEntry.FromPreview(artwork));
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string artworkId)
    {
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => e.Id == artworkId);
            if (removed > 0) Save();
            else return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle(ArtworkPreview artwork)
    {
        if (IsFavorite(artwork.Id)) Remove(artwork.Id);
        else Add(artwork);
    }

    private void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) { _entries = []; return; }
            try
            {
                using var fs = File.OpenRead(_path);
                _entries = JsonSerializer.Deserialize<List<LocalFavoriteEntry>>(fs, JsonOpts) ?? [];
            }
            catch { _entries = []; }
        }
    }

    private void Save()
    {
        using var fs = File.Create(_path);
        JsonSerializer.Serialize(fs, _entries, JsonOpts);
    }
}

/// <summary>Minimal snapshot of an artwork stored as a local favorite.</summary>
public sealed class LocalFavoriteEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int PageCount { get; set; } = 1;
    public int XRestrict { get; set; }
    public int IllustType { get; set; }
    public int AiType { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? FolderName { get; set; }

    public ArtworkPreview ToArtworkPreview() => new()
    {
        Id = Id,
        Title = Title,
        UserId = UserId,
        UserName = UserName,
        ThumbnailUrl = ThumbnailUrl,
        PageCount = PageCount,
        XRestrict = XRestrict,
        IllustType = IllustType,
        AiType = AiType,
        Tags = Tags,
    };

    public static LocalFavoriteEntry FromPreview(ArtworkPreview p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        UserId = p.UserId,
        UserName = p.UserName,
        ThumbnailUrl = p.ThumbnailUrl,
        PageCount = p.PageCount,
        XRestrict = p.XRestrict,
        IllustType = p.IllustType,
        AiType = p.AiType,
        Tags = p.Tags,
        AddedAt = DateTimeOffset.UtcNow,
    };
}
