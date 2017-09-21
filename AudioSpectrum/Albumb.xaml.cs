using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AudioSpectrum
{
    /// <summary>
    /// Interaction logic for Albumb.xaml
    /// </summary>
    public partial class Albumb : Window
    {
        public Albumb()
        {
            InitializeComponent();
        }
        public void SetData ( string image, string name )
        {
            Dispatcher.Invoke(() =>
            {
                BitmapImage logo = new BitmapImage();
                logo.BeginInit();
                logo.UriSource = new Uri(image);
                logo.EndInit();
                this.albumbImage.Source = logo;
                this.albumbTitle.Content = name;
                this.Title = name;
                this.BringIntoView();
            });
        }
    }
}
