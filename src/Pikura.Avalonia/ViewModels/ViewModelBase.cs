using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pikura.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly ILogger Logger;

    protected ViewModelBase()
    {
        Logger = NullLogger.Instance;
    }

    protected ViewModelBase(ILogger logger)
    {
        Logger = logger;
    }
}
