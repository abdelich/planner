using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class EveryNDaysToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is RecurrenceKind k && k == RecurrenceKind.EveryNDays ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SpecificDaysToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is RecurrenceKind k && k == RecurrenceKind.SpecificDaysOfWeek ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
