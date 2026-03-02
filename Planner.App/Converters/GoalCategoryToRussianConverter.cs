using System.Globalization;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class GoalCategoryToRussianConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GoalCategory c)
            return c == GoalCategory.Period ? "На день / неделю / месяц" : "Повторяемая привычка";
        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
