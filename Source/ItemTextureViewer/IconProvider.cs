using System;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 一张"游戏里看到的那张图"：纹理 + 可选 UI 材质（遮罩/双色上色）+ 颜色 + 旋转角 + 图标比例。
    /// </summary>
    public struct IconInfo
    {
        public Texture texture;
        public Material material;
        public Color color;
        public float angle;
        public Vector2 proportions;

        public bool valid => texture != null && texture != BaseContent.BadTex;
        public int width => texture != null ? texture.width : 0;
        public int height => texture != null ? texture.height : 0;
    }

    /// <summary>
    /// 直接用游戏自己的图标绘制管线（Widgets.GetIconFor / GetIconFor(ThingDef)）解析纹理，
    /// 因此拿到的是 Graphic 按实例 DrawColor 上色、带 _m 遮罩之后的结果，和物品栏 / 提示框里的图标完全一致。
    /// 每次 Repaint 都重新解析，可让"材质随时间切换"的动画图形也动起来（shader 驱动的动画由材质绘制本身驱动）。
    /// </summary>
    public static class IconProvider
    {
        private static readonly Vector2 IconSize = new Vector2(512f, 512f);
        private static bool warned;

        public static IconInfo GetForThing(Thing thing)
        {
            IconInfo info = default(IconInfo);
            info.color = Color.white;
            info.proportions = Vector2.one;
            if (thing == null || thing.def == null)
            {
                return info;
            }

            try
            {
                info.texture = Widgets.GetIconFor(thing, IconSize, null, false, out _, out float angle, out Vector2 proportions, out Color color, out Material material);
                info.angle = angle;
                info.proportions = proportions;
                info.color = color;
                info.material = material;
            }
            catch (Exception e)
            {
                Warn("thing:" + thing.def.defName, e);
            }
            Normalize(ref info);
            return info;
        }

        public static IconInfo GetForDef(ThingDef def, ThingDef stuff = null, ThingStyleDef style = null)
        {
            IconInfo info = default(IconInfo);
            info.color = Color.white;
            info.proportions = Vector2.one;
            if (def == null)
            {
                return info;
            }

            try
            {
                info.texture = Widgets.GetIconFor(def, out Material material, stuff, style);
                info.material = material;
                info.angle = def.uiIconAngle;
                if (def.graphicData != null)
                {
                    info.proportions = def.graphicData.drawSize;
                }
            }
            catch (Exception e)
            {
                Warn("def:" + def.defName, e);
            }
            Normalize(ref info);
            return info;
        }

        /// <summary>材质来自全局 MaterialPool 共享缓存，这里没有自建缓存需要释放；保留 API 供设置页按钮使用。</summary>
        public static void ClearCaches()
        {
            warned = false;
        }

        private static void Normalize(ref IconInfo info)
        {
            if (info.texture == null)
            {
                return;
            }
            if (info.proportions.x <= 0.0001f || info.proportions.y <= 0.0001f)
            {
                info.proportions = new Vector2(Mathf.Max(1, info.texture.width), Mathf.Max(1, info.texture.height));
            }
        }

        private static void Warn(string key, Exception e)
        {
            if (warned)
            {
                return;
            }
            warned = true;
            Log.WarningOnce("[ItemTextureViewer] 解析图标失败 (" + key + "): " + e.Message, 0x1A2B3C);
        }
    }
}
