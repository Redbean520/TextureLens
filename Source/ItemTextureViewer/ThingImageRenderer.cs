using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 用一台独立的离屏相机把 Graphic 画进 RenderTexture（相机参数照抄原版 PawnCacheCameraManager）。
    /// 这样走的是游戏自己的材质 / shader / MaterialPropertyBlock，
    /// 因此 _AgeSecs、_Working、材质切换等驱动出来的动画会随帧变化，颜色也与地图上完全一致，
    /// 而不是像图标管线那样把世界材质换成 UI 材质后丢掉动态参数。
    /// </summary>
    public class ThingImageRenderer : MonoBehaviour
    {
        private const float Padding = 1.12f;

        private static Camera renderCamera;
        private static ThingImageRenderer instance;
        private static bool failed;

        /// <summary>
        /// 某些 Graphic 在 DrawWorker 里会解引用 thing（例如 Graphic_*_AgeSecs 读 thing.TickSpawned）。
        /// 只有 def 没有实例时这些图形会抛异常；记下来，之后直接走图标管线兜底，避免每帧抛一次。
        /// </summary>
        private static readonly HashSet<string> UnsafeGraphics = new HashSet<string>();

        private Graphic graphic;
        private Thing thing;
        private ThingDef thingDef;
        private Rot4 rot;
        private float extraRotation;
        private bool drawing;
        private string pendingKey;
        private bool drawFailed;
        private bool drewThisRender;

        public static bool Failed => failed;

        /// <summary>清空"这个图形不能离屏渲染"的记录（设置页清缓存时一并重置，便于重试）。</summary>
        public static void ResetFailures()
        {
            UnsafeGraphics.Clear();
        }

        /// <summary>把 graphic 渲染进 target。成功返回 true。</summary>
        public static bool Render(RenderTexture target, Graphic graphic, Thing thing, ThingDef thingDef, Rot4 rot)
        {
            if (failed || target == null || graphic == null || instance == null && !EnsureInitialized())
            {
                return false;
            }
            if (renderCamera == null || instance == null)
            {
                return false;
            }

            // 失败记录按「是否带实例」分开：def-only 失败不应该影响之后对真实物体的渲染。
            string key = GraphicKey(graphic) + (thing == null ? "|def" : "|thing");
            if (UnsafeGraphics.Contains(key))
            {
                return false;
            }

            try
            {
                Rot4 useRot = rot.IsValid ? rot : Rot4.South;
                Vector2 drawSize = graphic.drawSize;
                if (drawSize.x <= 0.01f || drawSize.y <= 0.01f)
                {
                    drawSize = Vector2.one;
                }

                // 让相机视野正好框住贴图（正交相机，RT 宽高比与 drawSize 一致，所以画面不会变形）。
                float aspect = target.width / (float)target.height;
                float halfWidth = drawSize.x * 0.5f * Padding;
                float halfHeight = drawSize.y * 0.5f * Padding;
                renderCamera.aspect = aspect;
                renderCamera.orthographicSize = Mathf.Max(0.05f, Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.0001f, aspect)));

                instance.graphic = graphic;
                instance.thing = thing;
                instance.thingDef = thingDef ?? thing?.def;
                instance.rot = useRot;
                instance.extraRotation = -ExtraAngle(graphic, useRot);
                instance.pendingKey = key;
                instance.drawFailed = false;
                instance.drewThisRender = false;

                renderCamera.SetTargetBuffers(target.colorBuffer, target.depthBuffer);
                renderCamera.Render();
                renderCamera.targetTexture = null;

                if (instance.drawFailed)
                {
                    UnsafeGraphics.Add(key);
                    return false;
                }
                // 兜底：手动 Render() 没触发 OnPostRender 时画不出任何东西（2.6.2 的空白就是这么来的），
                // 与其给用户一张全透明的图，不如整条离屏管线停用、让调用方回退到图标管线。
                if (!instance.drewThisRender)
                {
                    failed = true;
                    Log.WarningOnce("[ItemTextureViewer] 离屏相机没有触发绘制回调，改用图标管线。", 0x51AE7D);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Log.Warning("[ItemTextureViewer] 离屏渲染失败，改用图标管线: " + e);
                return false;
            }
            finally
            {
                if (instance != null)
                {
                    instance.graphic = null;
                    instance.thing = null;
                    instance.thingDef = null;
                }
            }
        }

        private void OnPostRender()
        {
            if (graphic == null || drawing)
            {
                return;
            }
            drawing = true;
            drewThisRender = true;
            Graphic_Shadow shadow = null;
            try
            {
                // 不画影子：图标 / 查看窗里不该出现地图上的投影。
                shadow = graphic.ShadowGraphic;
                if (shadow != null)
                {
                    graphic.ShadowGraphic = null;
                }
                if (!DrawNow(graphic, Vector3.zero, rot, thing, extraRotation))
                {
                    drawFailed = true;
                }
            }
            catch (Exception e)
            {
                drawFailed = true;
                Log.WarningOnce("[ItemTextureViewer] 离屏绘制异常: " + e.Message, 0x51AE7B);
            }
            finally
            {
                if (drawFailed && !string.IsNullOrEmpty(pendingKey))
                {
                    UnsafeGraphics.Add(pendingKey);
                }
                if (shadow != null && graphic != null)
                {
                    graphic.ShadowGraphic = shadow;
                }
                drawing = false;
            }
        }

        /// <summary>
        /// 立即绘制一个 Graphic。几何 / 材质 / 朝向的算法与 Verse.Graphic.DrawWorker 逐行一致，
        /// 唯一区别是最后用 Graphics.DrawMeshNow 立刻画进当前渲染目标，而不是 Graphics.DrawMesh 延迟提交。
        /// 2.6.2 贴图全透明的根因就在这里：DrawWorker 内部是延迟提交，网格要等到下一帧的主渲染阶段才画，
        /// 那时 OnPostRender 早已结束、相机也已经切换到别的目标，于是这批网格永远进不了这张 RenderTexture。
        /// </summary>
        /// <returns>false 表示这个 Graphic 没法这样画，调用方应回退到图标管线。</returns>
        private static bool DrawNow(Graphic g, Vector3 loc, Rot4 useRot, Thing thing, float extraRotation)
        {
            if (g == null)
            {
                return false;
            }

            // Graphic_WithPropertyBlock（含 Graphic_Tiling）用 MaterialPropertyBlock 传 shader 参数，
            // 并且绕开 MeshAt 改用 plane10 + TRS 缩放；立即绘制拿不到那些参数，宁可回退图标也不画错画面。
            if (g is Graphic_WithPropertyBlock || g is Graphic_Shadow)
            {
                return false;
            }

            Mesh mesh = g.MeshAt(useRot);
            Material mat = g.MatAt(useRot, thing);
            if (mesh == null || mat == null)
            {
                return false;
            }

            Quaternion quat = g.QuatFromRot(useRot);
            if (extraRotation != 0f)
            {
                quat *= Quaternion.Euler(Vector3.up * extraRotation);
            }
            if (g.data != null && g.data.addTopAltitudeBias)
            {
                quat *= Quaternion.Euler(Vector3.left * 2f);
            }
            loc += g.DrawOffset(useRot);

            if (!mat.SetPass(0))
            {
                return false;
            }
            Graphics.DrawMeshNow(mesh, loc, quat);
            return true;
        }

        private static string GraphicKey(Graphic g)
        {
            return g.GetType().Name + "|" + g.path;
        }

        /// <summary>重算 AngleFromRot（它是 protected）：用来反向抵消朝向旋转，让贴图始终正向显示。</summary>
        private static float ExtraAngle(Graphic g, Rot4 useRot)
        {
            if (g == null || !g.ShouldDrawRotated)
            {
                return 0f;
            }
            float angle = useRot.AsAngle + g.DrawRotatedExtraAngleOffset;
            if ((useRot == Rot4.West && g.WestFlipped) || (useRot == Rot4.East && g.EastFlipped))
            {
                angle += 180f;
            }
            return angle;
        }

        private static bool EnsureInitialized()
        {
            if (failed)
            {
                return false;
            }
            if (renderCamera != null && instance != null)
            {
                return true;
            }

            try
            {
                GameObject host = new GameObject("ItemTextureViewerCamera", typeof(Camera));
                host.SetActive(false);
                instance = host.AddComponent<ThingImageRenderer>();
                UnityEngine.Object.DontDestroyOnLoad(host);

                Camera cam = host.GetComponent<Camera>();
                cam.transform.position = new Vector3(0f, 10f, 0f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.orthographic = true;
                cam.cullingMask = 0;
                cam.orthographicSize = 1f;
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.useOcclusionCulling = false;
                cam.renderingPath = RenderingPath.Forward;
                cam.nearClipPlane = 5f;
                cam.farClipPlane = 12f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                renderCamera = cam;
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Log.Warning("[ItemTextureViewer] 无法创建离屏相机: " + e);
                return false;
            }
        }
    }
}
