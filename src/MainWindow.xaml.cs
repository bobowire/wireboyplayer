using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NAudio.Wave;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace WireboyPlayer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        PlayerHelper helper = new PlayerHelper();
        MainWindowViewModel model = null;
        public MainWindow()
        {
            InitializeComponent();
            Console.SetOut(new TextBoxWriter(logBox));
            model = new MainWindowViewModel();
            model.PlayerImage = new BitmapImage();
            helper = new PlayerHelper();
            helper.playStatusCallback = PlayStatusCallBack;
            this.DataContext = model;
        }

        BitmapSource tempSource = null;
        public void SetImage(int pixelWidth, int pixelHeight, byte[] img, int stride, bool isTemp)
        {
            try
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    //DateTime cur = DateTime.Now;
                    if (isTemp)
                    {
                        tempSource = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgr24, null, img, stride);
                    }
                    else
                    {
                        if (tempSource != null)
                        {
                            model.PlayerImage = tempSource;
                            tempSource = null;
                        }
                        else
                        {
                            model.PlayerImage = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgr24, null, img, stride);
                        }
                    }
                    //Console.WriteLine($"{cur:ss:ffff}转换耗时：{DateTime.Now.Subtract(cur).TotalMilliseconds}毫秒");
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (btn_Play.Content.ToString() == "播放")
            {
                string url = "";
                if (text_url.SelectedItem == null)
                {
                    url = text_url.Text;
                }
                else
                {
                    url = text_url.SelectedValue.ToString();
                }
                btn_Play.Content = "停止";
                Task.Factory.StartNew(() =>
                {
                    //helper.Start(SetImage, "rtmp://58.200.131.2:1935/livetv/hunantv");
                    helper.Start(SetImage, url);
                });
            }
            else
            {
                helper.Stop();
                model.PlayerImage = new BitmapImage();
                btn_Play.Content = "播放";
            }
        }

        private void PlayStatusCallBack(bool isSuccess)
        {
            //this.Dispatcher.Invoke(() =>
            //{
            //    if (isSuccess)
            //    {
            //        btn_Play.Content = "停止";
            //    }
            //    else
            //    {
            //        btn_Play.Content = "播放";
            //    }
            //});
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (btn_mode.Content.ToString() == "开启直播模式")
            {
                helper.SetPlayMode(true);
                btn_mode.Content = "关闭直播模式";
            }
            else
            {
                helper.SetPlayMode(false);
                btn_mode.Content = "开启直播模式";
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            player.Visibility = Visibility.Hidden;
            logBox.Visibility = Visibility.Visible;
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            player.Visibility = Visibility.Visible;
            logBox.Visibility = Visibility.Hidden;
        }
    }

    public class MainWindowViewModel : PropertyStore
    {
        public BitmapSource PlayerImage
        {
            set { Set(value); }
            get { return Get<BitmapSource>(); }
        }

        public ObservableCollection<RtmpUrlModel> RtmpUrls
        {
            set { Set(value); }
            get { return Get<ObservableCollection<RtmpUrlModel>>(); }
        }

        public MainWindowViewModel()
        {
            RtmpUrls = new ObservableCollection<RtmpUrlModel>();
            RtmpUrls.Add(new RtmpUrlModel()
            {
                url = "rtmp://58.200.131.2:1935/livetv/hunantv",
                name = "湖南卫视"
            });
            RtmpUrls.Add(new RtmpUrlModel()
            {
                url = "http://ivi.bupt.edu.cn/hls/cctv6hd.m3u8",
                name = "CCTV6 - 电影频道"
            });
        }

    }

    public class RtmpUrlModel : PropertyStore
    {
        public string url
        {
            set { Set(value); }
            get { return Get<string>(); }
        }
        public string name
        {
            set { Set(value); }
            get { return Get<string>(); }
        }
    }

    internal class PlayerHelper
    {
        Action<int, int, byte[], int, bool> callBack;
        public Action<bool> playStatusCallback;
        string url;
        int currentGuid = 0;
        public void Start(Action<int, int, byte[], int, bool> action, string url)
        {
            //playStatusCallback(true);
            currentGuid += 1;
            this.url = url;
            callBack = action;
            Console.WriteLine("Current directory: " + Environment.CurrentDirectory);
            Console.WriteLine("Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32");

            FFmpegBinariesHelper.RegisterFFmpegBinaries();

            Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");

            SetupLogging();
            ConfigureHWDecoder(out var deviceType);

            Console.WriteLine("Decoding...");
            bool isError = false;
            int errorTime = 0;
            do
            {
                if (errorTime > 3) break;
                isError = false;
                try
                {
                    DecodeAllFramesToImages(deviceType);
                    errorTime = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"播放异常:{ex}");
                    isError = true;
                    errorTime++;
                }
            } while (isError);
            //playStatusCallback(false);

            //Console.WriteLine("Encoding...");
            //EncodeImagesToH264();
        }
        public void Stop()
        {
            currentGuid += 1;
        }

        bool isLastMode = false;
        /// <summary>
        /// 是否实时模式
        /// </summary>
        /// <param name="isLastMode"></param>
        public void SetPlayMode(bool isLastModeIn)
        {
            isLastMode = isLastModeIn;
        }

        private void ConfigureHWDecoder(out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            Console.WriteLine("Use hardware acceleration for decoding?[n]");
            //var key = Console.ReadLine();
            var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();
            //if (key == "y")
            //{
            Console.WriteLine("Select hardware decoder:");
            var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            var number = 0;
            while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                Console.WriteLine($"{++number}. {type}");
                availableHWDecoders.Add(number, type);
            }
            if (availableHWDecoders.Count == 0)
            {
                Console.WriteLine("Your system have no hardware decoders.");
                HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                return;
            }
            int decoderNumber = availableHWDecoders.SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
            if (decoderNumber == 0)
                decoderNumber = availableHWDecoders.First().Key;
            Console.WriteLine($"Selected [{decoderNumber}]");
            int.TryParse(Console.ReadLine(), out var inputDecoderNumber);
            availableHWDecoders.TryGetValue(inputDecoderNumber == 0 ? decoderNumber : inputDecoderNumber, out HWtype);
            //}
        }

        private unsafe void SetupLogging()
        {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            // do not convert to local function
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(line);
                Console.ResetColor();
            };

            ffmpeg.av_log_set_callback(logCallback);
        }

        List<VideoSt> frames = new List<VideoSt>();
        List<AudioSt> audioFrames = new List<AudioSt>();
        int TempBufferSize = 12;
        bool isWait = true;
        private unsafe void DecodeAllFramesToImages(AVHWDeviceType HWDevice)
        {
            isWait = true;
            frames.Clear();
            audioFrames.Clear();
            // decode all frames from url, please not it might local resorce, e.g. string url = "../../sample_mpeg4.mp4";
            //var url = "http://clips.vorwaerts-gmbh.de/big_buck_bunny.mp4"; // be advised this file holds 1440 frames
            //var url = "rtmp://58.200.131.2:1935/livetv/hunantv";
            int curGuid = currentGuid;
            //FileStream fs = new FileStream("E://tt.mp3", FileMode.OpenOrCreate);
            using (var vsd = new AudioStreamDecoder(url, HWDevice))
            {
                TempBufferSize = vsd.Fps / 2;
                StartVideoThread(vsd.Fps);
                StartAudioThread();
                Console.WriteLine($"FPS:{vsd.Fps}");
                Console.WriteLine($"codec name: {vsd.CodecName}");

                var info = vsd.GetContextInfo();
                info.ToList().ForEach(x => Console.WriteLine($"{x.Key} = {x.Value}"));

                var sourceSize = vsd.FrameSize;
                var sourcePixelFormat = HWDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? vsd.PixelFormat : GetHWPixelFormat(HWDevice);
                //var destinationSize = sourceSize;
                var destinationSize = new Size(1920, 1080);
                var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_BGR24;
                using (VideoFrameConverter vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat))
                {
                    using (AudioFrameConverter afc = new AudioFrameConverter(vsd.in_sample_fmt, vsd.in_sample_rate, vsd.in_channels))
                    {
                        naudioInit(vsd.in_sample_rate, vsd.in_channels);
                        Stopwatch stopwatch = new Stopwatch();
                        while (vsd.TryDecodeNextFrame(out var frame, out bool isVideo))
                        {
                            if (curGuid != currentGuid) break;
                            if (isVideo)
                            {
                                if (!isLastMode)
                                {
                                    while (frames.Count >= TempBufferSize)
                                    {
                                        if (curGuid != currentGuid) break;
                                        Thread.Sleep(1);
                                    }
                                }
                                //stopwatch.Start();
                                AVFrame convertedFrame = vfc.Convert(frame);
                                int length = convertedFrame.height * convertedFrame.linesize[0];
                                byte[] managedArray = IntPrtToBytes((IntPtr)convertedFrame.data[0], length);
                                VideoSt st = new VideoSt()
                                {
                                    data = managedArray,
                                    width = convertedFrame.width,
                                    height = convertedFrame.height,
                                    stride = convertedFrame.linesize[0]
                                };
                                frames.Add(st);
                                if (frames.Count >= TempBufferSize) isWait = false;
                                //stopwatch.Stop();
                                //Console.WriteLine($"解析时间：{stopwatch.ElapsedMilliseconds}毫秒");
                                //stopwatch.Reset();
                            }
                            else
                            {
                                var convertedFrame = afc.Convert(frame);
                                int length = convertedFrame.pkt_size;
                                byte[] managedArray = new byte[0];
                                if (managedArray.Length != length)
                                    managedArray = new byte[length];
                                Marshal.Copy((IntPtr)convertedFrame.data[0], managedArray, 0, managedArray.Length);
                                audioFrames.Add(new AudioSt() { data = managedArray });
                            }
                        }
                    }
                }
            }
            //fs.Close();
        }

        public void naudioInit(int sampleRate, int channels)
        {
            Console.WriteLine("-----------初始化音频设备--------------");
            try
            {
                waveOut = new WaveOut();
                WaveFormat wf = new WaveFormat((int)(sampleRate), channels);
                bufferedWaveProvider = new BufferedWaveProvider(wf);
                bufferedWaveProvider.DiscardOnBufferOverflow = true;
                bufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 0, 0, 500);
                waveOut.Init(bufferedWaveProvider);
                waveOut.Play();
                Console.WriteLine("-----------初始化成功--------------");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        public void addDataToBufferedWaveProvider(byte[] data, int position, int len)
        {
            bufferedWaveProvider.AddSamples(data, position, len);
        }
        WaveOut waveOut;            //播放器
        BufferedWaveProvider bufferedWaveProvider;       //5s缓存区
        class VideoSt
        {
            public byte[] data;
            public int width;
            public int height;
            public int stride;
        }
        class AudioSt
        {
            public byte[] data;
        }
        protected void StartVideoThread(int fps)
        {
            int curGuid = currentGuid;
            Task.Factory.StartNew(() =>
            {
                DateTime lasthandleTime = DateTime.Now;
                byte[] byteZero = new byte[0];
                int dropFrameTime = 0;
                double perFrameTime = 1000 / fps;
                Stopwatch stopwatch = new Stopwatch();
                while (curGuid == currentGuid)
                {
                    stopwatch.Start();
                    VideoSt frameData = null;
                    lasthandleTime = lasthandleTime.AddMilliseconds(perFrameTime);
                    bool isReCalcTime = false;
                    //时间没到时，等待
                    while (lasthandleTime > DateTime.Now || isWait)
                    {
                        if (curGuid != currentGuid) break;
                        Thread.Sleep(1);
                        if (isWait)
                        {
                            lasthandleTime = DateTime.Now.AddMilliseconds(perFrameTime);
                            if (!isReCalcTime)
                            {
                                Console.WriteLine("重新计算播放时间");
                                isReCalcTime = true;
                            }
                        }
                    }
                    if (curGuid != currentGuid) break;
                    if (frames.Count > 0)
                    {
                        //while (frames.Count > TempBufferSize)
                        //{
                        //    Console.WriteLine($"主动丢帧:{dropFrameTime}");
                        //    frames.RemoveAt(0);
                        //}
                        frameData = frames[0];
                        frames.RemoveAt(0);
                    }
                    if (frames.Count <= 1) isWait = true;
                    if (frameData != null)
                    {
                        //Console.WriteLine($"length:{frameData.data.Count()}");
                        callBack(frameData.width, frameData.height, frameData.data, frameData.stride, false);
                        frameData = null;
                    }

                    stopwatch.Stop();
                    if (stopwatch.ElapsedMilliseconds == 0)
                    {
                        Console.WriteLine($"{lasthandleTime:mm:ss:ffff} -> {DateTime.Now:mm:ss:ffff}");
                        Console.WriteLine($"解析时间：{stopwatch.ElapsedMilliseconds}毫秒 wait:{isWait}");
                    }
                    stopwatch.Reset();
                }
            });
        }
        protected void StartAudioThread()
        {
            int curGuid = currentGuid;
            Task.Factory.StartNew(() =>
            {
                DateTime lasthandleTime = DateTime.Now;
                while (curGuid == currentGuid)
                {
                    do
                    {
                        Thread.Sleep(1);
                    } while (isWait);
                    AudioSt frameData = null;
                    if (curGuid != currentGuid) break;
                    lasthandleTime = DateTime.Now;
                    if (audioFrames.Count > 0)
                    {
                        frameData = audioFrames[0];
                        audioFrames.RemoveAt(0);
                    }
                    if (frameData == null) continue;
                    try
                    {
                        addDataToBufferedWaveProvider(frameData.data, 0, frameData.data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"音频播放异常：{ex.Message}");
                    }
                    frameData = null;
                }
            });
        }
        public static BitmapSource ToBitmapImage(byte[] bitmap)
        {
            MemoryStream stream = new MemoryStream(bitmap);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.EndInit();
            return image;
        }
        public static BitmapSource ToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Bmp);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.EndInit();
            return image;
            //IntPtr ptr = bitmap.GetHbitmap();
            //BitmapSource ret = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(ptr, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            //Marshal.FreeCoTaskMem(ptr);
            //return ret;
        }
        public static byte[] IntPrtToBytes(IntPtr buffer, int size)
        {
            byte[] bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return bytes;
        }

        private AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
        {
            switch (hWDevice)
            {
                case AVHWDeviceType.AV_HWDEVICE_TYPE_NONE:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU:
                    return AVPixelFormat.AV_PIX_FMT_VDPAU;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
                    return AVPixelFormat.AV_PIX_FMT_CUDA;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI:
                    return AVPixelFormat.AV_PIX_FMT_VAAPI;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_QSV:
                    return AVPixelFormat.AV_PIX_FMT_QSV;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX:
                    return AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DRM:
                    return AVPixelFormat.AV_PIX_FMT_DRM_PRIME;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL:
                    return AVPixelFormat.AV_PIX_FMT_OPENCL;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC:
                    return AVPixelFormat.AV_PIX_FMT_MEDIACODEC;
                default:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
            }
        }

        //private unsafe void EncodeImagesToH264()
        //{
        //    var frameFiles = Directory.GetFiles(".", "frame.*.jpg").OrderBy(x => x).ToArray();
        //    var fistFrameImage = Image.FromFile(frameFiles.First());

        //    var outputFileName = "out.h264";
        //    var fps = 25;
        //    var sourceSize = fistFrameImage.Size;
        //    var sourcePixelFormat = AVPixelFormat.AV_PIX_FMT_BGR24;
        //    var destinationSize = sourceSize;
        //    var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
        //    using (var vfc = new VideoFrameConverter(sourceSize, sourcePixelFormat, destinationSize, destinationPixelFormat))
        //    {
        //        using (var fs = File.Open(outputFileName, FileMode.Create)) // be advise only ffmpeg based player (like ffplay or vlc) can play this file, for the others you need to go through muxing
        //        {
        //            using (var vse = new H264VideoStreamEncoder(fs, fps, destinationSize))
        //            {
        //                var frameNumber = 0;
        //                foreach (var frameFile in frameFiles)
        //                {
        //                    byte[] bitmapData;

        //                    using (var frameImage = Image.FromFile(frameFile))
        //                    using (var frameBitmap = frameImage is Bitmap bitmap ? bitmap : new Bitmap(frameImage))
        //                    {
        //                        bitmapData = GetBitmapData(frameBitmap);
        //                    }

        //                    fixed (byte* pBitmapData = bitmapData)
        //                    {
        //                        var data = new byte_ptrArray8 { [0] = pBitmapData };
        //                        var linesize = new int_array8 { [0] = bitmapData.Length / sourceSize.Height };
        //                        var frame = new AVFrame
        //                        {
        //                            data = data,
        //                            linesize = linesize,
        //                            height = sourceSize.Height
        //                        };
        //                        var convertedFrame = vfc.Convert(frame);
        //                        convertedFrame.pts = frameNumber * fps;
        //                        vse.Encode(convertedFrame);
        //                    }

        //                    Console.WriteLine($"frame: {frameNumber}");
        //                    frameNumber++;
        //                }
        //            }
        //        }
        //    }
        //}

        //private byte[] GetBitmapData(Bitmap frameBitmap)
        //{
        //    var bitmapData = frameBitmap.LockBits(new Rectangle(Point.Empty, frameBitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        //    try
        //    {
        //        var length = bitmapData.Stride * bitmapData.Height;
        //        var data = new byte[length];
        //        Marshal.Copy(bitmapData.Scan0, data, 0, length);
        //        return data;
        //    }
        //    finally
        //    {
        //        frameBitmap.UnlockBits(bitmapData);
        //    }
        //}
    }


    public class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        protected void DoPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void DoAllPropertyChanged()
        {
            DoPropertyChanged(null);
        }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            DoPropertyChanged(propertyName);
        }
    }
    public class PropertyStore : NotifyPropertyChanged
    {
        readonly Dictionary<string, object> _store = new Dictionary<string, object>();
        protected T Get<T>(T Default = default(T), [CallerMemberName] string propertyName = "")
        {
            lock (_store)
            {
                if (_store.TryGetValue(propertyName, out var obj) && obj is T val)
                {
                    return val;
                }
            }
            return Default;
        }

        protected void Set<T>(T Value, [CallerMemberName] string propertyName = "")
        {
            lock (_store)
            {
                if (_store.ContainsKey(propertyName))
                {
                    _store[propertyName] = Value;
                }
                else
                    _store.Add(propertyName, Value);
            }
            OnPropertyChanged(propertyName);
        }
    }
}
