using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// 角色贴图渲染器（2.8.0）：自己造一台离屏相机，用游戏「地图上的那条绘制管线」把 Pawn 画进 RenderTexture。
    ///
    /// 关键点：
    ///   1. 走 PawnRenderer.ParallelGetPreRenderResults（反射调用私有方法）+ PawnRenderTree.Draw，
    ///      因为人物待机动作/身体摆动这类动画，是动画 mod（如 Yayo's Animation）挂在「预渲染结果」上的：
    ///      它们补 PawnRenderer.BodyAngle / GetBodyPos / ParallelGetPreRenderResults 来算角度与位移。
    ///      只调 RenderCache（立绘路径）的话，这些补丁根本不会执行，画面就是静止的。
    ///   2. 传 disableCache=true，强制走「渲染树逐节点绘制」而不是缓存帧。
    ///   3. 绘制期间 RenderHooks.ForceImmediate = true：把所有「延迟提交」的绘制改成立即绘制，
    ///      这样人物节点、手里的武器都能进这张 RT（顺带武器也带上了动画 mod 的摆动）。
    ///   4. 反射失败时退回原版立绘路径（RenderCache），保证至少能显示人物。
    /// </summary>
    public class PawnTextureRenderer : MonoBehaviour
    {
        private static Camera cam;
        private static PawnTextureRenderer inst;
        private static bool warned;

        private static MethodInfo preRenderMethod;
        private static FieldInfo parmsField;
        private static bool reflectionFailed;

        private Pawn pawn;
        private Rot4 rot = Rot4.South;
        private bool drew;

        /// <summary>创建一张用于角色贴图的 RT（24 位深度，ARGB32，双线性过滤）。</summary>
        public static RenderTexture Create(int size)
        {
            int s = Mathf.Clamp(size, 64, 2048);
            RenderTexture rt = new RenderTexture(s, s, 24, RenderTextureFormat.ARGB32)
            {
                name = "TextureLens_Pawn",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// 把 pawn 渲染进 rt。contentZoom 为内容缩放（1 = 原尺寸），cameraOffset 为相机世界偏移（用于平移）。
        /// 返回 false 表示这次没画成功。
        /// </summary>
        public static bool Render(Pawn pawn, RenderTexture rt, Rot4 rot, float contentZoom, Vector3 cameraOffset)
        {
            if (pawn == null || rt == null || !rt.IsCreated() || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }
            if (pawn.Drawer == null || pawn.Drawer.renderer == null)
            {
                return false;
            }
            if (inst == null && !EnsureInitialized())
            {
                return false;
            }

            try
            {
                // 原版 PortraitsCache 的镜头映射：尺寸越大拉得越远，保证人物完整。
                float baseZoom = PortraitProvider.CameraZoomFor(rt.width);
                float zoom = Mathf.Clamp(baseZoom * Mathf.Clamp(contentZoom, 0.25f, 8f), 0.05f, 30f);

                inst.pawn = pawn;
                inst.rot = rot.IsValid ? rot : Rot4.South;
                inst.drew = false;

                cam.transform.position = new Vector3(cameraOffset.x, 10f + cameraOffset.y, cameraOffset.z);
                cam.orthographicSize = 1f / zoom;
                cam.SetTargetBuffers(rt.colorBuffer, rt.depthBuffer);
                cam.Render();
                cam.targetTexture = null;
                cam.transform.position = new Vector3(0f, 10f, 0f);
                cam.orthographicSize = 1f;
                if (!inst.drew)
                {
                    Log.WarningOnce("[TextureLens] 角色相机没有触发绘制回调（立绘会是空的）", 0x51AE7F);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[TextureLens] 角色贴图渲染失败: " + e);
                }
                return false;
            }
            finally
            {
                if (inst != null)
                {
                    inst.pawn = null;
                }
            }
        }

        private void OnPostRender()
        {
            if (pawn == null)
            {
                return;
            }
            drew = true;
            RenderHooks.ForceImmediate = true;
            try
            {
                PawnRenderer renderer = pawn.Drawer.renderer;
                renderer.EnsureGraphicsInitialized();

                PawnDrawParms parms;
                if (!TryGetPreRenderParms(renderer, out parms) || parms.pawn != pawn)
                {
                    // 反射不可用 / 这只 pawn 当前不参与绘制：退回原版立绘（静态但至少有画面）。
                    renderer.RenderCache(rot, 0f, Vector3.zero, true, portrait: false, renderHeadgear: true, renderClothes: true);
                    return;
                }

                // 这里就是地图绘制用的同一套 parms：身体角度、位置偏移、动作状态都已经由各 mod 的补丁算好了。
                renderer.renderTree.Draw(parms);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TextureLens] 角色离屏绘制异常: " + e.Message, 0x51AE7E);
            }
            finally
            {
                RenderHooks.ForceImmediate = false;
            }
        }

        /// <summary>
        /// 反射调用原版私有方法 PawnRenderer.ParallelGetPreRenderResults(drawLoc, rotOverride, neverAimWeapon, disableCache=true)，
        /// 拿到地图绘制用的 PawnDrawParms。动画 mod 的 Harmony 补丁就是挂在这个方法上的，所以调它动画才会动。
        /// </summary>
        private bool TryGetPreRenderParms(PawnRenderer renderer, out PawnDrawParms parms)
        {
            parms = default(PawnDrawParms);
            if (!EnsureReflection())
            {
                return false;
            }
            try
            {
                object boxed = preRenderMethod.Invoke(renderer, new object[] { Vector3.zero, (Rot4?)rot, false, true });
                if (boxed == null)
                {
                    return false;
                }
                object value = parmsField.GetValue(boxed);
                if (value == null)
                {
                    return false;
                }
                parms = (PawnDrawParms)value;
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[TextureLens] 读取角色预渲染参数失败（动画将不可用）: " + e.Message);
                }
                return false;
            }
        }

        private static bool EnsureReflection()
        {
            if (preRenderMethod != null && parmsField != null)
            {
                return true;
            }
            if (reflectionFailed)
            {
                return false;
            }
            try
            {
                preRenderMethod = AccessTools.Method(typeof(PawnRenderer), "ParallelGetPreRenderResults");
                if (preRenderMethod == null)
                {
                    reflectionFailed = true;
                    Log.Warning("[TextureLens] 未找到 PawnRenderer.ParallelGetPreRenderResults，角色动画不可用。");
                    return false;
                }
                parmsField = preRenderMethod.ReturnType.GetField("parms", BindingFlags.Public | BindingFlags.Instance);
                if (parmsField == null)
                {
                    reflectionFailed = true;
                    Log.Warning("[TextureLens] 未找到 PreRenderResults.parms，角色动画不可用。");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                reflectionFailed = true;
                Log.Warning("[TextureLens] 角色预渲染反射初始化失败: " + e.Message);
                return false;
            }
        }

        private static bool EnsureInitialized()
        {
            try
            {
                GameObject host = new GameObject("TextureLensPawnCamera", typeof(Camera));
                host.SetActive(false);
                inst = host.AddComponent<PawnTextureRenderer>();
                UnityEngine.Object.DontDestroyOnLoad(host);

                Camera c = host.GetComponent<Camera>();
                c.transform.position = new Vector3(0f, 10f, 0f);
                c.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                c.orthographic = true;
                c.cullingMask = 0;
                c.orthographicSize = 1f;
                c.clearFlags = CameraClearFlags.Color;
                c.backgroundColor = new Color(0f, 0f, 0f, 0f);
                c.useOcclusionCulling = false;
                c.renderingPath = RenderingPath.Forward;
                c.nearClipPlane = 5f;
                c.farClipPlane = 12f;
                c.allowHDR = false;
                c.allowMSAA = false;
                cam = c;
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[TextureLens] 无法创建角色离屏相机: " + e);
                }
                return false;
            }
        }

        public static void Release(ref RenderTexture rt)
        {
            if (rt == null)
            {
                return;
            }
            try
            {
                rt.Release();
            }
            catch (Exception)
            {
                // 忽略：只影响显存释放时机
            }
            UnityEngine.Object.Destroy(rt);
            rt = null;
        }
    }
}