using System;
using System.Collections.Generic;
using System.Linq;

namespace Pikura.Avalonia.Services;

/// <summary>
/// In-app clipboard for copying/pasting artist/artwork IDs across views.
/// Artist IDs are accumulated so that each individual copy adds to a queue;
/// calling FlushArtistIds() returns and clears the full accumulated list.
/// </summary>
public static class QuickClipboardService
{
    private static readonly List<string> _artistIdQueue = new();
    private static string? _lastCopiedArtworkId;

    /// <summary>Number of artist IDs currently queued.</summary>
    public static int QueuedArtistCount => _artistIdQueue.Count;

    /// <summary>Last copied artwork ID (single, not accumulated).</summary>
    public static string? LastCopiedId => _lastCopiedArtworkId ?? (_artistIdQueue.Count > 0 ? string.Join(",", _artistIdQueue) : null);
    public static string? LastCopiedType => _artistIdQueue.Count > 0 ? "artist" : (_lastCopiedArtworkId != null ? "artwork" : null);

    public static event Action? ClipboardChanged;

    /// <summary>Adds an artist ID to the accumulator queue.</summary>
    public static void CopyArtist(string artistId)
    {
        if (!_artistIdQueue.Contains(artistId))
            _artistIdQueue.Add(artistId);
        ClipboardChanged?.Invoke();
    }

    /// <summary>Returns all queued artist IDs joined by comma and clears the queue.</summary>
    public static string? FlushArtistIds()
    {
        if (_artistIdQueue.Count == 0) return null;
        var result = string.Join(",", _artistIdQueue);
        _artistIdQueue.Clear();
        ClipboardChanged?.Invoke();
        return result;
    }

    public static void CopyArtwork(string artworkId)
    {
        _lastCopiedArtworkId = artworkId;
        ClipboardChanged?.Invoke();
    }

    public static void Clear()
    {
        _artistIdQueue.Clear();
        _lastCopiedArtworkId = null;
        ClipboardChanged?.Invoke();
    }

    /// <summary>Copies multiple artist IDs (comma-separated) directly — replaces queue.</summary>
    public static void CopyMultipleArtists(string artistIds)
    {
        _artistIdQueue.Clear();
        foreach (var id in artistIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _artistIdQueue.Add(id);
        ClipboardChanged?.Invoke();
    }
}
