using CommunityToolkit.Mvvm.ComponentModel;
using Pikura.Avalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pikura.Avalonia.ViewModels;

/// <summary>
/// Represents a group of sessions for a specific date.
/// </summary>
public partial class SessionGroupViewModel : ObservableObject
{
    [ObservableProperty] private bool _isExpanded = true;
    
    public DateTime Date { get; }
    public string DisplayDate { get; }
    public List<HoshiSession> Sessions { get; }
    
    public SessionGroupViewModel(DateTime date, IEnumerable<HoshiSession> sessions)
    {
        Date = date.Date;
        DisplayDate = GetDisplayDate(date);
        Sessions = sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }
    
    private static string GetDisplayDate(DateTime date)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        
        if (date.Date == today.Date)
            return "Today";
        if (date.Date == yesterday.Date)
            return "Yesterday";
        
        // If within the last week, show day name
        if (today - date.Date <= TimeSpan.FromDays(7))
            return date.ToString("dddd");
        
        // Otherwise show date
        return date.ToString("MMM dd, yyyy");
    }
}
