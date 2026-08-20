using System.Globalization;
using Avalonia.Data.Converters;

namespace MapExplorer.App.Converters;

/// <summary>Save button label: "Saving…" while EditViewModel.SaveCommand is running, "Save" otherwise.</summary>
public sealed class SavingLabelConverter : IValueConverter
{
    public static readonly SavingLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Saving…" : "Save";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
