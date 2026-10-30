using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ModHub;

/// <summary>
/// Nexus serves every picture as WebP, which the game can't load. Windows can (WIC, the codec behind Photos):
/// this turns a WebP file into plain RGBA pixels, bottom row first as Unity expects. Returns null if Windows can't.
/// </summary>
internal static class WebP
{
    public static bool IsWebP(byte[] d) =>
        d != null && d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';

    /// <summary>Decodes a WebP file (path) to RGBA32 pixels. Call from a background thread.</summary>
    public static byte[] Decode(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED for this worker thread (harmless if already done)
            var clsid = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");   // CLSID_WICImagingFactory
            var iid = typeof(IWICImagingFactory).GUID;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var obj));
            var factory = (IWICImagingFactory)obj;
            factory.CreateDecoderFromFilename(path, IntPtr.Zero, 0x80000000, 0, out var decoder);
            decoder.GetFrame(0, out var frame);
            factory.CreateFormatConverter(out var converter);
            var rgba = new Guid("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");      // GUID_WICPixelFormat32bppRGBA
            converter.Initialize(frame, ref rgba, 0, IntPtr.Zero, 0.0, 0);
            converter.GetSize(out uint w, out uint h);
            if (w == 0 || h == 0 || w > 4096 || h > 4096) return null;
            int stride = (int)w * 4;
            var top = new byte[stride * (int)h];
            var pin = GCHandle.Alloc(top, GCHandleType.Pinned);
            try { converter.CopyPixels(IntPtr.Zero, (uint)stride, (uint)top.Length, pin.AddrOfPinnedObject()); }
            finally { pin.Free(); }
            Marshal.ReleaseComObject(converter); Marshal.ReleaseComObject(frame); Marshal.ReleaseComObject(decoder); Marshal.ReleaseComObject(factory);

            // Unity textures start at the bottom row.
            var bottom = new byte[top.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(top, y * stride, bottom, ((int)h - 1 - y) * stride, stride);
            width = (int)w; height = (int)h;
            return bottom;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogDebug($"WebP decode failed ({Path.GetFileName(path)}): {e.Message}");
            return null;
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);

    // Only the members we call are used, but every earlier slot must be declared, in order.
    [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        void CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string filename, IntPtr vendor, uint access, int options, out IWICBitmapDecoder decoder);
        void CreateDecoderFromStream();
        void CreateDecoderFromFileHandle();
        void CreateComponentInfo();
        void CreateDecoder();
        void CreateEncoder();
        void CreatePalette();
        void CreateFormatConverter(out IWICFormatConverter converter);
    }

    [ComImport, Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        void QueryCapability();
        void Initialize();
        void GetContainerFormat();
        void GetDecoderInfo();
        void CopyPalette();
        void GetMetadataQueryReader();
        void GetPreview();
        void GetColorContexts();
        void GetThumbnail();
        void GetFrameCount();
        void GetFrame(uint index, out IWICBitmapFrameDecode frame);
    }

    [ComImport, Guid("3B16811B-6A43-4ec9-A813-3D930C13B940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameDecode
    {
        // IWICBitmapSource
        void GetSize(out uint width, out uint height);
        void GetPixelFormat(out Guid format);
        void GetResolution(out double dpiX, out double dpiY);
        void CopyPalette(IntPtr palette);
        void CopyPixels(IntPtr rect, uint stride, uint size, IntPtr buffer);
    }

    [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter
    {
        // IWICBitmapSource
        void GetSize(out uint width, out uint height);
        void GetPixelFormat(out Guid format);
        void GetResolution(out double dpiX, out double dpiY);
        void CopyPalette(IntPtr palette);
        void CopyPixels(IntPtr rect, uint stride, uint size, IntPtr buffer);
        // IWICFormatConverter
        void Initialize(IWICBitmapFrameDecode source, ref Guid format, int dither, IntPtr palette, double alpha, int paletteType);
    }
}
