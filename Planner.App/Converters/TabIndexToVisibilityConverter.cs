using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Planner.App.Converters;

public class TabIndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var idx = value is int i ? i : -1;
        var param = parameter?.ToString() ?? "";
        if (param == "Label")
            return idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (param == "ComboBox")
            return idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
