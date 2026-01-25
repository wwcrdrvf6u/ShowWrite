using System.Drawing;
using System.Drawing.Imaging;
using ShowWrite.Models;

namespace ShowWrite.Services
{
    public static class ImageAdjust
    {
        public static Bitmap ApplyAdjustments(Bitmap source, ImageAdjustments adj)
        {
            try
            {
                using var adjusted = new Bitmap(source.Width, source.Height);
                using (var g = Graphics.FromImage(adjusted))
                {
                    float brightness = (adj.Brightness - 100) / 100f;
                    float contrast = adj.Contrast / 100f;
                    var matrix = new ColorMatrix(new float[][]
                    {
                        new float[]{contrast,0,0,0,0},
                        new float[]{0,contrast,0,0,0},
                        new float[]{0,0,contrast,0,0},
                        new float[]{0,0,0,1,0},
                        new float[]{brightness,brightness,brightness,0,1}
                    });
                    using var attrs = new ImageAttributes();
                    attrs.SetColorMatrix(matrix);

                    g.TranslateTransform(source.Width / 2f, source.Height / 2f);
                    g.RotateTransform(adj.Orientation);
                    g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);

                    if (adj.FlipHorizontal)
                    {
                        g.ScaleTransform(-1, 1);
                        g.TranslateTransform(-source.Width, 0);
                    }

                    g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0,
                        source.Width, source.Height, GraphicsUnit.Pixel, attrs);
                }
                return new Bitmap(adjusted);
            }
            catch
            {
                return new Bitmap(source);
            }
        }
    }
}
