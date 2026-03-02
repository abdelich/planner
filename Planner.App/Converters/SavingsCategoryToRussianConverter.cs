using System.Globalization;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class SavingsCategoryToRussianConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SavingsCategory c ? (c.Name ?? "") : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
