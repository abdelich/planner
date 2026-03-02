using System.Globalization;
using System.Windows.Data;
using Planner.App.Models;

namespace Planner.App.Converters;

public class TransactionTypeToRussianConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TransactionType t
            ? t == TransactionType.Income ? "Доход" : "Расход"
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
