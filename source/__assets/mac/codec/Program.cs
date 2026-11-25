using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ImageMagick;

const string nativeName = "Magick.Native-Q16-HDRI-x64.dll.dylib";

if (args is ["--check-exports", var assembly, var exports])
{
    using var stream = File.OpenRead(assembly);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var required = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var handle in metadata.MethodDefinitions)
    {
        var method = metadata.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
            continue;

        var import = method.GetImport();
        var module = metadata.GetModuleReference(import.Module);
        if (metadata.GetString(module.Name) == "Magick.Native-Q16-HDRI-x64.dll")
            required.Add(metadata.GetString(import.Name));
    }

    var actual = File.ReadLines(exports).Select(symbol => symbol.TrimStart('_')).ToHashSet(StringComparer.Ordinal);
    var missing = required.Where(symbol => !actual.Contains(symbol)).ToArray();
    Check(required.Count == 697, $"Unexpected Magick.NET P/Invoke count: {required.Count}");
    Check(missing.Length == 0, $"Missing native exports: {string.Join(", ", missing)}");
    Console.WriteLine($"All {required.Count} x64 Magick.NET P/Invoke entry points are exported.");
    return;
}

if (args.Length == 0)
    throw new ArgumentException("Supply the expected ImageMagick version and optional image paths.");

var version = MagickNET.ImageMagickVersion;
Check(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Probe must run on x86_64.");
Check(version.StartsWith($"ImageMagick {args[0]} Q16-HDRI x86_64 ", StringComparison.Ordinal), version);
Check(Quantum.Depth == 16 && Quantum.Max == 65535f, "Unexpected quantum depth or range.");
Check(!MagickNET.Features.Split(' ').Contains("OpenMP"), "Unexpected OpenMP build.");
var delegates = MagickNET.Delegates.Split(' ', StringSplitOptions.RemoveEmptyEntries);
foreach (var required in new[] { "heic", "jxl", "lcms", "openexr", "raw", "tiff" })
    Check(delegates.Contains(required, StringComparer.Ordinal), $"Missing {required} delegate.");

var loadedPaths = Dyld.ImagePaths().Where(path => Path.GetFileName(path) == nativeName).ToArray();
Check(loadedPaths.Length == 1, $"Expected one mapped Magick.Native library. Found {string.Join(", ", loadedPaths)}.");
var loadedPath = new FileInfo(loadedPaths[0]);
var expectedPath = new FileInfo(Path.Combine(AppContext.BaseDirectory, nativeName));
Check(PhysicalPath(loadedPath) == PhysicalPath(expectedPath), $"Unexpected Magick.Native path: {loadedPath}");
using var dylib = File.OpenRead(loadedPath.FullName);
Console.WriteLine($"{MagickNET.Version}\n{version}\n{MagickNET.Features}\n{MagickNET.Delegates}");
Console.WriteLine($"{loadedPath.FullName} SHA-256={Convert.ToHexString(SHA256.HashData(dylib)).ToLowerInvariant()}");

using var gradient = new MagickImage("gradient:black-white", 1, 1024)
{
    Depth = 16,
};
gradient.SetProfile(ColorProfiles.SRGB);
#if MAGICK_15
using (var normalized = new MagickImage(gradient))
    normalized.Normalize(Channels.Red);
#endif
using var secondPage = new MagickImage(MagickColors.Blue, 1, 1024);
using var frames = new MagickImageCollection { new MagickImage(gradient), new MagickImage(secondPage) };
using var tiff = new MemoryStream();
frames.Write(tiff, MagickFormat.Tiff);
tiff.Position = 0;
using var decoded = new MagickImageCollection(tiff);
Check(decoded.Count == 2, "TIFF frame count changed.");
Check(decoded[0].Depth == 16, "TIFF lost 16-bit depth.");
Check(decoded[0].GetColorProfile()?.ToByteArray().SequenceEqual(ColorProfiles.SRGB.ToByteArray()) == true,
    "TIFF ICC profile did not round-trip.");
using var pixels = decoded[0].GetPixelsUnsafe();
float[] area = pixels.GetArea(0, 0, 1, 1024) ?? throw new InvalidOperationException("Missing TIFF pixels.");
Check(area.Distinct().Count() > 256, "TIFF lost precision.");
Console.WriteLine($"TIFF round-trip: {decoded.Count} pages, {decoded[0].Depth} bits, {area.Distinct().Count()} distinct float samples.");

using var hdr = new MagickImage(MagickColors.Black, 4, 4)
{
    ColorSpace = ColorSpace.RGB,
};
using (var sourcePixels = hdr.GetPixelsUnsafe())
{
    var sourceRed = sourcePixels.GetChannelIndex(PixelChannel.Red) ?? throw new InvalidOperationException("Missing source red.");
    var source = sourcePixels.GetArea(0, 0, 1, 1) ?? throw new InvalidOperationException("Missing source pixels.");
    source[sourceRed] = 2 * Quantum.Max;
    sourcePixels.SetPixel(0, 0, source);
}
using var exr = new MagickImage(hdr.ToByteArray(MagickFormat.Exr));
using var hdrPixels = exr.GetPixelsUnsafe();
var redIndex = hdrPixels.GetChannelIndex(PixelChannel.Red) ?? throw new InvalidOperationException("EXR lost the red channel.");
var rgba = hdrPixels.GetArea(0, 0, 1, 1) ?? throw new InvalidOperationException("Missing EXR pixels.");
var linear = rgba[redIndex] / Quantum.Max;
Check(linear > 1f, $"EXR clamped HDR red to {linear}.");
Console.WriteLine($"Linear HDR EXR red after round-trip: {linear}.");

foreach (var file in args.Skip(1))
{
    using var images = new MagickImageCollection(file);
    Check(images.Count > 0 && images[0].Width > 0 && images[0].Height > 0, $"Empty image: {file}");
    var image = images[0];
    Console.WriteLine($"{Path.GetFileName(file)}: {image.Format} {image.Width}x{image.Height} frames={images.Count} depth={image.Depth} ICC={image.GetColorProfile() is not null}");
}

static string PhysicalPath(FileInfo file)
{
    var path = file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
    return path.StartsWith("/tmp/", StringComparison.Ordinal) ? "/private" + path : path;
}

static void Check(bool passed, string description)
{
    if (!passed)
        throw new InvalidOperationException(description);
}

internal static partial class Dyld
{
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";

    [LibraryImport(SystemLibrary, EntryPoint = "_dyld_image_count")]
    private static partial uint Count();

    [LibraryImport(SystemLibrary, EntryPoint = "_dyld_get_image_name")]
    private static partial nint GetName(uint index);

    internal static IEnumerable<string> ImagePaths()
    {
        var count = Count();
        for (uint index = 0; index < count; index++)
        {
            if (Marshal.PtrToStringUTF8(GetName(index)) is { } path)
                yield return path;
        }
    }
}
