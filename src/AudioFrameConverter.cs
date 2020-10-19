using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WireboyPlayer
{
    public sealed unsafe class AudioFrameConverter : IDisposable
    {
        private readonly SwrContext* audio_convert_ctx;
        AVSampleFormat in_sample_fmt;
        int in_sample_rate;
        long in_ch_layout;
        int out_sample_rate = 48000;
        long out_ch_layout = ffmpeg.av_get_default_channel_layout(2);
        int out_nb_channels;
        AVSampleFormat out_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;

        public AudioFrameConverter(AVSampleFormat fmt, int rate, int channels)
        {
            in_sample_fmt = fmt;
            in_sample_rate = rate;
            out_sample_rate = in_sample_rate;
            in_ch_layout = ffmpeg.av_get_default_channel_layout(channels);

            //out_sample_fmt = in_sample_fmt;
            out_ch_layout = ffmpeg.av_get_default_channel_layout(channels);
            out_nb_channels = channels;
            Console.WriteLine($"in_ch_layout:{in_ch_layout}");
            Console.WriteLine($"out_ch_layout:{out_ch_layout}");
            audio_convert_ctx = ffmpeg.swr_alloc();
            ffmpeg.swr_alloc_set_opts(audio_convert_ctx, out_ch_layout, out_sample_fmt, out_sample_rate, in_ch_layout, in_sample_fmt, in_sample_rate, 0, (void*)IntPtr.Zero);
            ffmpeg.swr_init(audio_convert_ctx);
            //out_nb_channels = ffmpeg.av_get_channel_layout_nb_channels((ulong)out_ch_layout);//获取声道个数
        }

        public AVFrame Convert(AVFrame sourceFrame)
        {
            byte_ptrArray8 retArray = new byte_ptrArray8();

            int lineSize = 0;
            int out_size = ffmpeg.av_samples_alloc((byte**)&retArray, &lineSize, out_nb_channels, sourceFrame.nb_samples, out_sample_fmt, 0);
            int reCount = ffmpeg.swr_convert(audio_convert_ctx, (byte**)&retArray, sourceFrame.nb_samples, (byte**)&sourceFrame.data, sourceFrame.nb_samples);
            //int outSize = reCount * sourceFrame.nb_samples * ffmpeg.av_get_bytes_per_sample(out_sample_fmt);

            return new AVFrame()
            {
                data = retArray,
                pkt_size = out_size
            };

        }

        public void Dispose()
        {
            SwrContext* ctx = audio_convert_ctx;
            ffmpeg.swr_free(&ctx);
        }
    }
}
