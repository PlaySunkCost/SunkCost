using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SunkCost.Audio
{
    internal static class NativeAudioBridge
    {
        private const string Library = "AudioPluginSunkCostAudio";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_master(float value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern float sc_output_peak();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint sc_mix_callbacks();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_init();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_refresh();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_count(int mic);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sc_name(int mic, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_id(int mic, int index, StringBuilder value, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_default(int mic, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_capture_start(int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_capture_stop();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_capture_read([Out] float[] data, int samples);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_capture_available();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_output_start(int index, int sampleRate);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_output_stop();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_output_write([In] float[] data, int samples);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint sc_output_callbacks();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint sc_output_nonzero();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_shutdown();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sc_encoder_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_encoder_destroy(IntPtr encoder);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_encode(IntPtr encoder, [In] float[] pcm, [Out] byte[] packet);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sc_decoder_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void sc_decoder_destroy(IntPtr decoder);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sc_decode(IntPtr decoder, [In] byte[] packet, int size, [Out] float[] pcm);
    }
}
