using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Un4seen.Bass;
using Un4seen.BassWasapi;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using SpotifyAPI.Local;
using System.Threading;

namespace AudioSpectrum
{

    internal class Analyzer
    {
        private bool _enable;               //enabled status
        private DispatcherTimer _t;         //timer that refreshes the display
        private float[] _fft;               //buffer for fft data
        private ProgressBar _l, _r;         //progressbars for left and right channel intensity
        private WASAPIPROC _process;        //callback function to obtain data
        private int _lastlevel;             //last output level
        private int _hanctr;                //last output level counter
        private List<byte> _spectrumdata;   //spectrum data buffer
        private Spectrum _spectrum;
        private Slider _colorSlider;//spectrum dispay control
        private ComboBox _devicelist;       //device list
        private bool _initialized;          //initialized flag
        private int devindex;               //used device index
        SpotifyLocalAPI _spotify;
        private int _lines = 1;            // number of spectrum lines
        private int _rightIdx = 16;
        int _leftIdx = 84;
        //int _ledCount = 100;
        int[] _ledArray = new int[96];
        int[] _lastSent = new int[96];
        private Albumb _alb;
        private UdpClient client;
        LinkedList<Tuple<float, DateTime>> powerHistory = new LinkedList<Tuple<float, DateTime>>();
        DateTime lastBeat = DateTime.Now;
        int colorStep = 2;
        ComboBox _visBoz;
        string StopLightIp = "172.19.65.170";
        string[] visualizers = new string[] {
            "ChaseOut",
            "ChaseIn",
            "RainbowAnalizer",
            "SmallGains",
            "RainbowGains",
            "StopLight",
            "ColorSlider",
            "Random" };

    //ctor
        public Analyzer(ProgressBar left, ProgressBar right, Spectrum spectrum, ComboBox devicelist, ComboBox visBoz, Slider colorSlider)
        {
            _fft = new float[1024];
            _lastlevel = 0;
            _hanctr = 0;
            _t = new DispatcherTimer();
            _t.Tick += _t_Tick;
            _t.Interval = TimeSpan.FromMilliseconds(40); //40hz refresh rate
            _t.IsEnabled = false;
            _l = left;
            _r = right;
            _l.Minimum = 0;
            _r.Minimum = 0;
            _r.Maximum = ushort.MaxValue;
            _l.Maximum = ushort.MaxValue;
            _visBoz = visBoz;
            _process = new WASAPIPROC(Process);
            _spectrumdata = new List<byte>();
            _spectrum = spectrum;
            _devicelist = devicelist;
            _colorSlider = colorSlider;
            _initialized = false;
           
            Init();
            InitSpot();
            this.IpAddress = IPAddress.Parse("172.19.65.95");
            this.Enable = true;

           

        }

        // Serial port for arduino output
        public SerialPort Serial { get; set; }

        // flag for display enable
        public bool DisplayEnable { get; set; }

        //flag for enabling and disabling program functionality
        public bool Enable
        {
            get { return _enable; }
            set
            {
                _enable = value;
                if (value)
                {
                    if (!_initialized)
                    {
                        var array = (_devicelist.Items[_devicelist.SelectedIndex] as string).Split(' ');
                        devindex = Convert.ToInt32(array[0]);

                        if ( IpAddress!= null)
                        {
                            client = new UdpClient();
                            IPEndPoint ep = new IPEndPoint(IpAddress, 7777); // endpoint where server is listening
                            client.Connect(ep);
                        }
                       



                        bool result = BassWasapi.BASS_WASAPI_Init(devindex, 0, 0, BASSWASAPIInit.BASS_WASAPI_BUFFER, 1f, 0.05f, _process, IntPtr.Zero);
                        if (!result)
                        {
                            var error = Bass.BASS_ErrorGetCode();
                            MessageBox.Show(error.ToString());
                        }
                        else
                        {
                            _initialized = true;
                            _devicelist.IsEnabled = false;
                        }
                    }
                    BassWasapi.BASS_WASAPI_Start();
                }
                else BassWasapi.BASS_WASAPI_Stop(true);
                System.Threading.Thread.Sleep(500);
                _t.IsEnabled = value;
            }
        }

        public IPAddress IpAddress { get;  set; }

