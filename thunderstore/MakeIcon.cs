// Draws the package's icon (a .NET file-based app; from the repository's root:
// dotnet run --file thunderstore/MakeIcon.cs):
//   thunderstore/icon.png  256 × 256: a spring on a hillside, its stream winding down into a pool
#:package SkiaSharp@4.153.1
#:package SkiaSharp.NativeAssets.Linux@4.153.1
#:package SkiaSharp.NativeAssets.Win32@4.153.1
#:package SkiaSharp.NativeAssets.macOS@4.153.1
using SkiaSharp;

var night = new SKColor(24, 32, 40);
var frame = new SKColor(70, 96, 112);
var hill = new SKColor(74, 98, 62);
var hillLight = new SKColor(104, 132, 80);
var rock = new SKColor(120, 112, 100);
var deep = new SKColor(36, 104, 168);
var water = new SKColor(84, 164, 220);
var foam = new SKColor(214, 238, 250);

using var surface = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
var c = surface.Canvas;
c.Clear(SKColors.Transparent);

using (var round = Path(p => p.AddRoundRect(new SKRect(8, 8, 248, 248), 40, 40)))
{
    using (var paint = new SKPaint { Color = night, IsAntialias = true })
    {
        c.DrawPath(round, paint);
    }

    c.Save();
    c.ClipPath(round, antialias: true);

    // The hillside, high on the left, down to the shore on the right; a lighter ridge on top.
    Fill(c, hill, p =>
    {
        p.MoveTo(0, 70);
        p.CubicTo(60, 60, 110, 120, 170, 170);
        p.CubicTo(200, 195, 230, 200, 256, 200);
        p.LineTo(256, 256);
        p.LineTo(0, 256);
        p.Close();
    });
    Fill(c, hillLight, p =>
    {
        p.MoveTo(0, 70);
        p.CubicTo(60, 60, 110, 120, 170, 170);
        p.CubicTo(120, 135, 70, 88, 0, 92);
        p.Close();
    });

    // The pool at the foot of the hill.
    Fill(c, deep, p => p.AddOval(new SKRect(120, 196, 290, 262)));
    Fill(c, water, p => p.AddOval(new SKRect(140, 204, 280, 236)));

    // The stream: from the spring down to the pool, wider as it goes, with a light line for its current.
    using var course = Path(p =>
    {
        p.MoveTo(52, 92);
        p.CubicTo(100, 100, 40, 140, 96, 160);
        p.CubicTo(150, 178, 120, 205, 176, 214);
    });
    Stroke(c, course, deep, 26);
    Stroke(c, course, water, 16);
    using var current = Path(p =>
    {
        p.MoveTo(60, 94);
        p.CubicTo(98, 104, 52, 140, 100, 156);
        p.CubicTo(146, 172, 126, 200, 168, 210);
    });
    using (var dash = SKPathEffect.CreateDash([14, 12], 0))
    {
        Stroke(c, current, foam, 4, dash);
    }

    // The spring: rocks around it and a drop over it.
    Fill(c, rock, p => p.AddOval(new SKRect(30, 82, 58, 100)));
    Fill(c, rock, p => p.AddOval(new SKRect(52, 80, 76, 96)));
    Fill(c, water, p => p.AddOval(new SKRect(40, 86, 66, 100)));
    c.Restore();
    Stroke(c, round, frame, 6);
}

Fill(c, water, p =>
{
    p.MoveTo(54, 24);
    p.CubicTo(54, 24, 34, 50, 34, 62);
    p.CubicTo(34, 74, 43, 80, 54, 80);
    p.CubicTo(65, 80, 74, 74, 74, 62);
    p.CubicTo(74, 50, 54, 24, 54, 24);
    p.Close();
});
Fill(c, foam, p => p.AddOval(new SKRect(42, 56, 52, 70)));

using var image = surface.Snapshot();
using var png = image.Encode(SKEncodedImageFormat.Png, 100);
using (var file = File.Create("thunderstore/icon.png"))
{
    png.SaveTo(file);
}

Console.WriteLine("thunderstore/icon.png");
return 0;

static SKPath Path(Action<SKPathBuilder> draw)
{
    using var builder = new SKPathBuilder();
    draw(builder);
    return builder.Detach();
}

static void Fill(SKCanvas canvas, SKColor color, Action<SKPathBuilder> draw)
{
    using var path = Path(draw);
    using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    canvas.DrawPath(path, paint);
}

static void Stroke(SKCanvas canvas, SKPath path, SKColor color, float width, SKPathEffect? effect = null)
{
    using var paint = new SKPaint
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        PathEffect = effect,
    };
    canvas.DrawPath(path, paint);
}
