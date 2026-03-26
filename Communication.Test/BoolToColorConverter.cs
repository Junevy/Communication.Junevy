using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Communication.Test
{
    class BoolToColorConverter : IValueConverter
    {
        SolidColorBrush red = Brushes.Red;
        SolidColorBrush green = Brushes.Green;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = (bool)value;

            if (b) return green;
            return red;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
