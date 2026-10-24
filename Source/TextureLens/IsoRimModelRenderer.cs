using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// 把 IsoRim 的 3D 模型画进我们自己的 RenderTexture：一个专用正交相机 + OnPostRender 里
    /// 逐个网格部件 Graphics.DrawMeshNow。做法和 ThingImageRenderer 的 2D 管线相同，
    /// 区别只是这里用的是 IsoRim 自己的材质，光照/视角靠它的全局 uniform（_IsoView、_IsoSun）。
    /// </summary>
    internal sealed class IsoRimModelRenderer : MonoBehaviour
    {
        private static Camera renderCamera;
        private static IsoRimModelRenderer instance;
        private static bool failed;

        private IsoRimModelView view;
        private bool drewThisRender;

        public static bool Failed
        {
            get { return failed; }
        }

        /// <summary>把 model view 渲染进 rt。失败返回 false（调用方回退 2D 贴图）。</summary>
        public static bool Render(IsoRimModelView target, RenderTexture rt)
        {
            if (failed || target == null || rt == null || !rt.IsCreated())
            {
                return false;
            }
            if (target.parts == null || target.parts.Count == 0)
            {
                return false;
            }
            if (!EnsureInitialized())
            {
                return false;
            }

            // 保存并还原 IsoRim 的全局 uniform：它每帧都会自己再设一次，但别让我们的离屏渲染
            // 在别的相机里留下奇怪的视角/光线。
            Vector4 oldView = Shader.GetGlobalVector("_IsoView");
            Vector4 oldSun = Shader.GetGlobalVector("_IsoSun");
            try
            {
                float aspect = rt.width / (float)Mathf.Max(1, rt.height);
                renderCamera.aspect = aspect;
                renderCamera.orthographic = true;
                renderCamera.orthographicSize = Mathf.Max(0.005f, Mathf.Max(target.halfHeight * 1.08f, target.halfWidth * 1.08f / aspect));

                float distance = Mathf.Max(2f, target.radius * 4f);
                Vector3 eye = target.center + target.faceCamera * distance;
                renderCamera.transform.position = eye;
                renderCamera.transform.rotation = Quaternion.LookRotation(-target.faceCamera, target.up);
                renderCamera.nearClipPlane = Mathf.Max(0.02f, distance - target.radius * 2f);
                renderCamera.farClipPlane = distance + target.radius * 3f + 1f;
                renderCamera.clearFlags = CameraClearFlags.Color;
                renderCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                renderCamera.cullingMask = 0;
                renderCamera.useOcclusionCulling = false;
                renderCamera.renderingPath = RenderingPath.Forward;
                renderCamera.allowHDR = false;
                renderCamera.allowMSAA = false;

                IsoRimBridge.ApplyPreviewLight(target.faceCamera);

                instance.view = target;
                instance.drewThisRender = false;
                renderCamera.SetTargetBuffers(rt.colorBuffer, rt.depthBuffer);
                renderCamera.Render();
                renderCamera.targetTexture = null;

                if (!instance.drewThisRender)
                {
                    failed = true;
                    Log.WarningOnce("[TextureLens] IsoRim 3D 预览的离屏相机没有触发绘制回调，改用 2D 贴图。", 0x51AE84);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Log.WarningOnce("[TextureLens] IsoRim 3D 模型离屏渲染失败，改用 2D 贴图：" + e.Message, 0x51AE85);
                return false;
            }
            finally
            {
                instance.view = null;
                try
                {
                    Shader.SetGlobalVector("_IsoView", oldView);
                    Shader.SetGlobalVector("_IsoSun", oldSun);
                }
                catch (Exception)
                {
                    // 全局 uniform 还原失败不影响什么：IsoRim 每帧会重新设置
                }
            }
        }

        private void OnPostRender()
        {
            drewThisRender = true;
            IsoRimModelView target = view;
            if (target == null || target.parts == null)
            {
                return;
            }

            Material outline = IsoRimBridge.OutlineMaterial;
            Matrix4x4 matrix = target.matrix;
            List<IsoRimPart> parts = target.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                IsoRimPart part = parts[i];
                if (part == null || part.mesh == null)
                {
                    continue;
                }
                Material material = part.outline && outline != null ? outline : part.material;
                if (material == null)
                {
                    continue;
                }
                try
                {
                    if (!material.SetPass(0))
                    {
                        continue;
                    }
                    Graphics.DrawMeshNow(part.mesh, matrix);
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[TextureLens] 画 IsoRim 模型部件失败：" + e.Message, 0x51AE86);
                }
            }
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
                GameObject host = new GameObject("TextureLensModelCamera", typeof(Camera));
                host.SetActive(false);
                instance = host.AddComponent<IsoRimModelRenderer>();
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
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 100f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                renderCamera = cam;
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Log.Warning("[TextureLens] 无法创建 IsoRim 3D 预览相机: " + e);
                return false;
            }
        }
    }
}
