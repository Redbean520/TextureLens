using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// Harmony 补丁集中处（2.6.0 重写）。只做两件事：
    /// 1) Dialog_InfoCard 右上角补一个 T 按钮 → 打开高清贴图查看窗；
    /// 2) Verse.Root.OnGUI 末尾消费 QuickViewController 投递的目标并安全开窗。
    /// 旧 2.5.0 里对已不存在的 WindowStack.BringToFront 的补丁已彻底删除。
    /// </summary>
    internal static class HarmonyPatches
    {
        private const string HarmonyId = "redbean.texturelens";
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                Harmony harmony = new Harmony(HarmonyId);
                MethodInfo infoCard = AccessTools.Method(typeof(Dialog_InfoCard), "DoWindowContents", new[] { typeof(Rect) });
                MethodInfo rootGui = AccessTools.Method(typeof(Root), "OnGUI");

                if (infoCard == null)
                {
                    Log.Error("[ItemTextureViewer] 未找到 Dialog_InfoCard.DoWindowContents，信息卡 T 按钮不可用。");
                }
                else
                {
                    harmony.Patch(infoCard, postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(InfoCardPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                if (rootGui == null)
                {
                    Log.Error("[ItemTextureViewer] 未找到 Verse.Root.OnGUI，全局快速查看快捷键不可用。");
                }
                else
                {
                    harmony.Patch(rootGui, postfix: new HarmonyMethod(typeof(HarmonyPatches).GetMethod(nameof(RootOnGuiPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // 2.8.0：让离屏预览复用地图绘制管线（动画 mod 的补丁才会生效）。
                RenderHooks.Apply(harmony);

                if (infoCard != null && rootGui != null) Log.Message("[ItemTextureViewer] Harmony 补丁已应用。");
            }
            catch (Exception ex)
            {
                Log.Error("[ItemTextureViewer] 应用 Harmony 补丁失败: " + ex);
            }
        }

        private static void InfoCardPostfix(Dialog_InfoCard __instance, Rect inRect)
        {
            try
            {
                if (__instance == null) return;
                object target = InfoCardUtil.GetTarget(__instance);
                if (target == null) return;

                ItemTextureViewerSettings s = ItemTextureViewerMod.Settings;
                float offX = s != null ? s.btnOffsetX : 0f;
                float offY = s != null ? s.btnOffsetY : 0f;
                Rect rect = new Rect(inRect.xMax - 44f + offX, inRect.y + 30f + offY, 24f, 24f);
                if (UIStyles.DrawButton(rect, "T", false, true, "ItemTextureViewer_ViewTextureTooltip".Translate()))
                {
                    Find.WindowStack.Add(new TextureViewerWindow(ViewerTarget.From(target)));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[ItemTextureViewer] 信息卡 T 按钮异常: " + ex, 738291);
            }
        }

        private static void RootOnGuiPostfix()
        {
            object pending = ItemTextureViewerMod.PendingTarget;
            if (pending == null) return;
            ItemTextureViewerMod.PendingTarget = null;
            try
            {
                ViewerTarget target = ViewerTarget.From(pending);
                if (target != null) Find.WindowStack.Add(new TextureViewerWindow(target));
            }
            catch (Exception ex)
            {
                Log.Error("[ItemTextureViewer] 快速查看开窗失败: " + ex);
            }
        }
    }
}
