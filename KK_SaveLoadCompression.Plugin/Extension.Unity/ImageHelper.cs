using System.IO;
using System.Reflection;
using UnityEngine;

namespace Extension
{
    public static class UnityImageHelper
    {
        public static Sprite LoadNewSprite(string FilePath, int width = -1, int height = -1, float PixelsPerUnit = 100.0f)
        {
            Texture2D SpriteTexture = LoadTexture(FilePath, width, height);
            if (null == SpriteTexture || SpriteTexture.width == 0)
            {
                SpriteTexture = LoadDllResourceToTexture2D(FilePath, width, height);
            }
            return Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), Vector2.zero, PixelsPerUnit);
        }

        public static Texture2D LoadTexture(string FilePath, int width = -1, int height = -1)
        {
            byte[] FileData;
            if (File.Exists(FilePath))
            {
                FileData = File.ReadAllBytes(FilePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (texture.LoadImage(FileData))
                {
                    if ((width > 0 && texture.width != width) || (height > 0 && texture.height != height))
                    {
                        texture = texture.Scale(width > 0 ? width : texture.width, height > 0 ? height : texture.height, mipmap: false);
                    }
                    return texture;
                }
            }
            return null;
        }

        public static Texture2D LoadDllResourceToTexture2D(string FilePath, int width = -1, int height = -1)
        {
            Assembly myAssembly = Assembly.GetExecutingAssembly();
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            using (Stream myStream = myAssembly.GetManifestResourceStream(FilePath))
            {
                if (texture.LoadImage(ImageHelper.ReadToEnd(myStream)))
                {
                    if ((width > 0 && texture.width != width) || (height > 0 && texture.height != height))
                    {
                        texture = texture.Scale(width > 0 ? width : texture.width, height > 0 ? height : texture.height, mipmap: false);
                    }
                    return texture;
                }
                else
                {
                    Logger.LogError($"Missing Dll resource: {FilePath}");
                }
            }
            return null;
        }

        public static Texture2D Scale(this Texture2D src, int width, int height, FilterMode mode = FilterMode.Trilinear, bool mipmap = true)
        {
            Rect texR = new Rect(0, 0, width, height);
            _gpu_scale(src, width, height, mode);

            Texture2D result = new Texture2D(width, height, src.format, mipmap);
            result.Resize(width, height);
            result.ReadPixels(texR, 0, 0, true);
            result.Apply(true);
            return result;
        }

        /// <summary>
        /// Scale proportionally by width.
        /// </summary>
        public static Texture2D Scale(this Texture2D src, int width, FilterMode mode = FilterMode.Trilinear, bool mipmap = true)
        {
            float ratio = (float)width / src.width;
            int height = (int)(src.height * ratio);
            return Scale(src, width, height, mode, mipmap);
        }

        private static void _gpu_scale(Texture2D src, int width, int height, FilterMode fmode = FilterMode.Trilinear)
        {
            src.filterMode = fmode;
            src.Apply(true);

            RenderTexture rtt = new RenderTexture(width, height, 32);
            Graphics.SetRenderTarget(rtt);
            GL.LoadPixelMatrix(0, 1, 1, 0);
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.DrawTexture(new Rect(0, 0, 1, 1), src);
        }

        public static Texture2D OverwriteTexture(this Texture2D background, Texture2D watermark, int startX, int startY)
        {
            Texture2D newTex = new Texture2D(background.width, background.height, background.format, false);
            for (int x = 0; x < background.width; x++)
            {
                for (int y = 0; y < background.height; y++)
                {
                    if (x >= startX && y >= startY && x - startX < watermark.width && y - startY < watermark.height)
                    {
                        Color bgColor = background.GetPixel(x, y);
                        Color wmColor = watermark.GetPixel(x - startX, y - startY);
                        Color final_color = Color.Lerp(bgColor, wmColor, wmColor.a);
                        final_color.a = bgColor.a + wmColor.a;
                        newTex.SetPixel(x, y, final_color);
                    }
                    else
                    {
                        newTex.SetPixel(x, y, background.GetPixel(x, y));
                    }
                }
            }
            newTex.Apply();
            return newTex;
        }
    }
}
