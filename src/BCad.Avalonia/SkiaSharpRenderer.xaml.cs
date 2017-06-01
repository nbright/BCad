// copyright

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace BCad.Avalonia
{
    public class SkiaSharpRenderer : UserControl
    {
        public SkiaSharpRenderer()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);
            using (var paint = new SKPaint())
            {
                paint.Color = SKColors.Red;
                canvas.DrawLine(0.0f, 0.0f, 10.0f, 10.0f, paint);
            }
        }
    }
}
