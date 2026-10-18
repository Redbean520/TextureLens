using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 角色立绘统一入口：封装 PortraitsCache 的尺寸/镜头参数，并处理 RT 过期（PortraitsCache 会销毁过期 RT）。
    /// </summary>
    public static class PortraitProvider
    {
        public static readonly int[] Sizes = { 128, 256, 512, 1024, 2048 };
        private static bool warned;

        public static float CameraZoomFor(int size)
        {
            if (size >= 2048) return 0.3f;
            if (size >= 1024) return 0.5f;
            if (size >= 512) return 0.7f;
            if (size >= 256) return 0.9f;
            return 1.2f;
        }

        public static RenderTexture Get(Pawn pawn, Rot4 rotation, int size)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return null;
            }
            if (!rotation.IsValid)
            {
                rotation = Rot4.South;
            }
            try
            {
                // PortraitsCache 自己会缓存 1 秒（CacheDuration），只按间隔重复调用 Get 拿到的还是同一张旧图，
                // 所以「预览刷新间隔」设到 0.1 秒也没用，pawn 的动作 / 受伤闪烁 / 装备变化都不同步。
                // 每次刷新前标记为脏，让原版管线用同一张 RT 原地重画一次：刷新间隔才真正生效，也不会触发 RT 回收复用。
                PortraitsCache.SetDirty(pawn);
                return PortraitsCache.Get(pawn, new Vector2(size, size), rotation, cameraZoom: CameraZoomFor(size));
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[ItemTextureViewer] 生成角色预览失败: " + e);
                }
                return null;
            }
        }

        public static bool IsUsable(RenderTexture rt)
        {
            return rt != null && rt.IsCreated();
        }
    }
}
