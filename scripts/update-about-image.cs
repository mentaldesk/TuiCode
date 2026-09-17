#:package SixLabors.ImageSharp@3.1.12

// Regenerates src/TuiCode.Workbench/About/about.rgb.z from assets/about.png: uint16 width and height, then RGB
// rows, zlib-compressed. The editor has no image decoder.

using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

const int width = 640;

var root = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
using var image = Image.Load<Rgb24>(Path.Combine(root, "assets", "about.png"));
image.Mutate(x => x.Resize(width, 0));

using var output = File.Create(Path.Combine(root, "src", "TuiCode.Workbench", "About", "about.rgb.z"));
using var zlib = new ZLibStream(output, CompressionLevel.SmallestSize);
using var writer = new BinaryWriter(zlib);
writer.Write((ushort)image.Width);
writer.Write((ushort)image.Height);
var pixels = new byte[image.Width * image.Height * 3];
image.CopyPixelDataTo(pixels);
writer.Write(pixels);
Console.WriteLine($"Wrote {image.Width}x{image.Height}");
