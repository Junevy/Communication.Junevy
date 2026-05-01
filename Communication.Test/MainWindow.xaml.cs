using System.Windows;

namespace Communication.Test
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel vm)
        {
            InitializeComponent();
            //MainWindowViewModel vm = new(App.Current.pro);
            this.DataContext = vm;
        }
    }
}