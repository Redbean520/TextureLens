using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 全屏贴图查看窗（2.6.3）。
    /// 贴图来源改成了"用游戏自己的相机离屏渲染 Graphic"，因此：
    ///   · 颜色 / 遮罩 / 朝向与地图上完全一致（不再有白模、贴图不符的问题）；
    ///   · shader 驱动的动画（_AgeSecs / _Working / 自定义动画 Graphic）会随帧刷新；
    ///   · 静态贴图只渲染一次，拖动 / 缩放大窗口时每帧只有一次 GUI.DrawTexture，不再卡。
    /// 画布支持滚轮缩放（以鼠标为锚点）、左键拖动平移、双击适应；可旋转朝向；角色可切换渲染尺寸。
    /// </summary>
    public class TextureViewerWindow : Window
    {
        private const float HeaderH = 40f;
        private const float FooterH = 46f;
        private const float Pad = 10f;
        private const float ButtonH = 26f;
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 20f;

        private readonly ViewerTarget target;
        private Rot4 rot = Rot4.South;
        private int selectedSize = 1024;
        private float zoom = 1f;
        private Vector2 pan = Vector2.zero;

        // 角色立绘（2.7.0 起由 PawnTextureRenderer 渲染到我们自己的 RT，需要本窗口负责销毁）
        private RenderTexture pawnTexture;
        private int pawnTextureSize;
        private float lastPawnRender = -999f;

        // 物品 / 建筑贴图（自建离屏渲染缓存）
        private RenderTexture thingTexture;
        private bool thingAnimated;

        // 离屏渲染失败时的兜底：游戏图标管线
        private IconInfo icon;
        private bool iconResolved;

        private bool draggingCanvas;
        private Vector2 lastMouse;

        public override Vector2 InitialSize => new Vector2(960f, 720f);
        protected override float Margin => 0f;

        public TextureViewerWindow(ViewerTarget target)
        {
            this.target = target;
            ItemTextureViewerSettings settings = ItemTextureViewerMod.Settings;
            if (settings != null)
            {
                if (settings.lastSelectedSize > 0)
                {
                    selectedSize = settings.lastSelectedSize;
                }
                if (settings.lastWindowZoom > 0f)
                {
                    zoom = settings.lastWindowZoom;
                }
            }

            // 打开新窗口时强制重渲染一次：避免拿到其它存档里同 ID 物体的旧图。
            ThingImageProvider.InvalidateAll();

            doWindowBackground = false;
            doCloseButton = false;
            doCloseX = false;
            closeOnCancel = true;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            draggable = false;
            resizeable = true;
            // 关键：不吞掉窗口外的点击，查看贴图时仍能操作游戏速度 / 暂停 / 其他 UI。
            absorbInputAroundWindow = false;
            drawShadow = true;
            soundAppear = null;
            soundClose = SoundDefOf.Click;
            layer = WindowLayer.Super;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            ItemTextureViewerSettings settings = ItemTextureViewerMod.Settings;
            if (settings == null)
            {
                return;
            }
            if (settings.lastWindowPosX >= 0f && settings.lastWindowPosY >= 0f)
            {
                windowRect.x = Mathf.Clamp(settings.lastWindowPosX, 0f, Mathf.Max(0f, UI.screenWidth - 160f));
                windowRect.y = Mathf.Clamp(settings.lastWindowPosY, 0f, Mathf.Max(0f, UI.screenHeight - 160f));
            }
        }

        private float RefreshInterval
        {
            get
            {
                ItemTextureViewerSettings settings = ItemTextureViewerMod.Settings;
                return settings != null ? Mathf.Max(0.02f, settings.previewRefreshInterval) : 0.033f;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (target == null)
            {
                return;
            }

            HandleKeys();

            Rect header = new Rect(0f, 0f, inRect.width, HeaderH);
            Rect footer = new Rect(0f, inRect.height - FooterH, inRect.width, FooterH);
            Rect body = new Rect(0f, HeaderH, inRect.width, inRect.height - HeaderH - FooterH);

            DrawHeader(header);
            DrawBody(body);
            DrawFooter(footer);

            Widgets.DrawBoxSolidWithOutline(inRect, Color.clear, UIStyles.Border, 2);
        }

        // ---------- 标题栏 ----------

        private void DrawHeader(Rect header)
        {
            Widgets.DrawBoxSolid(header, UIStyles.HeaderBg);

            string kind = target.IsPawn
                ? "ItemTextureViewer_KindPawn".Translate().ToString()
                : "ItemTextureViewer_KindThing".Translate().ToString();
            string title = kind + " · " + target.Label;
            if (!target.IsPawn && thingAnimated)
            {
                title += "  " + "ItemTextureViewer_AnimatedSuffix".Translate().ToString();
            }
            UIStyles.DrawText(new Rect(header.x + Pad, header.y, header.width - 60f, header.height), title, GameFont.Medium, TextAnchor.MiddleLeft, UIStyles.TextColor);

            Rect close = new Rect(header.xMax - 34f, header.y + 7f, 26f, 26f);
            if (UIStyles.DrawButton(close, "×", false, true))
            {
                Close();
            }

            // 交给 IMGUI 拖动窗口：鼠标坐标是窗口局部坐标，自己算 delta 会导致"窗口跟着鼠标走 → 局部坐标不变 → 拖动失效"。
            Rect dragArea = new Rect(header.x, header.y, header.width - 44f, header.height);
            GUI.DragWindow(dragArea);
        }

        // ---------- 画布 ----------

        private void DrawBody(Rect body)
        {
            Widgets.DrawBoxSolid(body, UIStyles.ViewerBg);
            Rect canvas = body.ContractedBy(Pad);
            TooltipHandler.TipRegion(canvas, new TipSignal("ItemTextureViewer_ViewerHint".Translate()));

            if (!ResolveContent(out Texture tex, out Vector2 proportions, out Material material, out Color tint, out float angle))
            {
                DrawFallback(canvas);
                return;
            }

            float fitScale = Mathf.Min(canvas.width / Mathf.Max(0.0001f, proportions.x), canvas.height / Mathf.Max(0.0001f, proportions.y));

            // 用 BeginGroup 而不是 BeginClip：组内绘制坐标和鼠标坐标都以组左上角为原点（RimWorld 全局惯例），
            // 放大超出画布的部分会被组自动裁掉，不会压到标题栏 / 底栏上。
            GUI.BeginGroup(canvas);
            try
            {
                Rect local = new Rect(0f, 0f, canvas.width, canvas.height);
                HandleCanvasInput(local, fitScale);

                float scale = fitScale * zoom;
                Vector2 center = local.center + pan;
                Rect draw = new Rect(center.x - proportions.x * scale * 0.5f, center.y - proportions.y * scale * 0.5f, proportions.x * scale, proportions.y * scale);

                Color oldColor = GUI.color;
                GUI.color = material != null ? Color.white : tint;
                if (material != null)
                {
                    Widgets.DrawTextureFitted(draw, tex, 1f, proportions, new Rect(0f, 0f, 1f, 1f), angle, material);
                }
                else
                {
                    GUI.DrawTexture(draw, tex, ScaleMode.StretchToFill, true);
                }
                GUI.color = oldColor;
            }
            finally
            {
                GUI.EndGroup();
            }

            UIStyles.DrawText(new Rect(canvas.x + 4f, canvas.yMax - 20f, 260f, 18f), "ItemTextureViewer_Dimensions".Translate(tex.width, tex.height), GameFont.Tiny, TextAnchor.MiddleLeft, UIStyles.TextDimColor);
        }

        /// <summary>
        /// 解析当前要画的图：角色走 PortraitsCache；物品 / 建筑走自建离屏渲染；两者失败才回退图标管线。
        /// 每帧调用，但内部有缓存，静态贴图不会重复渲染。
        /// </summary>
        private bool ResolveContent(out Texture tex, out Vector2 proportions, out Material material, out Color tint, out float angle)
        {
            tex = null;
            proportions = Vector2.one;
            material = null;
            tint = Color.white;
            angle = 0f;
            if (target == null)
            {
                return false;
            }

            if (target.IsPawn)
            {
                Pawn pawn = target.pawn;
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    return false;
                }
                EnsurePawnTexture(pawn);
                if (pawnTexture == null || !pawnTexture.IsCreated())
                {
                    return false;
                }
                tex = pawnTexture;
                proportions = new Vector2(pawnTexture.width, pawnTexture.height);
                return true;
            }

            Graphic graphic = CurrentGraphic();
            if (graphic != null)
            {
                RenderTexture rt = Diagnostics.Measure("Viewer render", delegate
                {
                    RenderTexture rendered = ThingImageProvider.Get(graphic, target.thing, target.thingDef, rot, out bool isAnimated);
                    thingAnimated = isAnimated;
                    return rendered;
                }, 8);
                if (rt != null && rt.IsCreated())
                {
                    thingTexture = rt;
                    proportions = new Vector2(rt.width, rt.height);
                    tex = rt;
                    return true;
                }
            }
            thingTexture = null;

            IconInfo fallback = FallbackIcon();
            if (!fallback.valid)
            {
                return false;
            }
            tex = fallback.texture;
            material = fallback.material;
            tint = fallback.color;
            angle = fallback.angle;
            proportions = fallback.proportions;
            return true;
        }

        /// <summary>
        /// 角色贴图：用 PawnTextureRenderer（原版 PawnCacheRenderer + portrait:false）渲染，
        /// 这样征召时手里的武器、以及动画节点都会被画出来；尺寸变了就重建 RT，销毁由本窗口负责。
        /// </summary>
        private void EnsurePawnTexture(Pawn pawn)
        {
            int size = Mathf.Clamp(selectedSize, 128, 2048);
            if (pawnTexture == null || !pawnTexture.IsCreated() || pawnTextureSize != size)
            {
                PawnTextureRenderer.Release(ref pawnTexture);
                pawnTexture = PawnTextureRenderer.Create(size);
                pawnTextureSize = size;
                lastPawnRender = -999f;
            }
            // 2.8.1：大窗口之前对 1024/2048 做了 0.25s/0.5s 的刷新节流，角色动画只有 2~4fps，看起来就是「卡卡的」。
            // 现在统一按设置里的刷新间隔（默认 0.033s ≈ 30fps）；2048 保留一点节流（20fps），
            // 兼顾流畅度与这一档分辨率下的绘制开销。嫌耗性能可在设置里把「刷新间隔」调大。
            float interval = RefreshInterval;
            if (selectedSize >= 2048)
            {
                interval = Mathf.Max(interval, 0.05f);
            }
            if (Time.realtimeSinceStartup - lastPawnRender >= interval)
            {
                PawnTextureRenderer.Render(pawn, pawnTexture, rot, 1f, Vector3.zero);
                lastPawnRender = Time.realtimeSinceStartup;
            }
        }

        private void ReleasePawnTexture()
        {
            PawnTextureRenderer.Release(ref pawnTexture);
            pawnTextureSize = 0;
            lastPawnRender = -999f;
        }

        private Graphic CurrentGraphic()
        {
            try
            {
                if (target.IsThing && target.thing != null)
                {
                    return target.thing.Graphic;
                }
                ThingDef def = target.thingDef;
                if (def != null && def.graphicData != null)
                {
                    return def.graphicData.Graphic;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[ItemTextureViewer] 取 Graphic 失败: " + e.Message, 0x51AE7D);
            }
            return null;
        }

        private IconInfo FallbackIcon()
        {
            if (iconResolved)
            {
                return icon;
            }
            iconResolved = true;
            icon = target.IsThing ? IconProvider.GetForThing(target.thing) : IconProvider.GetForDef(target.thingDef);
            return icon;
        }

        private void HandleCanvasInput(Rect canvas, float fitScale)
        {
            Event ev = Event.current;
            if (ev == null)
            {
                return;
            }
            Vector2 mouse = ev.mousePosition;

            if (ev.type == EventType.MouseDown && ev.clickCount >= 2 && canvas.Contains(mouse))
            {
                Fit();
                ev.Use();
                return;
            }

            switch (ev.type)
            {
                case EventType.MouseDown:
                    if (ev.button == 0 && canvas.Contains(mouse))
                    {
                        draggingCanvas = true;
                        lastMouse = mouse;
                        ev.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (ev.button == 0)
                    {
                        draggingCanvas = false;
                    }
                    break;
                case EventType.MouseDrag:
                    if (draggingCanvas && ev.button == 0)
                    {
                        pan += mouse - lastMouse;
                        lastMouse = mouse;
                        ev.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (canvas.Contains(mouse))
                    {
                        // 滚轮上滚 = 放大（Unity 上滚 delta.y > 0）。
                        ZoomAt(mouse, ev.delta.y > 0f ? 1.1f : 0.9f, canvas, fitScale);
                        ev.Use();
                    }
                    break;
            }
        }

        private void ZoomAt(Vector2 mouse, float factor, Rect canvas, float fitScale)
        {
            float oldScale = fitScale * zoom;
            if (oldScale < 0.0001f)
            {
                oldScale = 0.0001f;
            }
            float newZoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
            float newScale = fitScale * newZoom;
            Vector2 pivot = mouse - canvas.center - pan;
            pan = mouse - canvas.center - pivot * (newScale / oldScale);
            zoom = newZoom;
        }

        private void ZoomBy(float factor)
        {
            float oldZoom = zoom;
            float newZoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
            if (oldZoom > 0.0001f)
            {
                pan *= newZoom / oldZoom;
            }
            zoom = newZoom;
        }

        private void Fit()
        {
            zoom = 1f;
            pan = Vector2.zero;
        }

        private void DrawFallback(Rect canvas)
        {
            if (target.IsThing && target.thing != null)
            {
                float h = Mathf.Min(canvas.width, canvas.height) * 0.4f;
                Widgets.ThingIcon(new Rect(canvas.center.x - h * 0.5f, canvas.center.y - h * 0.5f, h, h), target.thing);
                return;
            }
            if (target.thingDef != null)
            {
                float h = Mathf.Min(canvas.width, canvas.height) * 0.4f;
                Widgets.ThingIcon(new Rect(canvas.center.x - h * 0.5f, canvas.center.y - h * 0.5f, h, h), target.thingDef);
                return;
            }
            UIStyles.DrawText(canvas, "ItemTextureViewer_NoTexture".Translate(), GameFont.Medium, TextAnchor.MiddleCenter, UIStyles.TextDimColor);
        }

        // ---------- 底栏 ----------

        private void DrawFooter(Rect footer)
        {
            Widgets.DrawBoxSolid(footer, UIStyles.FooterBg);
            float y = footer.y + (FooterH - ButtonH) * 0.5f;
            float x = footer.x + Pad;

            if (UIStyles.DrawButton(new Rect(x, y, 26f, ButtonH), "<", false, true, "ItemTextureViewer_RotateHint".Translate()))
            {
                Rotate(-1);
            }
            x += 30f;
            if (UIStyles.DrawButton(new Rect(x, y, 26f, ButtonH), ">", false, true, "ItemTextureViewer_RotateHint".Translate()))
            {
                Rotate(1);
            }
            x += 36f;

            if (target.IsPawn)
            {
                int current = Array.IndexOf(PortraitProvider.Sizes, selectedSize);
                if (current < 0)
                {
                    current = 3;
                }
                for (int i = 0; i < PortraitProvider.Sizes.Length; i++)
                {
                    int size = PortraitProvider.Sizes[i];
                    Rect button = new Rect(x, y, 46f, ButtonH);
                    if (UIStyles.DrawButton(button, size.ToString(), i == current, true, null, GameFont.Tiny))
                    {
                        ChangeSize(size);
                    }
                    x += 48f;
                }
            }

            UIStyles.DrawText(new Rect(x + 6f, y, 90f, ButtonH), Mathf.RoundToInt(zoom * 100f) + "%", GameFont.Tiny, TextAnchor.MiddleLeft, UIStyles.TextDimColor);

            float bw = 78f;
            Rect fitRect = new Rect(footer.xMax - Pad - bw, y, bw, ButtonH);
            Rect exportRect = new Rect(fitRect.x - bw - 6f, y, bw, ButtonH);
            if (UIStyles.DrawButton(fitRect, "ItemTextureViewer_ZoomFit".Translate(), false, true, null, GameFont.Tiny))
            {
                Fit();
            }
            DrawExportButton(exportRect);
        }

        private void DrawExportButton(Rect rect)
        {
            bool hover = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, hover ? UIStyles.PanelHover : UIStyles.PanelBg);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, UIStyles.Border);
            UIStyles.DrawText(rect, "ItemTextureViewer_ExportButton".Translate(), GameFont.Tiny, TextAnchor.MiddleCenter, UIStyles.TextColor);
            if (!hover)
            {
                return;
            }

            Event ev = Event.current;
            if (ev.type != EventType.MouseUp)
            {
                return;
            }

            if (ev.button == 0)
            {
                ExportCurrent();
                ev.Use();
            }
            else if (ev.button == 1)
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("ItemTextureViewer_OpenExportFolder".Translate(), ExportUtility.OpenExportFolder)
                };
                Find.WindowStack.Add(new FloatMenu(options));
                ev.Use();
            }
        }

        private void ExportCurrent()
        {
            if (target == null)
            {
                return;
            }

            string name = target.Label;
            if (name.NullOrEmpty())
            {
                name = "texture";
            }

            bool ok;
            RenderTexture ready = target.IsPawn
                ? (pawnTexture != null && pawnTexture.IsCreated() ? pawnTexture : null)
                : (thingTexture != null && thingTexture.IsCreated() ? thingTexture : null);
            if (ready != null)
            {
                ok = Diagnostics.Measure("Export PNG", () => ExportUtility.Export(ready, name, out string _), 1);
            }
            else
            {
                IconInfo info = FallbackIcon();
                if (!info.valid)
                {
                    Messages.Message("ItemTextureViewer_ExportFailed".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }
                Color exportTint = info.material != null ? Color.white : info.color;
                ok = Diagnostics.Measure("Export PNG", () => ExportUtility.Export(info.texture, exportTint, info.material, info.angle, name, out string _), 1);
            }

            Messages.Message(ok ? "ItemTextureViewer_ExportSuccess".Translate() : "ItemTextureViewer_ExportFailed".Translate(), ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput);
        }

        // ---------- 状态变更 ----------

        private void Rotate(int direction)
        {
            rot = rot.Rotated(direction < 0 ? RotationDirection.Counterclockwise : RotationDirection.Clockwise);
            InvalidateContent();
        }

        private void ChangeSize(int size)
        {
            selectedSize = size;
            ReleasePawnTexture();
        }

        private void InvalidateContent()
        {
            // 角色 RT 是我们自己建的：切换朝向时原地重渲染即可，不重建也不销毁。
            lastPawnRender = -999f;
            thingTexture = null;
            iconResolved = false;
        }

        private void HandleKeys()
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown)
            {
                return;
            }
            if (Find.WindowStack == null || !Find.WindowStack.GetsInput(this))
            {
                return;
            }

            switch (ev.keyCode)
            {
                case KeyCode.LeftArrow:
                    Rotate(-1);
                    break;
                case KeyCode.RightArrow:
                    Rotate(1);
                    break;
                case KeyCode.Equals:
                case KeyCode.KeypadPlus:
                    ZoomBy(1.25f);
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    ZoomBy(0.8f);
                    break;
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    Fit();
                    break;
                default:
                    return;
            }
            ev.Use();
        }

        public override void PreClose()
        {
            base.PreClose();
            ReleasePawnTexture();
            ItemTextureViewerSettings settings = ItemTextureViewerMod.Settings;
            if (settings == null)
            {
                return;
            }
            settings.lastWindowPosX = windowRect.x;
            settings.lastWindowPosY = windowRect.y;
            settings.lastWindowZoom = zoom;
            settings.lastSelectedSize = selectedSize;
            settings.currentZoom = zoom;
            settings.Write();
        }
    }
}
