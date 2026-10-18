using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 管理离屏渲染出来的 RenderTexture 缓存。
    /// 关键点：
    /// 1. 静态图形只渲染一次，之后每帧只是一次 GUI.DrawTexture —— 这是 2.6.2 解决"大窗口移动很卡"的核心；
    /// 2. 动画图形（_AgeSecs / _Working / 自定义动画 Graphic）按约 45Hz 重渲染，保证动画跟游戏同步。
    /// </summary>
    public static class ThingImageProvider
    {
        private const int MaxDimension = 1024;
        private const int MaxEntries = 4;
        private const float AnimatedInterval = 1f / 45f;

        private class Entry
        {
            public RenderTexture rt;
            public float lastRender = -9999f;
            public int lastTick = -1;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        private static readonly List<string> Order = new List<string>();
        private static bool warned;

        /// <summary>取（必要时渲染）某张图形在当前朝向下的贴图。返回 null 表示离屏渲染不可用，调用方应回退图标管线。</summary>
        public static RenderTexture Get(Graphic graphic, Thing thing, ThingDef def, Rot4 rot, out bool animated)
        {
            animated = false;
            if (graphic == null || ThingImageRenderer.Failed)
            {
                return null;
            }

            try
            {
                animated = IsAnimated(graphic);
                Rot4 useRot = rot.IsValid ? rot : Rot4.South;

                Vector2 drawSize = graphic.drawSize;
                if (drawSize.x <= 0.01f || drawSize.y <= 0.01f)
                {
                    drawSize = Vector2.one;
                }

                float aspect = drawSize.x / Mathf.Max(0.0001f, drawSize.y);
                int width;
                int height;
                if (aspect >= 1f)
                {
                    width = MaxDimension;
                    height = Mathf.Max(32, Mathf.RoundToInt(MaxDimension / aspect));
                }
                else
                {
                    height = MaxDimension;
                    width = Mathf.Max(32, Mathf.RoundToInt(MaxDimension * aspect));
                }

                // Graphic 不是 UnityEngine.Object（没有 GetInstanceID），用 类型 + 贴图路径 + 颜色 + 实例 + 朝向 + 尺寸 组成稳定 key。
                Color gc = graphic.color;
                string key = graphic.GetType().Name + "|" + graphic.path + "|" +
                    ((int)(gc.r * 255f)) + "," + ((int)(gc.g * 255f)) + "," + ((int)(gc.b * 255f)) + "," + ((int)(gc.a * 255f)) + "|" +
                    (thing != null ? thing.thingIDNumber : 0) + "|" + useRot.AsInt + "|" + (def != null ? def.shortHash : 0) + "|" + width + "x" + height;
                Entry entry = GetEntry(key);
                float now = Time.realtimeSinceStartup;
                // 游戏暂停 / 时间静止时图形状态不会变，没必要按 45Hz 重渲染。
                int tick = Find.TickManager != null ? Find.TickManager.TicksGame : -1;

                if (entry.rt == null || !entry.rt.IsCreated() || entry.rt.width != width || entry.rt.height != height)
                {
                    DestroyEntry(entry);
                    entry.rt = CreateTexture(width, height, graphic.path);
                    entry.lastRender = -9999f;
                }

                bool tickChanged = tick < 0 || tick != entry.lastTick;
                bool needRender = entry.lastRender < 0f
                    || (animated && tickChanged && now - entry.lastRender >= AnimatedInterval);
                if (needRender)
                {
                    if (!ThingImageRenderer.Render(entry.rt, graphic, thing, def, useRot))
                    {
                        return null;
                    }
                    entry.lastRender = now;
                    entry.lastTick = tick;
                }
                return entry.rt;
            }
            catch (Exception e)
            {
                Warn(e);
                return null;
            }
        }

        /// <summary>
        /// 是否可能有动画。1.6 原版动画图形都是 Graphic_*_AgeSecs / *_BuildingWorking / _FadesInOut 之类的子类，
        /// 类名带特征词；mod 自写的动画 Graphic 通常也是 Graphic 的自定义子类（不在已知静态基类里），同样按动画处理。
        /// </summary>
        public static bool IsAnimated(Graphic graphic)
        {
            if (graphic == null)
            {
                return false;
            }
            Type type = graphic.GetType();
            string name = type.Name;
            if (name.IndexOf("Age", StringComparison.Ordinal) >= 0
                || name.IndexOf("Anim", StringComparison.Ordinal) >= 0
                || name.IndexOf("Working", StringComparison.Ordinal) >= 0
                || name.IndexOf("Flicker", StringComparison.Ordinal) >= 0
                || name.IndexOf("Pulse", StringComparison.Ordinal) >= 0
                || name.IndexOf("Fade", StringComparison.Ordinal) >= 0
                || name.IndexOf("Tiling", StringComparison.Ordinal) >= 0
                || name.IndexOf("Mote", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            return type != typeof(Graphic_Single)
                && type != typeof(Graphic_Multi)
                && type != typeof(Graphic_Random)
                && type != typeof(Graphic_RandomRotated)
                && type != typeof(Graphic_Appearances)
                && type != typeof(Graphic_StackCount)
                && type != typeof(Graphic_Collection)
                && type != typeof(Graphic_Shadow);
        }

        /// <summary>强制下一次 Get 重新渲染（打开新窗口时用，避免拿到上一个存档里同 ID 物体的旧图）。</summary>
        public static void InvalidateAll()
        {
            foreach (KeyValuePair<string, Entry> pair in Cache)
            {
                pair.Value.lastRender = -9999f;
            }
        }

        /// <summary>销毁全部缓存（设置页按钮用）。</summary>
        public static void ClearCaches()
        {
            foreach (KeyValuePair<string, Entry> pair in Cache)
            {
                DestroyEntry(pair.Value);
            }
            Cache.Clear();
            Order.Clear();
            warned = false;
            ThingImageRenderer.ResetFailures();
        }

        private static Entry GetEntry(string key)
        {
            if (Cache.TryGetValue(key, out Entry entry) && entry != null)
            {
                Order.Remove(key);
                Order.Add(key);
                return entry;
            }

            entry = new Entry();
            Cache[key] = entry;
            Order.Add(key);
            while (Order.Count > MaxEntries)
            {
                string oldest = Order[0];
                Order.RemoveAt(0);
                if (Cache.TryGetValue(oldest, out Entry stale))
                {
                    DestroyEntry(stale);
                    Cache.Remove(oldest);
                }
            }
            return entry;
        }

        private static RenderTexture CreateTexture(int width, int height, string label)
        {
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "ItemTextureViewer_" + (label.NullOrEmpty() ? "texture" : label),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            return rt;
        }

        private static void DestroyEntry(Entry entry)
        {
            if (entry == null || entry.rt == null)
            {
                return;
            }
            try
            {
                entry.rt.Release();
            }
            catch (Exception)
            {
                // 忽略：只影响显存释放时机
            }
            UnityEngine.Object.Destroy(entry.rt);
            entry.rt = null;
        }

        private static void Warn(Exception e)
        {
            if (warned)
            {
                return;
            }
            warned = true;
            Log.WarningOnce("[ItemTextureViewer] 贴图缓存异常: " + e.Message, 0x51AE7C);
        }
    }
}
