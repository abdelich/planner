using System.Globalization;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class RecurrenceKindToRussianConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecurrenceKind k)
            return k switch
            {
                RecurrenceKind.EveryDay => "Каждый день",
                RecurrenceKind.EveryNDays => "Каждые N дней",
                RecurrenceKind.SpecificDaysOfWeek => "По дням недели",
                _ => value.ToString()
            };
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
