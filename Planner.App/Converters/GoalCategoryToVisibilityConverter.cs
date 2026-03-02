using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class GoalCategoryToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is GoalCategory c && c == GoalCategory.Period ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
