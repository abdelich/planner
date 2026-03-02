using System.Globalization;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class GoalTypeToRussianConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GoalType type)
            return type switch
            {
                GoalType.Daily => "День",
                GoalType.Weekly => "Неделя",
                GoalType.Monthly => "Месяц",
                _ => value.ToString()
            };
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
