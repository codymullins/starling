using System.Runtime.InteropServices;
using Tessera.Common.Image;

namespace Tessera.Codecs.Mac;

/// <summary>
/// macOS / Mac Catalyst image decoder built on the ImageIO + Core Graphics
/// frameworks. Decodes any format ImageIO understands (PNG, JPEG, WebP, GIF,
/// HEIC, …) by handing the bytes to a <c>CGImageSource</c>, then drawing the
/// first frame into a freshly-allocated <c>CGBitmapContext</c> with an explicit
/// RGBA8888 layout so the output matches the <see cref="DecodedImage"/>
/// contract exactly — straight (non-premultiplied) alpha, top-down rows,
/// stride == width*4.
/// </summary>
/// <remarks>
/// All Core Foundation objects created here (<c>CFData</c>, <c>CGImageSource</c>,
/// <c>CGImage</c>, <c>CGColorSpace</c>, <c>CGContext</c>) are released in a
/// <c>finally</c> so a decode failure cannot leak. This is one of the two
/// sanctioned interop seams (see AGENTS.md); <c>LibraryImport</c> is allowed.
/// </remarks>
internal sealed partial class ImageIODecoder : IImageDecoder
{
    public unsafe DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        nint data = 0;
        nint source = 0;
        nint cgImage = 0;
        nint colorSpace = 0;
        nint context = 0;
        try
        {
            fixed (byte* p = bytes)
            {
                data = CFDataCreate(0, (nint)p, bytes.Length);
            }
            if (data == 0)
                throw new ImageDecodeException("ImageIO: CFDataCreate returned null.");

            source = CGImageSourceCreateWithData(data, 0);
            if (source == 0)
                throw new ImageDecodeException("ImageIO: CGImageSourceCreateWithData failed (not a recognised image).");

            cgImage = CGImageSourceCreateImageAtIndex(source, 0, 0);
            if (cgImage == 0)
                throw new ImageDecodeException("ImageIO: CGImageSourceCreateImageAtIndex failed (corrupt or unsupported image).");

            nint width = CGImageGetWidth(cgImage);
            nint height = CGImageGetHeight(cgImage);
            if (width <= 0 || height <= 0)
                throw new ImageDecodeException($"ImageIO: decoded image has non-positive dimensions {width}x{height}.");

            colorSpace = CGColorSpaceCreateDeviceRGB();
            if (colorSpace == 0)
                throw new ImageDecodeException("ImageIO: CGColorSpaceCreateDeviceRGB failed.");

            int w = checked((int)width);
            int h = checked((int)height);
            nint stride = (nint)w * 4;

            // Allocate the destination buffer up front and point the bitmap
            // context straight at it: ImageIO writes RGBA8888 with straight
            // (non-premultiplied) alpha, top-down, no row padding — exactly the
            // DecodedImage contract. kCGImageAlphaLast | kCGBitmapByteOrderDefault.
            return DecodedImage.CreatePooled(w, h, span =>
            {
                fixed (byte* dst = span)
                {
                    nint ctx = CGBitmapContextCreate(
                        (nint)dst, width, height,
                        bitsPerComponent: 8,
                        bytesPerRow: stride,
                        space: colorSpace,
                        bitmapInfo: kCGImageAlphaLast | kCGBitmapByteOrderDefault);
                    if (ctx == 0)
                        throw new ImageDecodeException("ImageIO: CGBitmapContextCreate failed.");
                    context = ctx;

                    // CoreGraphics' origin is bottom-left; CGContextDrawImage
                    // would therefore produce a vertically-flipped result. Flip
                    // the CTM so row 0 of the buffer is the top of the image.
                    CGContextTranslateCTM(ctx, 0, height);
                    CGContextScaleCTM(ctx, 1, -1);

                    var rect = new CGRect { X = 0, Y = 0, Width = w, Height = h };
                    CGContextDrawImage(ctx, rect, cgImage);
                    CGContextFlush(ctx);
                }
            });
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ImageDecodeException("ImageIO: native image decode failed.", ex);
        }
        finally
        {
            if (context != 0) CGContextRelease(context);
            if (colorSpace != 0) CGColorSpaceRelease(colorSpace);
            if (cgImage != 0) CGImageRelease(cgImage);
            if (source != 0) CFRelease(source);
            if (data != 0) CFRelease(data);
        }
    }

    // CGImageAlphaInfo.kCGImageAlphaLast == 3 (RGBA, straight alpha).
    private const uint kCGImageAlphaLast = 3;
    // CGBitmapInfo.kCGBitmapByteOrderDefault == 0.
    private const uint kCGBitmapByteOrderDefault = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ImageIO =
        "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataCreate(nint allocator, nint bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);

    [LibraryImport(ImageIO)]
    private static partial nint CGImageSourceCreateWithData(nint data, nint options);

    [LibraryImport(ImageIO)]
    private static partial nint CGImageSourceCreateImageAtIndex(nint source, nint index, nint options);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGImageGetHeight(nint image);

    [LibraryImport(CoreGraphics)]
    private static partial void CGImageRelease(nint image);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport(CoreGraphics)]
    private static partial void CGColorSpaceRelease(nint space);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGBitmapContextCreate(
        nint data, nint width, nint height, nint bitsPerComponent,
        nint bytesPerRow, nint space, uint bitmapInfo);

    [LibraryImport(CoreGraphics)]
    private static partial void CGContextRelease(nint context);

    [LibraryImport(CoreGraphics)]
    private static partial void CGContextTranslateCTM(nint context, double tx, double ty);

    [LibraryImport(CoreGraphics)]
    private static partial void CGContextScaleCTM(nint context, double sx, double sy);

    [LibraryImport(CoreGraphics)]
    private static partial void CGContextDrawImage(nint context, CGRect rect, nint image);

    [LibraryImport(CoreGraphics)]
    private static partial void CGContextFlush(nint context);
}
