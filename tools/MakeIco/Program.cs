// Converts a set of PNG files into a proper multi-size .ico with BMP DIB frames.
// Usage: dotnet run -- <output.ico> <input1.png> [input2.png ...]

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MakeIco <output.ico> <input1.png> ...");
    return 1;
}

var icoPath  = args[0];
var pngPaths = args.Skip(1).ToArray();

// Build DIB bytes for one frame
static byte[] MakeDib(string pngPath)
{
    using var src = new Bitmap(pngPath);
    int w = src.Width, h = src.Height;

    // Ensure 32bpp ARGB
    using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
        g.DrawImage(src, 0, 0, w, h);

    var rect = new Rectangle(0, 0, w, h);
    var bd   = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    int stride = bd.Stride; // should be w*4 for 32bpp
    var raw  = new byte[stride * h];
    Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
    bmp.UnlockBits(bd);

    // AND mask: one bit per pixel, rows padded to 4-byte boundary, bottom-to-top
    int andRowBytes = ((w + 31) / 32) * 4;

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // BITMAPINFOHEADER (40 bytes)
    bw.Write((int)40);       // biSize
    bw.Write((int)w);        // biWidth
    bw.Write((int)(h * 2));  // biHeight × 2 (XOR + AND mask)
    bw.Write((short)1);      // biPlanes
    bw.Write((short)32);     // biBitCount
    bw.Write((int)0);        // biCompression = BI_RGB
    bw.Write((int)0);        // biSizeImage
    bw.Write((int)0);        // biXPelsPerMeter
    bw.Write((int)0);        // biYPelsPerMeter
    bw.Write((int)0);        // biClrUsed
    bw.Write((int)0);        // biClrImportant

    // XOR mask: rows bottom-to-top, BGRA byte order
    for (int row = h - 1; row >= 0; row--)
    {
        int rowBase = row * stride;
        for (int col = 0; col < w; col++)
        {
            int i = rowBase + col * 4;
            // raw is BGRA (GDI stores as B,G,R,A)
            bw.Write(raw[i]);     // B
            bw.Write(raw[i + 1]); // G
            bw.Write(raw[i + 2]); // R
            bw.Write(raw[i + 3]); // A
        }
    }

    // AND mask: all zeros (fully transparent via alpha; Windows uses alpha channel)
    var andMask = new byte[andRowBytes * h];
    bw.Write(andMask);

    bw.Flush();
    return ms.ToArray();
}

var frames = pngPaths.Select(p => (path: p, dib: MakeDib(p), size: new Bitmap(p).Width)).ToList();

using var outMs = new MemoryStream();
using var outBw = new BinaryWriter(outMs);

// ICONDIR header
outBw.Write((ushort)0);               // reserved
outBw.Write((ushort)1);               // type = ICO
outBw.Write((ushort)frames.Count);    // image count

// Data starts after header (6) + directory entries (16 × count)
uint dataOffset = (uint)(6 + frames.Count * 16);

// ICONDIRENTRY for each frame
foreach (var (_, dib, size) in frames)
{
    byte w = size >= 256 ? (byte)0 : (byte)size;
    byte h = size >= 256 ? (byte)0 : (byte)size;
    outBw.Write(w);
    outBw.Write(h);
    outBw.Write((byte)0);    // color count
    outBw.Write((byte)0);    // reserved
    outBw.Write((ushort)1);  // planes
    outBw.Write((ushort)32); // bit depth
    outBw.Write((uint)dib.Length);
    outBw.Write(dataOffset);
    dataOffset += (uint)dib.Length;
}

// Image data
foreach (var (_, dib, _) in frames)
    outBw.Write(dib);

outBw.Flush();
File.WriteAllBytes(icoPath, outMs.ToArray());

// Validate
using var icon = new Icon(icoPath);
Console.WriteLine($"OK: {icoPath} — {frames.Count} frames, {new FileInfo(icoPath).Length} bytes, largest={icon.Width}x{icon.Height}");
return 0;
