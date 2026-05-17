using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Pixora.Avalonia.ViewModels;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Navigation service for Avalonia application
/// </summary>
public class NavigationService
{
    private readonly ILogger<NavigationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private ContentControl? _contentControl;

    public NavigationService(ILogger<NavigationService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public void Initialize(ContentControl contentControl)
    {
        _contentControl = contentControl;
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        if (_contentControl == null)
        {
            _logger.LogError("NavigationService not initialized with ContentControl");
            return;
        }

        try
        {
            var viewModel = _serviceProvider.GetRequiredService<T>();
            var viewType = GetViewType<T>();
            var view = (Control)Activator.CreateInstance(viewType)!;
            view.DataContext = viewModel;
            
            _contentControl.Content = view;
            _logger.LogInformation("Navigated to {ViewType}", viewType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to {ViewModelType}", typeof(T).Name);
        }
    }

    private Type GetViewType<T>() where T : ViewModelBase
    {
        var viewModelTypeName = typeof(T).Name;
        var viewTypeName = viewModelTypeName.Replace("ViewModel", "View");
        
        // Try to find the view in subdirectories first (more specific)
        var viewType = typeof(App).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == viewTypeName && typeof(Control).IsAssignableFrom(t));
        
        if (viewType == null)
        {
            // Try to find the view in the current assembly
            viewType = typeof(App).Assembly.GetType($"Pixora.Avalonia.Views.{viewTypeName}");
        }

        if (viewType == null)
        {
            // Try common namespace patterns
            var possibleNamespaces = new[]
            {
                $"Pixora.Avalonia.Views.{viewTypeName}",
                $"Pixora.Avalonia.Views.Gallery.{viewTypeName}",
                $"Pixora.Avalonia.Views.Settings.{viewTypeName}",
                $"Pixora.Avalonia.Views.Rankings.{viewTypeName}",
                $"Pixora.Avalonia.Views.Artwork.{viewTypeName}"
            };
            
            foreach (var ns in possibleNamespaces)
            {
                viewType = typeof(App).Assembly.GetType($"{ns}.{viewTypeName}");
                if (viewType != null) break;
            }
        }

        if (viewType == null)
        {
            throw new InvalidOperationException($"View type '{viewTypeName}' not found for ViewModel '{viewModelTypeName}'. Available views: {string.Join(", ", typeof(App).Assembly.GetTypes().Where(t => typeof(Control).IsAssignableFrom(t) && t.Name.EndsWith("View")).Select(t => t.Name))}");
        }

        return viewType;
    }
}
