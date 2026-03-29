using System.Globalization;
using System.Windows.Data;

namespace Communication.Test
{
    class LengthEnableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var i = (int)value;
            if (i == 5 || i == 6) return true;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
