using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// IsoRim 3D 模型的一个只读视图：离屏渲染需要的一切（网格部件、朝向、包围盒、相机参数）。
    /// </summary>
    internal sealed class IsoRimModelView
    {
        public object model;
        public int id;
        public Color tint = Color.white;
        public string swatch = "Wood";
        public float yaw;
        public float pitch = 35.3f;
        public float scale = 1f;

        // 模型局部坐标 -> 世界（缩放 + 绕 Y 轴旋转，不平移：相机对着包围盒中心）
        public Matrix4x4 matrix = Matrix4x4.identity;

        public Vector3 center;       // 世界空间包围盒中心
        public Vector3 faceCamera;   // 单位向量：模型中心 -> 相机（着色器 global _IsoView 用）
        public Vector3 right;        // 相机右轴
        public Vector3 up;           // 相机上轴
        public float halfWidth;      // 相机右轴方向上的半宽（世界单位）
        public float halfHeight;     // 相机上轴方向上的半高
        public float radius;         // 包围盒半径（算裁剪面用）

        public List<IsoRimPart> parts;
        public string key;
    }

    /// <summary>IsoRim 的一个网格部件（solid / 发光 / 描边 / 贴图）。</summary>
    internal sealed class IsoRimPart
    {
        public Mesh mesh;
        public Material material;
        public bool outline;
    }

    /// <summary>
    /// 可选依赖：IsoRim（Waylith 的 3D 模型 mod）。
    /// 全部通过反射访问，TextureLens 不引用 IsoRim.dll —— 没装 IsoRim、IsoRim 没给 1.6 版本、
    /// 或者 IsoRim 内部结构变了，这里都只会安静地不可用，查看窗照旧走原来的 2D 贴图。
    /// </summary>
    internal static class IsoRimBridge
    {
        private const BindingFlags StaticAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool initialized;
        private static bool available;
        private static string unavailableReason = "";

        // IsoRim 类型
        private static Type modelLibraryType;
        private static Type modelEntryType;
        private static Type modelDefType;
        private static Type modelType;
        private static Type partType;
        private static Type materialsType;
        private static Type glowType;
        private static Type viewType;
        private static Type texturesType;

        // 成员
        private static MethodInfo tryGetMethod;
        private static PropertyInfo hasModelProperty;
        private static FieldInfo entryDefField;
        private static MethodInfo pickMethod;
        private static MethodInfo yawMethod;
        private static FieldInfo modelFlatField;
        private static FieldInfo modelScaleField;
        private static FieldInfo modelIdField;
        private static FieldInfo modelPositionsField;
        private static MethodInfo partsForMethod;
        private static FieldInfo partMeshField;
        private static FieldInfo partMaterialField;
        private static FieldInfo partOutlineField;
        private static PropertyInfo outlineMaterialProperty;
        private static MethodInfo setSunHourMethod;
        private static MethodInfo setViewMethod;
        private static MethodInfo bindNeutralMethod;
        private static PropertyInfo viewPitchProperty;
        private static MethodInfo modelTintMethod;
        private static MethodInfo stuffSwatchMethod;
        private static MethodInfo swatchOfMethod;
        private static PropertyInfo apiVersionProperty;

        // 「这个动物品有没有 3D 模型」的缓存：查看窗每帧都要问，别每次都反射一遍
        private static readonly Dictionary<ThingDef, bool> hasModelCache = new Dictionary<ThingDef, bool>();

        private static float isoYaw = 45f;
        private static string apiVersion = "?";

        public static bool Available
        {
            get
            {
                EnsureInitialized();
                return available;
            }
        }

        public static string Status
        {
            get
            {
                EnsureInitialized();
                return available ? "ready (IsoRim API " + apiVersion + ")" : unavailableReason;
            }
        }

        /// <summary>解析一次反射目标；任何一步失败都永久标记为不可用（并记一次日志）。</summary>
        public static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;
            try
            {
                modelType = FindType("IsoRim.Model");
                modelEntryType = FindType("IsoRim.ModelEntry");
                modelDefType = FindType("IsoRim.ModelDef");
                modelLibraryType = FindType("IsoRim.ModelLibrary");
                partType = FindType("IsoRim.DynamicModels+Part");
                Type dynamicType = FindType("IsoRim.DynamicModels");
                materialsType = FindType("IsoRim.R3DMaterials");
                glowType = FindType("IsoRim.IsoGlow");
                viewType = FindType("IsoRim.View3D");
                texturesType = FindType("IsoRim.R3DTextures");
                Type apiType = FindType("IsoRim.Api");

                if (modelType == null || modelEntryType == null || modelDefType == null || modelLibraryType == null
                    || dynamicType == null || partType == null || materialsType == null || glowType == null
                    || viewType == null || texturesType == null)
                {
                    Unavailable("未找到 IsoRim 的类型（没装 IsoRim，或它不是 1.7 版本）");
                    return;
                }

                tryGetMethod = Method(modelLibraryType, "TryGet", StaticAny, typeof(ThingDef), modelEntryType.MakeByRefType());
                hasModelProperty = Property(modelEntryType, "HasModel");
                entryDefField = Field(modelEntryType, "def");
                pickMethod = Method(modelEntryType, "Pick", InstanceAny, typeof(Thing));
                yawMethod = Method(modelEntryType, "Yaw", InstanceAny, typeof(Rot4));
                modelFlatField = Field(modelDefType, "flat");
                modelScaleField = Field(modelDefType, "scale");
                modelIdField = Field(modelType, "id");
                modelPositionsField = Field(modelType, "positions");
                partsForMethod = Method(dynamicType, "PartsFor", StaticAny, modelType, typeof(Color), typeof(string));
                partMeshField = Field(partType, "mesh");
                partMaterialField = Field(partType, "material");
                partOutlineField = Field(partType, "outline");
                outlineMaterialProperty = Property(materialsType, "Outline");
                setSunHourMethod = Method(materialsType, "SetSunHour", StaticAny, typeof(float));
                setViewMethod = Method(materialsType, "SetView", StaticAny, typeof(Vector3), typeof(float));
                bindNeutralMethod = Method(glowType, "BindNeutral", StaticAny, Type.EmptyTypes);
                viewPitchProperty = Property(viewType, "Pitch");
                modelTintMethod = Method(texturesType, "ModelTint", StaticAny, typeof(Thing));
                stuffSwatchMethod = Method(texturesType, "StuffSwatch", StaticAny, typeof(Thing));
                swatchOfMethod = Method(texturesType, "SwatchOf", StaticAny, typeof(ThingDef));
                apiVersionProperty = apiType != null ? Property(apiType, "Version") : null;

                FieldInfo yawField = viewType.GetField("IsoYaw", StaticAny);
                if (yawField != null && yawField.IsLiteral)
                {
                    object raw = yawField.GetRawConstantValue();
                    if (raw is float)
                    {
                        isoYaw = (float)raw;
                    }
                }
                if (apiVersionProperty != null)
                {
                    object v = apiVersionProperty.GetValue(null, null);
                    apiVersion = v != null ? v.ToString() : "?";
                }

                if (tryGetMethod == null || hasModelProperty == null || entryDefField == null || pickMethod == null
                    || yawMethod == null || modelFlatField == null || modelScaleField == null || modelIdField == null
                    || modelPositionsField == null || partsForMethod == null || partMeshField == null
                    || partMaterialField == null || partOutlineField == null || outlineMaterialProperty == null
                    || setSunHourMethod == null || setViewMethod == null || bindNeutralMethod == null
                    || modelTintMethod == null || stuffSwatchMethod == null || swatchOfMethod == null)
                {
                    Unavailable("IsoRim 的版本和 TextureLens 预期的不一样（反射目标不全）");
                    return;
                }

                available = true;
                Log.Message("[TextureLens] 检测到 IsoRim（API " + apiVersion + "）：物品/建筑的查看窗可以显示 3D 模型。");
            }
            catch (Exception e)
            {
                Unavailable("初始化时出错：" + e.Message);
            }
        }

        private static void Unavailable(string reason)
        {
            available = false;
            unavailableReason = reason;
        }

        /// <summary>
        /// 给这个物体生成一个 3D 视图。没有模型（或模型是贴片式的 flat 模型）、或者 IsoRim 不可用都返回 false。
        /// 只有需要重渲染时才会调用（每帧调用太贵：要遍历顶点算包围盒）。
        /// </summary>
        public static bool TryGetModelView(Thing thing, ThingDef def, Rot4 rot, out IsoRimModelView view)
        {
            view = null;
            if (!Available)
            {
                return false;
            }
            if (def == null && thing != null)
            {
                def = thing.def;
            }
            if (def == null)
            {
                return false;
            }

            try
            {
                object[] args = { def, null };
                if (!(bool)tryGetMethod.Invoke(null, args) || args[1] == null)
                {
                    return false;
                }
                object entry = args[1];
                if (!(bool)hasModelProperty.GetValue(entry, null))
                {
                    return false;
                }
                object modelDef = entryDefField.GetValue(entry);
                if (modelDef == null)
                {
                    return false;
                }
                if (modelFlatField.GetValue(modelDef) is bool && (bool)modelFlatField.GetValue(modelDef))
                {
                    return false;   // flat 模型是「贴在平面上」的假 3D，游戏里也是按 2D 贴图那样用，直接走 2D
                }

                object model = pickMethod.Invoke(entry, new object[] { thing });
                if (model == null)
                {
                    return false;
                }
                Vector3[] positions = modelPositionsField.GetValue(model) as Vector3[];
                if (positions == null || positions.Length < 3)
                {
                    return false;
                }

                IsoRimModelView result = new IsoRimModelView();
                result.model = model;
                object idValue = modelIdField.GetValue(model);
                result.id = idValue is int ? (int)idValue : 0;
                result.yaw = (float)yawMethod.Invoke(entry, new object[] { rot });
                object scaleValue = modelScaleField.GetValue(modelDef);
                float scale = scaleValue is float ? (float)scaleValue : 1f;
                if (scale <= 0.0001f)
                {
                    scale = 1f;
                }
                result.scale = scale;
                result.pitch = CurrentPitch();

                // 模型矩阵：先缩放，再绕 Y 轴转向（和 IsoRim 的 DynamicModels.Draw 一致，只是不平移）
                result.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(result.yaw, Vector3.up), new Vector3(scale, scale, scale));

                Vector3 faceCamera;
                Vector3 right;
                Vector3 up;
                CameraBasis(result.pitch, out faceCamera, out right, out up);
                result.faceCamera = faceCamera;
                result.right = right;
                result.up = up;

                if (!MeasureBox(positions, result.matrix, faceCamera, right, up, result))
                {
                    return false;
                }

                ResolveTintAndSwatch(thing, def, result);
                result.parts = PartsOf(model, result.tint, result.swatch);
                if (result.parts == null || result.parts.Count == 0)
                {
                    return false;
                }

                result.key = result.id + "|" + result.yaw.ToString("F2") + "|" + result.pitch.ToString("F2")
                    + "|" + scale.ToString("F3") + "|" + result.swatch + "|" + result.tint.r.ToString("F3")
                    + "," + result.tint.g.ToString("F3") + "," + result.tint.b.ToString("F3");
                view = result;
                return true;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TextureLens] 读取 IsoRim 模型失败（回退 2D 贴图）：" + e.Message, 0x51AE81);
                return false;
            }
        }

        /// <summary>
        /// 轻量版检查：这个 Def 在 IsoRim 里有没有可用的 3D 模型（不做几何/材质计算，结果按 Def 缓存）。
        /// 查看窗用它决定要不要显示「3D / 贴图」切换按钮。
        /// </summary>
        public static bool HasModelFor(Thing thing, ThingDef def)
        {
            if (!Available)
            {
                return false;
            }
            if (def == null && thing != null)
            {
                def = thing.def;
            }
            if (def == null)
            {
                return false;
            }

            bool cached;
            if (hasModelCache.TryGetValue(def, out cached))
            {
                return cached;
            }

            bool result = false;
            try
            {
                object[] args = { def, null };
                if ((bool)tryGetMethod.Invoke(null, args) && args[1] != null)
                {
                    object entry = args[1];
                    if ((bool)hasModelProperty.GetValue(entry, null))
                    {
                        object modelDef = entryDefField.GetValue(entry);
                        object flat = modelDef == null ? null : modelFlatField.GetValue(modelDef);
                        result = modelDef != null && !(flat is bool && (bool)flat);
                    }
                }
            }
            catch (Exception)
            {
                result = false;
            }

            if (hasModelCache.Count > 4096)
            {
                hasModelCache.Clear();
            }
            hasModelCache[def] = result;
            return result;
        }

        /// <summary>渲染前把预览用的光照/视角设成中性：纯太阳光、真正的空间朝向。</summary>
        public static void ApplyPreviewLight(Vector3 faceCamera)
        {
            if (!Available)
            {
                return;
            }
            try
            {
                bindNeutralMethod.Invoke(null, null);
                setSunHourMethod.Invoke(null, new object[] { 10f });   // 和 IsoRim 自己画图标时的默认光线一致
                setViewMethod.Invoke(null, new object[] { faceCamera, 1f });   // 1.0 = 不压扁（真空间几何）
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TextureLens] 设置 IsoRim 预览光照失败：" + e.Message, 0x51AE82);
            }
        }

        public static Material OutlineMaterial
        {
            get
            {
                if (!Available || outlineMaterialProperty == null)
                {
                    return null;
                }
                try
                {
                    return outlineMaterialProperty.GetValue(null, null) as Material;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>IsoRim 当前的相机俯角（等距 35.3°，或它自己设置里的角度）。</summary>
        private static float CurrentPitch()
        {
            try
            {
                if (viewPitchProperty != null)
                {
                    object value = viewPitchProperty.GetValue(null, null);
                    if (value is float && (float)value > 1f)
                    {
                        return (float)value;
                    }
                }
            }
            catch (Exception)
            {
                // 没进入 3D 视角时 IsoRim 可能不初始化设置，用默认等距角度
            }
            return 35.3f;
        }

        /// <summary>相机朝向：俯角 pitch、方位角 IsoRim 的等距角（默认 45°）。</summary>
        private static void CameraBasis(float pitch, out Vector3 faceCamera, out Vector3 right, out Vector3 up)
        {
            Quaternion look = Quaternion.Euler(pitch, isoYaw, 0f);
            Vector3 forward = look * Vector3.forward;
            faceCamera = (-forward).normalized;
            right = (look * Vector3.right).normalized;
            up = (look * Vector3.up).normalized;
        }

        /// <summary>遍历模型的顶点，算出世界空间里紧贴模型的包围盒和相机轴上的半宽/半高。</summary>
        private static bool MeasureBox(Vector3[] positions, Matrix4x4 matrix, Vector3 faceCamera, Vector3 right, Vector3 up, IsoRimModelView result)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int count = positions.Length;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(positions[i]);
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.z < min.z) min.z = p.z;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
                if (p.z > max.z) max.z = p.z;
            }
            if (min.x > max.x)
            {
                return false;
            }

            Vector3 center = (min + max) * 0.5f;
            float halfW = 0f;
            float halfH = 0f;
            float radius = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 local = matrix.MultiplyPoint3x4(positions[i]) - center;
                float w = Mathf.Abs(Vector3.Dot(local, right));
                float h = Mathf.Abs(Vector3.Dot(local, up));
                if (w > halfW) halfW = w;
                if (h > halfH) halfH = h;
                float d = local.sqrMagnitude;
                if (d > radius) radius = d;
            }

            result.center = center;
            result.halfWidth = Mathf.Max(0.001f, halfW);
            result.halfHeight = Mathf.Max(0.001f, halfH);
            result.radius = Mathf.Max(0.001f, Mathf.Sqrt(radius));
            return true;
        }

        /// <summary>模型该用的颜色/材质花色：有实体就用实体的，只有 Def 时用默认材料。</summary>
        private static void ResolveTintAndSwatch(Thing thing, ThingDef def, IsoRimModelView result)
        {
            try
            {
                if (thing != null)
                {
                    object tint = modelTintMethod.Invoke(null, new object[] { thing });
                    if (tint is Color)
                    {
                        result.tint = (Color)tint;
                    }
                    object swatch = stuffSwatchMethod.Invoke(null, new object[] { thing });
                    if (swatch is string && !((string)swatch).NullOrEmpty())
                    {
                        result.swatch = (string)swatch;
                    }
                    return;
                }

                if (def != null && def.MadeFromStuff)
                {
                    ThingDef stuff = GenStuff.DefaultStuffFor(def);
                    if (stuff != null)
                    {
                        result.tint = def.GetColorForStuff(stuff);
                        object swatch = swatchOfMethod.Invoke(null, new object[] { stuff });
                        if (swatch is string && !((string)swatch).NullOrEmpty())
                        {
                            result.swatch = (string)swatch;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[TextureLens] 取 IsoRim 模型颜色失败（用白色）：" + e.Message, 0x51AE83);
            }
        }

        /// <summary>让 IsoRim 把模型烘成若干网格部件（它自己按 颜色+花色 缓存，这里只是取来画）。</summary>
        private static List<IsoRimPart> PartsOf(object model, Color tint, string swatch)
        {
            object raw = partsForMethod.Invoke(null, new object[] { model, tint, swatch });
            IList list = raw as IList;
            if (list == null)
            {
                return null;
            }
            List<IsoRimPart> parts = new List<IsoRimPart>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                object item = list[i];
                if (item == null)
                {
                    continue;
                }
                IsoRimPart part = new IsoRimPart();
                part.mesh = partMeshField.GetValue(item) as Mesh;
                part.material = partMaterialField.GetValue(item) as Material;
                object outline = partOutlineField.GetValue(item);
                part.outline = outline is bool && (bool)outline;
                if (part.mesh != null)
                {
                    parts.Add(part);
                }
            }
            return parts;
        }

        // ---------- 反射小工具 ----------

        private static Type FindType(string fullName)
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type found = null;
                    try
                    {
                        found = assemblies[i].GetType(fullName, false);
                    }
                    catch (Exception)
                    {
                        found = null;
                    }
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }
            return null;
        }

        private static MethodInfo Method(Type type, string name, BindingFlags flags, params Type[] args)
        {
            try
            {
                return type.GetMethod(name, flags, null, args, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static FieldInfo Field(Type type, string name)
        {
            try
            {
                return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PropertyInfo Property(Type type, string name)
        {
            try
            {
                return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
