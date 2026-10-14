using System.Drawing;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoundPartyCompanion.App.Infrastructure;
using Xunit;
using Forms = System.Windows.Forms;
using Color = System.Drawing.Color;

namespace SoundPartyCompanion.Tests;

public sealed class TrayPresentationTests
{
    [Fact]
    public void MenuContentHasInsetAndKeepsDiagnosticsAndShadowFix()
    {
        RunSta(() =>
        {
            using var service = new TrayIconService(() => { }, _ => { }, () => { },
                () => { }, () => { }, () => { }, () => { });
            var menu = (Forms.ContextMenuStrip)typeof(TrayIconService)
                .GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            Assert.False(menu.DropShadowEnabled);
            var createParams = (Forms.CreateParams)typeof(Forms.Control)
                .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
            Assert.Equal(0, createParams.ClassStyle & 0x00020000);
            var items = menu.Items.OfType<Forms.ToolStripMenuItem>().ToArray();
            Assert.Equal(7, items.Length);
            foreach (var item in items)
            {
                Assert.Equal(22, item.Padding.Left);
                Assert.Equal(7, item.Padding.Top);
                Assert.Equal(9, item.Padding.Right);
                Assert.Equal(menu.Width, item.Width);
            }
            var header = menu.Items.OfType<Forms.ToolStripLabel>().First();
            var status = menu.Items.OfType<Forms.ToolStripLabel>().Last();
            Assert.Equal(27, header.Padding.Left);
            Assert.Equal(45, status.Margin.Left);
            Assert.Equal(32, menu.ImageScalingSize.Width);
            var diagnosticItem = Assert.Single(items, item => item.Text?.Contains("导出诊断", StringComparison.Ordinal) == true);
            Assert.True(diagnosticItem.Enabled);
            service.SetDiagnosticExportEnabled(false);
            Assert.False(diagnosticItem.Enabled);
            service.SetDiagnosticExportEnabled(true);
            Assert.True(diagnosticItem.Enabled);

            // Inspect the actual WinForms text rectangles, not just padding values.
            var textRectangles = new Dictionary<Forms.ToolStripItem, Rectangle>();
            var labelTextRectangles = new Dictionary<Forms.ToolStripItem, Rectangle>();
            menu.Renderer.RenderItemText += (_, args) =>
            {
                if (args.Item is Forms.ToolStripMenuItem) textRectangles[args.Item] = args.TextRectangle;
                if (args.Item is Forms.ToolStripLabel) labelTextRectangles[args.Item] = args.TextRectangle;
            };
            using var bitmap = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
            Assert.Equal(items.Length, textRectangles.Count);
            Assert.Single(textRectangles.Values.Select(rectangle => rectangle.Left).Distinct());
            Assert.All(textRectangles.Values, rectangle => Assert.True(rectangle.Left >= 22,
                $"Text must have clear space inside the 9-pixel surface inset: {rectangle}."));
            foreach (var item in items)
            {
                var rectangle = textRectangles[item];
                Assert.True(Math.Abs(rectangle.Top + rectangle.Height / 2d - item.Height / 2d) <= 0.5,
                    $"Text region must be vertically centered in its row: text={rectangle}, rowHeight={item.Height}.");
                AssertInkCentered(bitmap, new Rectangle(item.Bounds.Left + rectangle.Left,
                    item.Bounds.Top, rectangle.Width, item.Height), 2);
            }
            var headerTextLeft = header.Bounds.Left + labelTextRectangles[header].Left;
            var statusTextLeft = status.Bounds.Left + labelTextRectangles[status].Left;
            Assert.True(Math.Abs(headerTextLeft - statusTextLeft) <= 2,
                $"Header/status text should align: header={headerTextLeft}, status={statusTextLeft}.");
            var previewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "artifacts", "test-results", "tray-menu-preview.png"));
            bitmap.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
        });
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void SelectionSurfaceRetainsInsetAtSupportedDpi(int dpi)
    {
        RunSta(() =>
        {
            using var service = new TrayIconService(() => { }, _ => { }, () => { },
                () => { }, () => { }, () => { }, () => { });
            var menu = (Forms.ContextMenuStrip)typeof(TrayIconService)
                .GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            var item = menu.Items.OfType<Forms.ToolStripMenuItem>().First();
            var scale = dpi / 96f;
            item.Size = new Size((int)(300 * scale), (int)(40 * scale));
            using var bitmap = new Bitmap(item.Width, item.Height);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);
            var background = Color.FromArgb(40, 42, 40);
            graphics.Clear(background);
            menu.Renderer.DrawMenuItemBackground(new Forms.ToolStripItemRenderEventArgs(graphics, item));
            var inset = (int)Math.Round(9 * scale);
            Assert.Equal(background.ToArgb(), bitmap.GetPixel(inset - 1, item.Height / 2).ToArgb());
            Assert.NotEqual(background.ToArgb(), bitmap.GetPixel((int)(22 * scale), item.Height / 2).ToArgb());
            Assert.Equal(background.ToArgb(), bitmap.GetPixel(item.Width - inset + 1, item.Height / 2).ToArgb());
            Rectangle? textRectangle = null;
            Forms.TextFormatFlags? textFormat = null;
            menu.Renderer.RenderItemText += (_, args) =>
            {
                textRectangle = args.TextRectangle;
                textFormat = args.TextFormat;
            };
            menu.Renderer.DrawItemText(new Forms.ToolStripItemTextRenderEventArgs(graphics, item,
                item.Text ?? string.Empty, new Rectangle(8, 0, item.Width - 16, item.Height),
                Color.White, item.Font, ContentAlignment.MiddleLeft));
            Assert.NotNull(textRectangle);
            Assert.Equal((int)Math.Round(22 * scale), textRectangle.Value.Left);
            Assert.Equal((int)Math.Round(22 * scale), item.Width - textRectangle.Value.Right);
            Assert.True(Math.Abs(textRectangle.Value.Top + textRectangle.Value.Height / 2d - item.Height / 2d) <= 0.5,
                $"At {dpi} DPI text must be vertically centered: {textRectangle.Value} in height {item.Height}.");
            Assert.True(textFormat!.Value.HasFlag(Forms.TextFormatFlags.VerticalCenter));
            AssertInkCentered(bitmap, new Rectangle(textRectangle.Value.Left, 0, textRectangle.Value.Width, item.Height), 2 * scale);
            var toggle = menu.Items.OfType<Forms.ToolStripMenuItem>().Single(row => row.Tag as string == "toggle");
            toggle.Size = item.Size;
            menu.Renderer.DrawItemText(new Forms.ToolStripItemTextRenderEventArgs(graphics, toggle,
                toggle.Text ?? string.Empty, new Rectangle(8, 0, toggle.Width - 16, toggle.Height),
                Color.White, toggle.Font, ContentAlignment.MiddleLeft));
            Assert.Equal((int)Math.Round(57 * scale), toggle.Width - textRectangle.Value.Right);
        });
    }

    [Fact]
    public void PackagedIconHasTransparentSpeakerFramesAtAllSizes()
    {
        RunSta(() =>
        {
            var uri = new Uri("/SoundPartyCompanion.App;component/Assets/app.ico", UriKind.Relative);
            using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 },
                decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray());
            foreach (var frame in decoder.Frames)
            {
                Assert.Equal(frame.PixelWidth, frame.PixelHeight);
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var stride = converted.PixelWidth * 4;
                var pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);
                Assert.Equal(0, pixels[3]);
                var bodyIndex = (frame.PixelHeight / 2 * frame.PixelWidth + frame.PixelWidth / 2) * 4;
                Assert.Equal(255, pixels[bodyIndex + 3]);
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index =>
                    pixels[index * 4 + 2] > 150 && pixels[index * 4 + 1] < 150 && pixels[index * 4 + 3] > 200);
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Tray presentation test timed out.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void AssertInkCentered(Bitmap bitmap, Rectangle row, double tolerance)
    {
        var top = int.MaxValue;
        var bottom = int.MinValue;
        for (var y = row.Top; y < Math.Min(row.Bottom, bitmap.Height); y++)
        {
            for (var x = row.Left; x < Math.Min(row.Right, bitmap.Width); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 180 || pixel.G < 180 || pixel.B < 180) continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        Assert.True(top != int.MaxValue, "Expected visible text pixels in each menu row.");
        var inkCenter = (top + bottom + 1) / 2d;
        var rowCenter = row.Top + row.Height / 2d;
        Assert.True(Math.Abs(inkCenter - rowCenter) <= tolerance,
            $"Visible text must be centered: ink={top}..{bottom}, row={row}, offset={inkCenter - rowCenter}.");
    }
}