        string _vizulizer;
        bool? sle = null;
        bool StopLightEnabled
        {
            get
            {
                if (!sle.HasValue)
                {
                    System.Net.NetworkInformation.Ping p = new System.Net.NetworkInformation.Ping();
                    try
                    {
                       var res = p.Send(IPAddress.Parse(StopLightIp), 1000);
                        sle = res.Status == System.Net.NetworkInformation.IPStatus.Success;
                    }
                    catch
                    {
                        sle = false;
                    }
                }
                return sle.Value;
            }
        }
        public string Visuualizer
        {
            get { return _vizulizer; }
            set
            {
                Array.Clear(_ledArray, 0, _ledArray.Length);
                Render();
               
                if ( value?.ToLower() == "random")
                {
                    if ( _randTimer != null)
                    {
                        _randTimer.Dispose();
                    }
                    _randTimer = new System.Threading.Timer(s =>
                    {
                       
                        
                        string last = _vizulizer;
                        while (last == _vizulizer)
                        {
                            var rand = new Random().Next(visualizers.Length - 2);
                            
                            var viz = visualizers[rand];

                            if ( viz == "StopLight" )
                            {
                                continue;
                            }
                            _vizulizer = visualizers[rand];
                        }
                        try
                        {
                            DoStopLight(AllOff).Wait();
                        }
                        catch { }

                    }, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(30));
                }else
                {
                    _vizulizer = value;
                }
                _enable = true;



            }
        }

        System.Threading.Timer _randTimer;

        // initialization
        private void Init()
        {
            bool result = false;
            for (int i = 0; i < BassWasapi.BASS_WASAPI_GetDeviceCount(); i++)
            {
                var device = BassWasapi.BASS_WASAPI_GetDeviceInfo(i);
                if (device.IsEnabled && device.IsLoopback)
                {
                    _devicelist.Items.Add(string.Format("{0} - {1}", i, device.name));
                }
            }
            foreach ( var entry in visualizers)
            {
                _visBoz.Items.Add(entry);
            }

            _visBoz.SelectedIndex = _visBoz.Items.Count - 1;
            Visuualizer = visualizers[0];

            _devicelist.SelectedIndex = 0;
            Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATETHREADS, false);
            result = Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
            if (!result) throw new Exception("Init Error");
            
        }
        void InitSpot()
        {
            _spotify = new SpotifyLocalAPI();
           
            if (!SpotifyLocalAPI.IsSpotifyRunning())
            {
                SpotifyLocalAPI.RunSpotify();
                Thread.Sleep(5000);
            }

            if (!SpotifyLocalAPI.IsSpotifyWebHelperRunning())
            {
                SpotifyLocalAPI.RunSpotifyWebHelper();
                Thread.Sleep(4000);
            }
            if (!_spotify.Connect())
            {
                Console.WriteLine("asdf");
            }
            _alb = new AudioSpectrum.Albumb();
            _alb.WindowState = WindowState.Maximized;
            var status = _spotify.GetStatus();

            if (status.Track != null)
            {
                _spotify_OnTrackChange(null, new TrackChangeEventArgs() { NewTrack = status.Track });
            }
           

            _alb.Show();

            _spotify.OnTrackChange += _spotify_OnTrackChange;

            _spotify.ListenForEvents = true;
        }

