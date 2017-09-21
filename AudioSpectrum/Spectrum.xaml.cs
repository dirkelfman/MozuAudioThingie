using System.Collections.Generic;
using System.Windows.Controls;

namespace AudioSpectrum
{
    /// <summary>
    /// Interaction logic for Spectrum.xaml
    /// </summary>
    public partial class Spectrum : UserControl
    {
        ProgressBar[] bars;
        public Spectrum()
        {
            InitializeComponent();
            bars = new ProgressBar[] { Bar01 ,
            Bar02,
            Bar03,
             Bar04,
            Bar05,
             Bar06,
            Bar07,
             Bar08,
            Bar09,
             Bar10,
            Bar11,
             Bar12,
            Bar13,
             Bar14,
            Bar15,
             Bar16
            };
        }

        public void Set(List<byte> data)
        {
            for ( int i =0;i< data.Count; i++)
            {
                bars[i].Value = data[i];
            }
           
        }
    }
}
