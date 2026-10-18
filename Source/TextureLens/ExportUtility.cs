using System;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// 把当前贴图 / 立绘导出成 PNG。
    /// 带材质（遮罩 / 双色）的图标交给 GPU 用材质合成，因此导出的就是游戏里看到的效果；
    /// 不可读的贴图（例如 .dds）也走 GPU 拷贝，不再因为 isReadable == false 而失败。
    /// </summary>
    public static class ExportUtility
    {
        public static string ExportFolder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "TextureLens_Exports");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        public static void OpenExportFolder()
        {
            try
            {
                Application.OpenURL(new Uri(ExportFolder).AbsoluteUri);
            }
            catch (Exception)
            {
                Application.OpenURL("file://" + ExportFolder);
            }
        }

        /// <summary>
        /// 导出。material 非空时用材质在 GPU 上合成（遮罩 / 颜色由 shader 计算，和游戏内图标一致）；
        /// 否则复制一份纹理再乘 tint。angle 支持 90/180/270 度旋转（对应 ThingDef.uiIconAngle）。
        /// </summary>
        public static bool Export(Texture texture, Color tint, Material material, float angle, string baseName, out string filePath)
        {
            filePath = null;
            if (texture == null)
            {
                return false;
            }

            Texture2D composed = null;
            try
            {
                composed = Compose(texture, tint, material);
                if (composed == null)
                {
                    return false;
                }
                composed = ApplyAngle(composed, angle);
                return Save(composed, baseName, out filePath);
            }
            catch (Exception e)
            {
                Log.Warning("[TextureLens] 导出失败: " + e);
                return false;
            }
            finally
            {
                if (composed != null)
                {
                    UnityEngine.Object.Destroy(composed);
                }
            }
        }

        /// <summary>
        /// 导出已经渲染好的 RenderTexture（离屏相机 / 角色立绘）。会先自动裁掉四周的透明留白，
        /// 因此导出尺寸是图形本身的大小，而不是 1024×1024 的画布。
        /// </summary>
        public static bool Export(RenderTexture source, string baseName, out string filePath)
        {
            filePath = null;
            if (source == null || !source.IsCreated())
            {
                return false;
            }

            Texture2D copy = null;
            try
            {
                int width = source.width;
                int height = source.height;
                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = source;
                    copy = new Texture2D(width, height, TextureFormat.ARGB32, false);
                    copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    copy.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                Texture2D trimmed = CropTransparent(copy);
                if (trimmed != null && !ReferenceEquals(trimmed, copy))
                {
                    UnityEngine.Object.Destroy(copy);
                    copy = trimmed;
                }
                return Save(copy, baseName, out filePath);
            }
            catch (Exception e)
            {
                Log.Warning("[TextureLens] 导出失败: " + e);
                return false;
            }
            finally
            {
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        private static bool Save(Texture2D image, string baseName, out string filePath)
        {
            filePath = null;
            if (image == null)
            {
                return false;
            }
            byte[] png = image.EncodeToPNG();
            if (png == null || png.Length == 0)
            {
                return false;
            }
            string name = Sanitize(baseName);
            filePath = Path.Combine(ExportFolder, name + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            File.WriteAllBytes(filePath, png);
            return true;
        }

        /// <summary>按不透明像素包围盒裁剪；没有可裁的透明边时返回 null（调用方继续用原图）。</summary>
        private static Texture2D CropTransparent(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }
            int width = source.width;
            int height = source.height;
            Color[] pixels = source.GetPixels();
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a > 0.02f)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX || maxY < minY)
            {
                return null;
            }
            if (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1)
            {
                return null;
            }

            int nw = maxX - minX + 1;
            int nh = maxY - minY + 1;
            Color[] cropped = new Color[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                Array.Copy(pixels, (minY + y) * width + minX, cropped, y * nw, nw);
            }
            Texture2D result = new Texture2D(nw, nh, TextureFormat.ARGB32, false);
            result.SetPixels(cropped);
            result.Apply(false, false);
            return result;
        }

        /// <summary>把任意纹理（含不可读 Texture2D / RenderTexture）通过 GPU 拷到一张可读的 Texture2D 上。</summary>
        private static Texture2D Compose(Texture texture, Color tint, Material material)
        {
            int width = texture.width;
            int height = texture.height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                if (material != null)
                {
                    Graphics.Blit(texture, rt, material);
                }
                else
                {
                    Graphics.Blit(texture, rt);
                }

                RenderTexture.active = rt;
                Texture2D copy = new Texture2D(width, height, TextureFormat.ARGB32, false);
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                copy.Apply(false, false);

                if (material == null && !IsWhite(tint))
                {
                    Color[] pixels = copy.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = pixels[i] * tint;
                    }
                    copy.SetPixels(pixels);
                    copy.Apply(false, false);
                }
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }

        /// <summary>按 90 度的整数倍旋转像素；不是 90 的倍数时原样返回。会销毁传入的贴图。</summary>
        private static Texture2D ApplyAngle(Texture2D source, float angle)
        {
            int degrees = (Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 90f) * 90) % 360;
            if (degrees == 0 || source == null)
            {
                return source;
            }

            int w = source.width;
            int h = source.height;
            Color[] pixels = source.GetPixels();
            bool swap = degrees == 90 || degrees == 270;
            int nw = swap ? h : w;
            int nh = swap ? w : h;
            Color[] rotated = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int nx;
                    int ny;
                    switch (degrees)
                    {
                        case 90:
                            nx = h - 1 - y;
                            ny = x;
                            break;
                        case 180:
                            nx = w - 1 - x;
                            ny = h - 1 - y;
                            break;
                        default:
                            nx = y;
                            ny = w - 1 - x;
                            break;
                    }
                    rotated[ny * nw + nx] = pixels[y * w + x];
                }
            }

            Texture2D result = new Texture2D(nw, nh, TextureFormat.ARGB32, false);
            result.SetPixels(rotated);
            result.Apply(false, false);
            UnityEngine.Object.Destroy(source);
            return result;
        }

        private static bool IsWhite(Color c)
        {
            return c.r >= 0.99f && c.g >= 0.99f && c.b >= 0.99f && c.a >= 0.99f;
        }

        private static string Sanitize(string name)
        {
            if (name.NullOrEmpty())
            {
                return "texture";
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            string cleaned = builder.ToString().Trim();
            if (cleaned.Length == 0)
            {
                cleaned = "texture";
            }
            if (cleaned.Length > 60)
            {
                cleaned = cleaned.Substring(0, 60);
            }
            return cleaned;
        }
    }
}
