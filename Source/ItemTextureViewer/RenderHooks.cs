using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 让「预览窗/查看窗的离屏渲染」复用游戏在地图上那条真实的绘制管线（含动画 mod 的补丁）。
    ///
    /// 之前的问题是：预览走 PawnRenderer.RenderCache（立绘路径），而地图上的人物/装备动画（如 Yayo's Animation）
    /// 是挂在「地图绘制管线」上的——ParallelGetPreRenderResults 里算身体角度/位置偏移、DrawEquipmentAndApparelExtras 里
    /// 算武器摆动。离屏渲染不走那条路，所以画面是静止的。
    ///
    /// 2.8.0 的做法：渲染时打开 ForceImmediate 开关，去调游戏自己的「预渲染 + 渲染树绘制」，同时把两处「延迟提交」改成立即绘制：
    ///   1. GenDraw.DrawMeshNowOrLater(..., drawNow: false) → 强制 true（人物身上每个节点）；
    ///   2. PawnRenderUtility.DrawEquipmentAiming（手里的武器）→ 用 Graphics.DrawMeshNow 复刻原版逻辑。
    /// 开关平时是 false，地图绘制完全不受影响。
    /// </summary>
    public static class RenderHooks
    {
        /// <summary>为 true 时（仅在我们的离屏渲染期间），所有「延迟提交」的绘制都改成立即绘制。</summary>
        public static bool ForceImmediate;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo nowOrLaterLoc = AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
                    new[] { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material), typeof(bool) });
                MethodInfo nowOrLaterMat = AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
                    new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(bool), typeof(MaterialPropertyBlock) });
                MethodInfo drawAiming = AccessTools.Method(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming));

                if (nowOrLaterLoc != null)
                {
                    harmony.Patch(nowOrLaterLoc, prefix: new HarmonyMethod(typeof(RenderHooks).GetMethod(nameof(ForceDrawNowPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                if (nowOrLaterMat != null)
                {
                    harmony.Patch(nowOrLaterMat, prefix: new HarmonyMethod(typeof(RenderHooks).GetMethod(nameof(ForceDrawNowPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                if (drawAiming != null)
                {
                    harmony.Patch(drawAiming, prefix: new HarmonyMethod(typeof(RenderHooks).GetMethod(nameof(DrawEquipmentAimingPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                }
            }
            catch (Exception e)
            {
                Log.Error("[ItemTextureViewer] 注册渲染钩子失败: " + e);
            }
        }

        private static void ForceDrawNowPrefix(ref bool drawNow)
        {
            if (ForceImmediate)
            {
                drawNow = true;
            }
        }

        /// <summary>返回 false 表示跳过原版 DrawEquipmentAiming（原版最后一步是延迟提交，在这个渲染时机进不了 RT）。</summary>
        private static bool DrawEquipmentAimingPrefix(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            if (!ForceImmediate)
            {
                return true;
            }
            try
            {
                DrawEquipmentNow(eq, drawLoc, aimAngle);
            }
            catch (Exception)
            {
                // 画武器失败不影响人物本体
            }
            return false;
        }

        /// <summary>复刻原版 DrawEquipmentAiming，只把最后的 Graphics.DrawMesh 换成 DrawMeshNow。</summary>
        private static void DrawEquipmentNow(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            float num = aimAngle - 90f;
            Mesh mesh;
            if (aimAngle > 20f && aimAngle < 160f)
            {
                mesh = MeshPool.plane10;
                num += eq.def.equippedAngleOffset;
            }
            else if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                num -= 180f;
                num -= eq.def.equippedAngleOffset;
            }
            else
            {
                mesh = MeshPool.plane10;
                num += eq.def.equippedAngleOffset;
            }
            num %= 360f;

            CompEquippable compEquippable = eq.TryGetComp<CompEquippable>();
            if (compEquippable != null)
            {
                EquipmentUtility.Recoil(eq.def, EquipmentUtility.GetRecoilVerb(compEquippable.AllVerbs), out Vector3 drawOffset, out float angleOffset, aimAngle);
                drawLoc += drawOffset;
                num += angleOffset;
            }

            Material material = eq.Graphic is Graphic_StackCount stack
                ? stack.SubGraphicForStackCount(1, eq.def).MatSingleFor(eq)
                : eq.Graphic.MatSingleFor(eq);
            if (material == null)
            {
                return;
            }
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), new Vector3(eq.Graphic.drawSize.x, 1f, eq.Graphic.drawSize.y));
            if (material.SetPass(0))
            {
                Graphics.DrawMeshNow(mesh, matrix);
            }
        }
    }
}