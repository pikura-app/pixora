using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pikura.Avalonia.Services;

/// <summary>Static converters for collection/count predicates used in bindings.</summary>
public static class CountToBoolConverter
{
    /// <summary>True when the bound integer value is zero.</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(c => c == 0);

    /// <summary>True when the bound integer value is greater than zero.</summary>
    public static readonly IValueConverter IsNonZero = new FuncValueConverter<int, bool>(c => c > 0);
}
