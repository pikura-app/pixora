using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pikura.Avalonia.ViewModels;

namespace Pikura.Avalonia.Views.History;

public partial class HistoryView : UserControl
{
    private bool _dragging;
    private int  _dragFromIndex = -1;
    private Point _dragStart;
    private const double DragThreshold = 6;

    public HistoryView()
    {
        InitializeComponent();
        ActiveJobsList.Loaded += OnActiveJobsListLoaded;
    }

    private void OnActiveJobsListLoaded(object? sender, RoutedEventArgs e)
        => RefreshDragHandlers();

    // Re-attach whenever the collection changes
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HistoryViewModel hvm)
            hvm.ActiveJobs.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshDragHandlers);
    }

    private void RefreshDragHandlers()
    {
        var panel = ActiveJobsList.ItemsPanelRoot as Panel;
        if (panel == null) return;
        foreach (var child in panel.Children)
        {
            child.RemoveHandler(PointerPressedEvent,  OnCardPressed);
            child.RemoveHandler(PointerMovedEvent,    OnCardMoved);
            child.RemoveHandler(PointerReleasedEvent, OnCardReleased);
            child.RemoveHandler(PointerCaptureLostEvent, OnCaptureLost);
            child.AddHandler(PointerPressedEvent,  OnCardPressed,  RoutingStrategies.Tunnel);
            child.AddHandler(PointerMovedEvent,    OnCardMoved,    RoutingStrategies.Tunnel);
            child.AddHandler(PointerReleasedEvent, OnCardReleased, RoutingStrategies.Tunnel);
            child.AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel);
        }
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        var panel = ActiveJobsList.ItemsPanelRoot as Panel;
        if (panel == null) return;
        _dragFromIndex = panel.Children.IndexOf((Visual?)sender as Control ?? (Control)sender!);
        _dragStart = e.GetPosition(panel);
        _dragging  = false;
    }

    private void OnCardMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFromIndex < 0) return;
        var panel = ActiveJobsList.ItemsPanelRoot as Panel;
        if (panel == null) return;
        var pos = e.GetPosition(panel);
        if (!_dragging && Math.Abs(pos.Y - _dragStart.Y) < DragThreshold) return;
        _dragging = true;
        ((InputElement)sender!).Cursor = new Cursor(StandardCursorType.DragMove);

        // Find destination index by hit-testing child midpoints
        int toIndex = _dragFromIndex;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            var bounds = child.Bounds;
            if (pos.Y < bounds.Y + bounds.Height / 2) { toIndex = i; break; }
            toIndex = i;
        }
        if (toIndex == _dragFromIndex) return;

        // Swap in the observable collection
        if (DataContext is not HistoryViewModel hvm) return;
        var jobs = hvm.ActiveJobs;
        if (_dragFromIndex >= jobs.Count || toIndex >= jobs.Count) return;
        jobs.Move(_dragFromIndex, toIndex);
        _dragFromIndex = toIndex;
    }

    private void OnCardReleased(object? sender, PointerReleasedEventArgs e)
    {
        ((InputElement)sender!).Cursor = Cursor.Default;
        if (_dragging && DataContext is HistoryViewModel hvm && _dragFromIndex >= 0
            && _dragFromIndex < hvm.ActiveJobs.Count)
        {
            var ids = hvm.ActiveJobs.Select(j => j.Job.Id).ToList();
            _ = hvm.PersistActiveJobOrderAsync(ids);
        }
        _dragging      = false;
        _dragFromIndex = -1;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ((InputElement)sender!).Cursor = Cursor.Default;
        _dragging      = false;
        _dragFromIndex = -1;
    }
}
