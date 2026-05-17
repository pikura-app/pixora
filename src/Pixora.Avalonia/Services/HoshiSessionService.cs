using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pixora.Avalonia.ViewModels;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Stores Hoshi conversation sessions on disk under <c>%AppData%/Pixora/hoshi_sessions/</c>.
/// Each session is one <c>{id}.json</c> file plus an optional <c>{id}.img</c> sidecar.
/// </summary>
public sealed class HoshiSessionService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _dir;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    /// <summary>All sessions, sorted by UpdatedAt descending (most recent first).</summary>
    public ObservableCollection<HoshiSession> Sessions { get; } = new();

    /// <summary>Fired when sessions are added, removed, or renamed externally (so the UI can refresh).</summary>
    public event EventHandler? SessionsChanged;

    public HoshiSessionService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixora", "hoshi_sessions");
        Directory.CreateDirectory(_dir);
        LoadAll();
    }

    private void LoadAll()
    {
        Sessions.Clear();
        try
        {
            var jsons = Directory.EnumerateFiles(_dir, "*.json").ToList();
            var loaded = new System.Collections.Generic.List<HoshiSession>();
            foreach (var path in jsons)
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var session = JsonSerializer.Deserialize<HoshiSession>(json, JsonOpts);
                    if (session == null) continue;

                    // Sidecar image
                    var imgPath = Path.ChangeExtension(path, ".img");
                    if (File.Exists(imgPath))
                    {
                        try { session.ImageBytes = File.ReadAllBytes(imgPath); }
                        catch { /* corrupt sidecar — skip image */ }
                    }
                    loaded.Add(session);
                }
                catch { /* skip corrupt session file */ }
            }
            foreach (var s in loaded.OrderByDescending(x => x.UpdatedAt))
                Sessions.Add(s);
        }
        catch { /* directory inaccessible — start empty */ }
    }

    /// <summary>Creates a new empty session, persists it, and returns it.</summary>
    public HoshiSession CreateNew(string? title = null)
    {
        var s = new HoshiSession { Title = title ?? "New chat" };
        Sessions.Insert(0, s);
        _ = SaveAsync(s);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return s;
    }

    /// <summary>Persists a session to disk (JSON + optional image sidecar).</summary>
    public async Task SaveAsync(HoshiSession session)
    {
        await _ioLock.WaitAsync();
        try
        {
            session.UpdatedAt = DateTime.UtcNow;
            var jsonPath = Path.Combine(_dir, session.Id + ".json");
            var imgPath  = Path.Combine(_dir, session.Id + ".img");

            var json = JsonSerializer.Serialize(session, JsonOpts);
            await File.WriteAllTextAsync(jsonPath, json);

            if (session.ImageBytes is { Length: > 0 } bytes)
            {
                await File.WriteAllBytesAsync(imgPath, bytes);
            }
            else if (File.Exists(imgPath))
            {
                try { File.Delete(imgPath); } catch { /* ignore */ }
            }
        }
        catch { /* persist failure is non-fatal */ }
        finally { _ioLock.Release(); }
    }

    /// <summary>Deletes a session from disk and the collection.</summary>
    public async Task DeleteAsync(string id)
    {
        var existing = Sessions.FirstOrDefault(s => s.Id == id);
        if (existing != null) Sessions.Remove(existing);

        await _ioLock.WaitAsync();
        try
        {
            var jsonPath = Path.Combine(_dir, id + ".json");
            var imgPath  = Path.Combine(_dir, id + ".img");
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            if (File.Exists(imgPath))  File.Delete(imgPath);
        }
        catch { /* non-fatal */ }
        finally { _ioLock.Release(); }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Creates a copy of a session with a new id (image + messages preserved).</summary>
    public HoshiSession Duplicate(HoshiSession source)
    {
        var copy = new HoshiSession
        {
            Title = source.Title + " (copy)",
            ImageSource = source.ImageSource,
            ImageBytes  = source.ImageBytes?.ToArray(),
            Messages    = source.Messages.Select(m => new PersistedMessage
            {
                Role = m.Role, Content = m.Content, At = m.At
            }).ToList(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        Sessions.Insert(0, copy);
        _ = SaveAsync(copy);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return copy;
    }

    /// <summary>Moves a session to the top of the list (call after updates).</summary>
    public void Touch(HoshiSession session)
    {
        var idx = Sessions.IndexOf(session);
        if (idx > 0) Sessions.Move(idx, 0);
    }
    
    /// <summary>Gets sessions grouped by date for display in the sidebar.</summary>
    public List<SessionGroupViewModel> GetGroupedSessions()
    {
        return Sessions
            .GroupBy(s => s.UpdatedAt.ToLocalTime().Date)
            .Select(g => new SessionGroupViewModel(g.Key, g))
            .OrderByDescending(g => g.Date)
            .ToList();
    }
}