        private void _spotify_OnTrackChange(object sender, TrackChangeEventArgs e)
        {
           
            try
            {
                var track = e.NewTrack;
                DoStopLight(RedOn).Wait();
                Thread.Sleep(250);
                DoStopLight(YelloOn).Wait();
                Thread.Sleep(250);
                DoStopLight(GreenOn).Wait();

                var url = track.GetAlbumArtUrl(SpotifyAPI.Local.Enums.AlbumArtSize.Size640);
                var name = track.ArtistResource.Name + " : " + track.TrackResource.Name;
                _alb.SetData(url, name);

                Thread.Sleep(2000);
                DoStopLight(AllOff).Wait();
            }
            catch(Exception ex )
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        void MiniGains ()
        {

        }
        int lastindex = 0;
        void AnimateCenterRightLeft()
        {
          
            int x, y;
            int b0 = 0;
            int chanelCount = 1;
            _spectrumdata.Clear();
            //computes the spectrum data, the code is taken from a bass_wasapi sample.
            for (x = 0; x < chanelCount; x++)
            {
                float peak = 0;
                int b1 = (int)Math.Pow(2, x * 10.0 / (chanelCount - 1));
                if (b1 > 1023) b1 = 1023;
                if (b1 <= b0) b1 = b0 + 1;
                for (; b0 < b1; b0++)
                {
                    if (peak < _fft[1 + b0]) peak = _fft[1 + b0];
                }
                y = (int)(Math.Sqrt(peak) * 3 * 255 - 4);
                if (y > 255) y = 255;
                if (y < 0) y = 0;
                _spectrumdata.Add((byte)y);
                //Console.Write("{0, 3} ", y);
            }

            if (DisplayEnable) _spectrum.Set(_spectrumdata);
            //if (Serial != null)
            //{
            //    Serial.Write(_spectrumdata.ToArray(), 0, _spectrumdata.Count);
            //}


            int level = BassWasapi.BASS_WASAPI_GetLevel();


            var left = (float)Utils.LowWord32(level) / 32768; ;
            var right = (float)Utils.HighWord32(level) / 32768;
 
            if ((DateTime.Now - lastBeat).TotalSeconds > 1)
            {
                if (left > Percentile(powerHistory.Select(_=> _.Item1), .8f))
                {
                    lastBeat = DateTime.Now;
                    lastindex += 125;
                    System.Diagnostics.Debug.WriteLine("beat " + DateTime.Now.Second + " " + DateTime.Now.Millisecond);
                }
            }

            if (client != null)
            {
                Array.Clear(_ledArray, 0, _ledArray.Length);

                lastindex += colorStep;
                if (lastindex > 256)
                {
                    lastindex = lastindex % 256;
                }




                for (int i = 0; i < _ledArray.Length; i++)
                {
                    _ledArray[i] = colorwheel((lastindex + i * colorStep) % 256);
                }
                var specLength = (_leftIdx - _rightIdx) / chanelCount;


                var rem = (_leftIdx - _rightIdx) % 3 + specLength % 2;
                specLength = specLength / 2;

                //Newtonsoft.Json.Linq.JArray arr = new Newtonsoft.Json.Linq.JArray();
                for (int i = 0; i < chanelCount; i++)
                {
                    var colorStart = (int)(i * ((float)256 / (float)chanelCount));
                    for (int j = 0; j < specLength; j++)
                    {
                        var powerLevel = (float)_spectrumdata[i] / (float)255;
                        var percentLevel = (float)(j + 1) / (float)specLength;
                        // var color = 0;
                        var on = powerLevel > percentLevel;


                        var idxa = _rightIdx + i * (specLength * 2) + specLength - (j + 1);
                        var idxb = _rightIdx + i * (specLength * 2) + specLength + j;
                        _ledArray[idxa] = on ? _ledArray[idxa] : 0;
                        _ledArray[idxb] = on ? _ledArray[idxb] : 0;
                    }
                }
                //  var percentLoud = 1.1*  (float)Utils.LowWord32(level)/ (float)65553;
                for (int i = 0; i < _rightIdx; i++)
                {
                    var percentIdx = (float)(i + 1) / (float)_rightIdx;
                    var lon = (1.1 * Math.Pow(left, .4)) > percentIdx;
                    var ron = (1.1 * Math.Pow(right, .4)) > percentIdx;
                    _ledArray[i] = ron ? _ledArray[0] : 0;
                    _ledArray[_ledArray.Length - i - 1] = lon ? _ledArray[0] : 0;
                }


                Render();


            }
         

        
        }

        void Render()
        {
            var updData = ConvertToMessageData(_ledArray, _lastSent);
            Array.Copy(_ledArray, _lastSent, (int)_ledArray.Length);
            if (updData.Length > 0)
            {
                client.Send(updData, updData.Length);
            }
        }

        int smallGainCnt = 0;
        void SmallGains()
        {
            smallGainCnt = smallGainCnt % 255 + 1;
            if ( smallGainCnt %4 == 0)
            {
                return;
            }
            var size = 11;
            var current = powerHistory.First() ;
            var pow = powerHistory.TakeWhile(x => (x.Item2 - current.Item2).TotalMilliseconds < .1).Average(x => x.Item1);
            var percentile = GetPrecentile(powerHistory.Where(x => x.Item1 > 0).Select(x => x.Item1), current.Item1);
            for ( int i = 0; i < Math.Ceiling(size/2f); i++)
            {
                int cnt = 0;
                var color = 0;
                if (pow>0 && percentile > (i+1)/(size/2f+1))
                {
                    color = colorwheel(smallGainCnt*2 + (i * 20));
                }
                for ( int j=0; j + (size / 2) + i < _ledArray.Length; j+=size)
                {
                    _ledArray[j+(size / 2)+i] = color;
                    _ledArray[j+(size / 2)-i] = color;
                }
            }
            System.Diagnostics.Debug.WriteLine($"{ current} ,  {percentile}");
            Render();
        }

        int RainbowGainsCnt = 0;
        void RainbowGains()
        {
            RainbowGainsCnt = RainbowGainsCnt % 255 + 1;
            if (RainbowGainsCnt % 4 == 0)
            {
                return;
            }
            for (int i=0;i< _ledArray.Length;i++)
            {
                _ledArray[i] = colorwheel(i * 4 + RainbowGainsCnt*4);
            }
            var size = 7;
            var current = powerHistory.First();
            var pow = powerHistory.TakeWhile(x => (x.Item2 - current.Item2).TotalMilliseconds < .1).Average(x => x.Item1);
            var percentile = GetPrecentile(powerHistory.Where(x => x.Item1 > 0).Select(x => x.Item1), current.Item1);
            for (int i = 0; i < Math.Ceiling(size / 2f); i++)
            {
                // int cnt = 0;
                bool on = false;
                
                if (pow > 0 && percentile > (i + 1) / (size / 2f + 1))
                {
                    on = true;
                }
                if (!on)
                {
                    for (int j = 0; j + (size / 2) + i < _ledArray.Length; j += size)
                    {
                        _ledArray[j + (size / 2) + i] = 0;
                        _ledArray[j + (size / 2) - i] = 0;
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"{ current} ,  {percentile}");
            Render();
        }

        void ChasOld()
        {
          
            int x, y;
            int b0 = 0;
            int chanelCount = 3;

            int level = BassWasapi.BASS_WASAPI_GetLevel();


           

            var left = (float)Utils.LowWord32(level) / 32768; ;



           
            //  BassWasapi.BASS_WASAPI_GetLevel()
            _spectrumdata.Clear();
            //computes the spectrum data, the code is taken from a bass_wasapi sample.
            for (x = 0; x < chanelCount; x++)
            {
                float peak = 0;
                int b1 = (int)Math.Pow(2, x * 10.0 / (chanelCount - 1));
                if (b1 > 1023) b1 = 1023;
                if (b1 <= b0) b1 = b0 + 1;
                for (; b0 < b1; b0++)
                {
                    if (peak < _fft[1 + b0]) peak = _fft[1 + b0];
                }
                y = (int)(Math.Sqrt(peak) * 3 * 255 - 4);
                if (y > 255) y = 255;
                if (y < 0) y = 0;
                _spectrumdata.Add((byte)y);
                //Console.Write("{0, 3} ", y);
            }

            if (DisplayEnable) _spectrum.Set(_spectrumdata);
         

        
            if (client != null)
            {
                //   var precentile = GetPrecentile(powerHistory, left);
                //   System.Diagnostics.Debug.WriteLine(precentile);
                //      var poweer256 =  Convert.ToInt32( GetPrecentile(powerHistory, left)  * 255);

                //var red = _spectrumdata[1] - _spectrumdata[1] % 3;
                //var green = _spectrumdata[0] - _spectrumdata[0] % 3;
                //var blue =  _spectrumdata[2] - _spectrumdata[2] % 3;
                // var red = (byte)((int)Convert.ToInt32(Math.Pow(GetPrecentile(powerHistory, left), 2) * 255));
                //   var newColor = rgb2Int(red, green, blue);
                var newColor = rgb2Intx(_spectrumdata[1], _spectrumdata[0], _spectrumdata[2]);
                int middele = _ledArray.Length / 2;

                var nextcolor = newColor;
                for (var i = middele -1; i >=0 ; i--)
                {
                    var lastColor = _ledArray[i];
                    _ledArray[i] = nextcolor;
                     nextcolor = lastColor;
                }
                nextcolor = newColor;
                for (var i = middele ; i<  _ledArray.Length;i++)
                {
                    var lastColor = _ledArray[i];
                    _ledArray[i] = nextcolor;
                    nextcolor = lastColor;
                }
                Render();
            }
           

        }
        void ColorSlider()
        {
            int middele = _ledArray.Length / 2;
            for (var i = middele - 1; i >= 0; i--)
            {

                _ledArray[i] = colorwheel((int)_colorSlider.Value + (middele-i));
            }
          
            for (var i = middele; i < _ledArray.Length; i++)
            {
                _ledArray[i] = colorwheel((int)_colorSlider.Value + (i -middele));
            }
            Render();
        }
        int chasecnt = 0;
        internal byte[] Color
        {
            set
            {
                var val=rgb2Int(value[0], value[1], value[2]);
                for (int i = 0; i < _ledArray.Length; i++)
                {
                    _ledArray[i] = val;
                }
                Render();
            }
        }

        void Chase(bool outward=true)
        {

            if ( powerHistory.Count < 10)
            {
                return;
            }
            
            var current= powerHistory.First();
            //var span = powerHistory.First().Item2 - powerHistory.Last().Item2;
         //   var pow = powerHistory.TakeWhile(x => (x.Item2 - current.Item2).TotalMilliseconds < .1).Average(x=>x.Item1);
            var pow = Math.Max(current.Item1, 0);
           
            if (client != null)
            {
                var newColor = 0;
              //  var pow = powerHistory.First().Item1;
                var precentile = GetPrecentile(powerHistory.Where( x=> x.Item1> 0 ).Select(x=> x.Item1), pow);
                if (precentile > .6 && pow > .01)
                {
                    chasecnt++;
                    chasecnt = chasecnt % 10;

                    var colorWheelPos = 0; ;
                    if (precentile > .7)
                    {
                        
                        colorWheelPos = 270 - (int)(50f * Math.Pow( ((1f - precentile) / .3)    , 2));
                    }
                    else
                    {
                        colorWheelPos = 150 - (int)(50f * Math.Pow(((.7f - precentile) /.1), 2));
                    }
                    colorWheelPos += chasecnt;
                    System.Diagnostics.Debug.WriteLine($"pow { current.Item1} avg { pow} precentile{ precentile} colorPos { colorWheelPos}" );
                    newColor = colorwheel(colorWheelPos);
                }
                if (outward)
                {
                    int middele = _ledArray.Length / 2;
                    var nextcolor = newColor;
                    for (var i = middele - 1; i >= 0; i--)
                    {
                        var lastColor = _ledArray[i];
                        _ledArray[i] = nextcolor;
                        nextcolor = lastColor;
                    }
                    nextcolor = newColor;
                    for (var i = middele; i < _ledArray.Length; i++)
                    {
                        var lastColor = _ledArray[i];
                        _ledArray[i] = nextcolor; ;
                        nextcolor = lastColor;
                    }
                }
                else
                {
                    int middele = _ledArray.Length / 2;
                    var nextcolor = newColor;
                    for (var i = 0; i < middele ; i++)
                    {
                        var lastColor = _ledArray[i];
                        _ledArray[i] = nextcolor;
                        nextcolor = lastColor;
                    }
                    nextcolor = newColor;
                    for (var i = _ledArray.Length-1; i >= middele; i--)
                    {
                        var lastColor = _ledArray[i];
                        _ledArray[i] = nextcolor; ;
                        nextcolor = lastColor;
                    }
                }
              

                Render();
            }

        }
        bool[] lastState = new bool[] { false , false ,false };
        int stopLightcnt = 0;
        void StopLight(bool outward = true)
        {
            
            stopLightcnt++;
            if (powerHistory.Count < 10)
            {
                return;
            }

            //if (stopLightcnt % 10 != 0)
            //{

            //}

            bool[] state = new bool[] { false, false, false };

            var current = powerHistory.First();
            //var span = powerHistory.First().Item2 - powerHistory.Last().Item2;
            //   var pow = powerHistory.TakeWhile(x => (x.Item2 - current.Item2).TotalMilliseconds < .1).Average(x=>x.Item1);
            var pow = Math.Max(current.Item1, 0);
            //bool greenOn = false;
            //bool redOn = false;
            //bool yelloOn = false;
            if (client != null)
            {
           
                //  var pow = powerHistory.First().Item1;
                var precentile = GetPrecentile(powerHistory.Where(x => x.Item1 > 0).Select(x => x.Item1), pow);
                if (precentile > .5 && pow > .02)
                {
                    state[0] = true;
                  //  greenOn = true;
                }
                if (precentile > .7 && pow > .02)
                {
                    state[1] = true;
                }
                if (precentile > .85 && pow > .02)
                {
                    state[2] = true;
                }
            }
            var half = +_ledArray.Length / 2;
            for (int i = 0; i < half; i++)
            {
                if ( i > _ledArray.Length *.66 *.5)
                {
                    _ledArray[half - i] = _ledArray[half + i] = state[0] ? colorwheel(85) : 0;
                   
                }
                else if(i > _ledArray.Length * .33 * .5)
                {
                    _ledArray[half - i] = _ledArray[half + i] = state[1] ? colorwheel(30) : 0;
                }
                else
                {
                    _ledArray[half - i] = _ledArray[half + i] = state[2] ? colorwheel(0) : 0;

                }

            }

            List<Task> tasks = new List<Task>();
            try
            {

                var code = AllOff;
                if (state[2]!= lastState[1])
                {
                    tasks.Add(DoStopLight(state[2] ? RedOn : RedOff));
                }
                if (state[1] != lastState[1])
                {
                    tasks.Add(DoStopLight(state[1] ? YelloOn : YelloOff));
                }
                if (state[0] != lastState[0])
                {
                    tasks.Add(DoStopLight(state[1] ? GreenOn : GreenOff));
                }


                lastState = state;
                Render();
                Task.WaitAll(tasks.ToArray(), 5000);

            }
            catch { }
        }
        HttpClient httpClient = null;
        Task<string> DoStopLight ( string script)
        {
            if (httpClient == null )
            {
                httpClient = new System.Net.Http.HttpClient();
                var byteArray = Encoding.ASCII.GetBytes("admin:1234");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }
            var url = "http://"+ StopLightIp + "/script?run" + script + "=run";
            return httpClient.GetStringAsync(url);
          
        }
        const string AllOff = "003";
        const string RedOn = "011";
        const string RedOff = "005";
        const string YelloOff = "015";
        const string GreenOn = "031";
        const string YelloOn = "021";
        const string GreenOff = "025";

        byte[] ConvertToMessageData ( int[] leds , int[] mask)
        {
            var output = new List<byte>();
            for ( int i =0;i< leds.Length;i++)
            {
                
                if (leds.Length == mask?.Length && leds[i] == mask[i] )
                {
                    continue;
                }

                output.Add((byte)i);
                var rgb = IntToRGB(leds[i]);
                output.AddRange(rgb);

            }
            return output.ToArray();
        }

        //timer 
        private void _t_Tick(object sender, EventArgs e)
        {
            if(!Enable)
            {
                return ;
            }
            int ret = BassWasapi.BASS_WASAPI_GetData(_fft, (int)BASSData.BASS_DATA_FFT2048); //get channel fft data
            if (ret < -1) return;




            int level = BassWasapi.BASS_WASAPI_GetLevel();


            _l.Value = Utils.LowWord32(level);
            _r.Value = Utils.HighWord32(level);



            var left = (float)Utils.LowWord32(level) / 32768; ;
            var right = (float)Utils.HighWord32(level) / 32768;
            powerHistory.AddFirst(new Tuple<float, DateTime>(left, DateTime.Now));
            if ((powerHistory.First().Item2 - powerHistory.Last().Item2).TotalMilliseconds > 8 && powerHistory.Count > 200)
            {
                powerHistory.RemoveLast();
            }


            if (this.Visuualizer == "ChaseOut")
            {
                Chase(true);
            }
            else if (this.Visuualizer == "ChaseIn")
            {
                Chase(false);
            }
            else if (this.Visuualizer == "ColorSlider")
            {
                ColorSlider();
            }
            else if ( this.Visuualizer == "SmallGains")
            {
                SmallGains();
            }
            else if (this.Visuualizer == "RainbowGains")
            {
                RainbowGains();
            }
            else if (this.Visuualizer == "StopLight")
            {
                StopLight();
            }
            else
            {
                AnimateCenterRightLeft();
            }


            if (level == _lastlevel && level != 0) _hanctr++;
            _lastlevel = level;

            //Required, because some programs hang the output. If the output hangs for a 75ms
            //this piece of code re initializes the output so it doesn't make a gliched sound for long.
            if (_hanctr > 3)
            {
                _hanctr = 0;
                _l.Value = 0;
                _r.Value = 0;
                Free();
                Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
                _initialized = false;
                Enable = true;
            }
        }

        static float GetPrecentile(IEnumerable<float> sequence, float comp)
        {
            var seq = sequence.OrderBy(x => x).ToArray();

            for (int i =0; i < seq.Length; i++)
            {
                if ( seq[i] > comp)
                {
                    return (((float)i) +1) / (float)seq.Length;
                }
            }

            return 1f;


        }

        static float Percentile(IEnumerable<float> sequence, float excelPercentile)
        {
            var seq = sequence.OrderBy(x => x).ToArray();
            int N = seq.Length;
            float n = (N - 1) * excelPercentile + 1;
            // Another method: double n = (N + 1) * excelPercentile;
            if (n == 1d) return seq[0];
            else if (n == N) return seq[N - 1];
            else
            {
                int k = (int)n;
                float d = n - k;
                return seq[k - 1] + d * (seq[k] - seq[k - 1]);
            }
        }

        static int colorwheel(int pos, float power =1)
        {
            pos = pos % 255;
            if (pos < 85) { return rgb2Int( (int)( 255 - pos * 3 * power)   , 0, (int) ( pos * 3 * power)); }
            else if (pos < 170) { pos -= 85; return rgb2Int(0, (int)(pos * 3 * power), (int)( power *255 - pos * 3)); }
            else { pos -= 170; return rgb2Int((int)(power*pos * 3), (int)(power* 255 - pos * 3), 0); }
        }

        static int colorwheel(int pos)
        {
            pos = pos % 255;
            if (pos < 85) { return rgb2Int(255 - pos * 3, 0, pos * 3); }
            else if (pos < 170) { pos -= 85; return rgb2Int(0, pos * 3, 255 - pos * 3); }
            else { pos -= 170; return rgb2Int(pos * 3, 255 - pos * 3, 0); }
        }

        static int rgb2Intx(int r, int g, int b)
        {
            g = Convert.ToInt32((float)g * 1.7);
            var wheelVal = 0;
            if ( r > b  && r > g)
            {
                if ( r < 80)
                {
                    return 0;
                }
                if ( g > b )
                {
                    wheelVal = 85 - Convert.ToInt32  ((((float)r) / 255F) * 85)  ;
                }
                else
                {
                    wheelVal = 255 - Convert.ToInt32((((float)r) / 255F) * 85);
                }
            }
            else if ( g > b )
            {
                if (g < 80)
                {
                    return 0;
                }
                if ( b > r)
                {
                    wheelVal = 86 + Convert.ToInt32((((float)g) / 255F) * 85 );
                }
                else
                {
                    wheelVal = 86 - Convert.ToInt32((((float)g) / 255F) * 85 + 170);
                }
               
            }
            else
            {
                if (b < 80)
                {
                    return 0;
                }
                if ( r > g )
                {
                    wheelVal = 170 + Convert.ToInt32((((float)b) / 255F) * 85);
                }
                else
                {
                    wheelVal = 170 - Convert.ToInt32((((float)b) / 255F) * 85);
                }
            }

            return colorwheel(wheelVal);


          
        }



        static int rgb2Int(int r, int g, int b)
        {
            return ((r & 0xff) << 16) + ((g & 0xff) << 8) + (b & 0xff);
        }
        static int rgb2Int(byte r, byte g, byte b)
        {
            return ((r & 0xff) << 16) + ((g & 0xff) << 8) + (b & 0xff);
        }
        static byte[] IntToRGB(int color)
        {
            var x = 4;
            byte y = (byte)x;
            var r = (byte)((color >> 16) & 0xff);
            var g = (byte)((color >> 8) & 0xff);
            var b = (byte)(color & 0xff);
            return new byte[] { r, g, b };
        }
        



        // WASAPI callback, required for continuous recording
        private int Process(IntPtr buffer, int length, IntPtr user)
        {
            return length;
        }

        //cleanup
        public void Free()
        {
            BassWasapi.BASS_WASAPI_Free();
            Bass.BASS_Free();
        }
    }
}
