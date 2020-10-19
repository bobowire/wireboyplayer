using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace WireboyPlayer
{
    public sealed unsafe class AudioStreamDecoder : IDisposable
    {
        private readonly AVCodecContext* _pVideoCodecContext;
        private readonly AVFormatContext* _pFormatContext;
        private readonly int _streamVideoIndex;
        private readonly int _streamAudioIndex;
        private readonly AVFrame* _pFrame;
        private readonly AVFrame* _receivedFrame;
        private readonly AVPacket* _pPacket;


        private readonly AVCodecContext* _pAudioCodecContext;
        public AVSampleFormat in_sample_fmt;
        public int in_sample_rate;
        public ulong in_ch_layout;
        public int in_channels;
        public long in_start_time;

        public AudioStreamDecoder(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            _pFormatContext = ffmpeg.avformat_alloc_context();
            _receivedFrame = ffmpeg.av_frame_alloc();
            var pFormatContext = _pFormatContext;
            ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
            ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
            AVCodec* videoCodec = null;
            _streamVideoIndex = ffmpeg.av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &videoCodec, 0).ThrowExceptionIfError();
            _pVideoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
            if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                ffmpeg.av_hwdevice_ctx_create(&_pVideoCodecContext->hw_device_ctx, HWDeviceType, null, null, 0).ThrowExceptionIfError();
            }
            ffmpeg.avcodec_parameters_to_context(_pVideoCodecContext, _pFormatContext->streams[_streamVideoIndex]->codecpar).ThrowExceptionIfError();
            if (_pFormatContext->streams[_streamVideoIndex]->avg_frame_rate.den != 0)
            {
                Fps = _pFormatContext->streams[_streamVideoIndex]->avg_frame_rate.num / _pFormatContext->streams[_streamVideoIndex]->avg_frame_rate.den;
                Console.WriteLine("计算得到FPS");
            }
            else
            {
                Console.WriteLine("默认FPS");
                Fps = 25;
            }
            ffmpeg.avcodec_open2(_pVideoCodecContext, videoCodec, null).ThrowExceptionIfError();

            CodecName = ffmpeg.avcodec_get_name(videoCodec->id);
            FrameSize = new Size(_pVideoCodecContext->width, _pVideoCodecContext->height);
            PixelFormat = _pVideoCodecContext->pix_fmt;

            _pPacket = ffmpeg.av_packet_alloc();
            _pFrame = ffmpeg.av_frame_alloc();



            AVCodec* audioCodec = null;
            _streamAudioIndex = ffmpeg.av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audioCodec, 0).ThrowExceptionIfError();
            _pAudioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
            ffmpeg.avcodec_parameters_to_context(_pAudioCodecContext, _pFormatContext->streams[_streamAudioIndex]->codecpar).ThrowExceptionIfError();
            ffmpeg.avcodec_open2(_pAudioCodecContext, audioCodec, null).ThrowExceptionIfError();
            if (_streamAudioIndex > 0)
            {
                AVStream* avs = _pFormatContext->streams[_streamAudioIndex];
                Console.WriteLine($"codec_id:{avs->codecpar->codec_id}");
                Console.WriteLine($"format:{avs->codecpar->format}");
                Console.WriteLine($"sample_rate:{avs->codecpar->sample_rate}");
                Console.WriteLine($"channels:{avs->codecpar->channels}");
                Console.WriteLine($"frame_size:{avs->codecpar->frame_size}");
                in_sample_fmt = _pAudioCodecContext->sample_fmt;
                in_sample_rate = _pAudioCodecContext->sample_rate;//输入的采样率
                in_ch_layout = _pAudioCodecContext->channel_layout;//输入的声道布局
                in_channels = _pAudioCodecContext->channels;
                in_start_time = avs->start_time;
            }
        }

        public int Fps { set; get; }

        public string CodecName { get; }
        public Size FrameSize { get; }
        public AVPixelFormat PixelFormat { get; }

        public void Dispose()
        {
            ffmpeg.av_frame_unref(_pFrame);
            ffmpeg.av_free(_pFrame);

            ffmpeg.av_packet_unref(_pPacket);
            ffmpeg.av_free(_pPacket);

            ffmpeg.avcodec_close(_pVideoCodecContext);
            ffmpeg.avcodec_close(_pAudioCodecContext);
            var pFormatContext = _pFormatContext;
            ffmpeg.avformat_close_input(&pFormatContext);
        }

        public bool TryDecodeNextFrame(out AVFrame frame, out bool isVideo)
        {
            isVideo = true;
            ffmpeg.av_frame_unref(_pFrame);
            ffmpeg.av_frame_unref(_receivedFrame);
            int error;
            AVCodecContext* currentCodecContext = _pVideoCodecContext;
            do
            {
                try
                {
                    do
                    {
                        error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);
                        if (error == ffmpeg.AVERROR_EOF)
                        {
                            frame = *_pFrame;
                            return false;
                        }

                        error.ThrowExceptionIfError();
                    } while (_pPacket->stream_index != _streamVideoIndex && _pPacket->stream_index != _streamAudioIndex);
                    isVideo = _pPacket->stream_index == _streamVideoIndex;
                    if (!isVideo)
                    {
                        currentCodecContext = _pAudioCodecContext;
                    }
                    ffmpeg.avcodec_send_packet(currentCodecContext, _pPacket).ThrowExceptionIfError();
                }
                finally
                {
                    ffmpeg.av_packet_unref(_pPacket);
                }
                error = ffmpeg.avcodec_receive_frame(currentCodecContext, _pFrame);
            } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
            error.ThrowExceptionIfError();
            if (currentCodecContext->hw_device_ctx != null)
            {
                ffmpeg.av_hwframe_transfer_data(_receivedFrame, _pFrame, 0).ThrowExceptionIfError();
                frame = *_receivedFrame;
            }
            else
            {
                frame = *_pFrame;
            }
            return true;
        }

        public IReadOnlyDictionary<string, string> GetContextInfo()
        {
            AVDictionaryEntry* tag = null;
            var result = new Dictionary<string, string>();
            while ((tag = ffmpeg.av_dict_get(_pFormatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringAnsi((IntPtr)tag->key);
                var value = Marshal.PtrToStringAnsi((IntPtr)tag->value);
                result.Add(key, value);
            }

            return result;
        }
    }
}