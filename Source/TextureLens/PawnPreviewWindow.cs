using RimWorld;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// 角色实时预览浮窗（2.7.0 重做）：
    /// 1. 透明背景：只画角色本体的 RenderTexture（原版相机就是透明底），游戏画面直接透出来；
    /// 2. 右上角小 ×：点掉后窗口缩成角落一个小按钮「显示预览」，点一下就能重新打开；
    /// 3. Ctrl + 滚轮：缩放画面里的内容（重新渲染，不糊）；Ctrl + 左键拖动：平移内容；双击内容：复位；
    /// 4. 普通滚轮仍然是调整整个窗口大小。
    /// 位置由 PawnPreviewManager 每帧决定（跟随信息卡或使用设置里的坐标）。
    /// </summary>
    public class PawnPreviewWindow : Window
    {
        private const int TexSize = 512;
        private const float TabW = 138f;
        private const float TabH = 30f;
        private const float BarH = 22f;
        private const float HintH = 16f;

        private Pawn pawn;
        private RenderTexture tex;
        private int texSize;
        private float lastRender = -999f;
        private Vector2 camPan;
        private bool draggingContent;
        private Vector2 lastDragMouse;

        // 2.12.0：装了 IsoRim 时，浮窗里直接用它的 3D 模型（和查看窗共用同一套离屏渲染管线）
        private RenderTexture modelTex;
        private int modelTexSize;
        private float lastModelRender = -999f;
        private bool using3D;
        private bool modelFailed;

        /// <summary>被玩家点 × 收起时为 true，窗口只画一个小按钮。</summary>
        public bool Minimized;

        public Pawn Pawn => pawn;

        public Vector2 WindowSize
        {
            get
            {
                if (Minimized)
                {
                    return new Vector2(TabW, TabH);
                }
                TextureLensSettings s = TextureLensMod.Settings;
                float zoom = s != null ? s.ClampedPreviewZoom : 0.4f;
                float size = Mathf.Max(96f, TexSize * zoom);
                return new Vector2(size, size);
            }
        }

        public PawnPreviewWindow()
        {
            doWindowBackground = false;
            doCloseButton = false;
            doCloseX = false;
            closeOnCancel = false;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            focusWhenOpened = false;
            absorbInputAroundWindow = false;
            draggable = false;
            drawShadow = false;
            preventCameraMotion = false;
            resizeable = false;
            soundAppear = null;
            soundClose = null;
            layer = WindowLayer.GameUI;
        }

        public override Vector2 InitialSize => WindowSize;
        protected override float Margin => 0f;

        public void SetPawn(Pawn p)
        {
            if (pawn == p)
            {
                return;
            }
            pawn = p;
            camPan = Vector2.zero;
            lastRender = -999f;
            lastModelRender = -999f;
            modelFailed = false;
        }

        public void Invalidate()
        {
            lastRender = -999f;
            lastModelRender = -999f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (Minimized)
            {
                DrawTab(inRect);
                return;
            }
            DrawContent(inRect);
        }

        public override void PreClose()
        {
            base.PreClose();
            PawnTextureRenderer.Release(ref tex);
            texSize = 0;
            lastRender = -999f;
            PawnTextureRenderer.Release(ref modelTex);
            modelTexSize = 0;
            lastModelRender = -999f;
        }

        // ---------- 收起后的小按钮 ----------

        private void DrawTab(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, new Color(0.07f, 0.07f, 0.09f, 0.72f));
            Widgets.DrawBoxSolidWithOutline(inRect, Color.clear, UIStyles.Border);
            UIStyles.DrawText(inRect, "TextureLens_PreviewShowTab".Translate(), GameFont.Tiny, TextAnchor.MiddleCenter, UIStyles.TextColor);
            TooltipHandler.TipRegion(inRect, new TipSignal("TextureLens_PreviewShowTabTip".Translate()));
            if (Widgets.ButtonInvisible(inRect))
            {
                TextureLensMod.PreviewHidden = false;
                lastRender = -999f;
            }
        }

        // ---------- 正常窗口 ----------

        private void DrawContent(Rect inRect)
        {
            TextureLensSettings s = TextureLensMod.Settings;
            bool transparent = s == null || s.previewTransparent;
            if (!transparent)
            {
                Widgets.DrawBoxSolid(inRect, UIStyles.WindowBg);
            }

            RenderTexture rt = EnsureTexture();
            if (rt != null)
            {
                Color old = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(inRect, rt, ScaleMode.StretchToFill, true);
                GUI.color = old;
            }
            else
            {
                UIStyles.DrawText(inRect, "TextureLens_NoTexture".Translate(), GameFont.Small, TextAnchor.MiddleCenter, UIStyles.TextDimColor);
            }

            if (s != null && s.showPreviewDebugArea)
            {
                Widgets.DrawBoxSolidWithOutline(inRect, Color.clear, Color.red, 2);
            }

            bool hover = Mouse.IsOver(inRect);
            if (hover)
            {
                DrawOverlay(inRect);
                Widgets.DrawBoxSolidWithOutline(inRect, Color.clear, UIStyles.Border);
            }

            HandleInput(inRect);
        }

        /// <summary>悬停时出现的顶部操作条 + 底部操作提示（透明背景下用半透明底条保证可读）。</summary>
        private void DrawOverlay(Rect inRect)
        {
            Rect bar = new Rect(inRect.x, inRect.y, inRect.width, BarH);
            Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.45f));

            string label = pawn != null && pawn.Name != null ? pawn.Name.ToStringShort : string.Empty;
            UIStyles.DrawText(new Rect(bar.x + 4f, bar.y, Mathf.Max(0f, bar.width - 126f), bar.height), label, GameFont.Tiny, TextAnchor.MiddleLeft, UIStyles.TextColor);

            Rect closeRect = new Rect(bar.xMax - 20f, bar.y + 2f, 18f, BarH - 4f);
            Rect viewRect = new Rect(closeRect.x - 40f, bar.y + 2f, 38f, BarH - 4f);
            // 2.12.0：装了 IsoRim（且这个角色能画成 3D figure）时，多一个 3D / 立绘切换按钮
            if (IsoRimBridge.CanPreviewPawn(pawn))
            {
                TextureLensSettings st = TextureLensMod.Settings;
                bool on = st != null && st.previewShow3D;
                Rect modelRect = new Rect(viewRect.x - 30f, bar.y + 2f, 28f, BarH - 4f);
                if (UIStyles.DrawButton(modelRect, "3D", on, true, "TextureLens_Show3DTooltip".Translate(), GameFont.Tiny))
                {
                    if (st != null)
                    {
                        st.previewShow3D = !on;
                        st.Write();
                    }
                    lastRender = -999f;
                    lastModelRender = -999f;
                    modelFailed = false;
                }
            }
            if (UIStyles.DrawButton(viewRect, "TextureLens_PreviewView".Translate(), false, true, "TextureLens_PreviewViewTip".Translate(), GameFont.Tiny))
            {
                if (pawn != null)
                {
                    Find.WindowStack.Add(new TextureViewerWindow(ViewerTarget.From(pawn)));
                }
            }
            if (UIStyles.DrawButton(closeRect, "×", false, true, "TextureLens_PreviewCloseTip".Translate(), GameFont.Tiny))
            {
                TextureLensMod.PreviewHidden = true;
            }

            // 窗口太小时不画提示条，避免文字被裁得很难看。
            if (inRect.width >= 240f)
            {
                Rect hint = new Rect(inRect.x, inRect.yMax - HintH, inRect.width, HintH);
                Widgets.DrawBoxSolid(hint, new Color(0f, 0f, 0f, 0.35f));
                UIStyles.DrawText(hint, "TextureLens_PreviewControls".Translate(), GameFont.Tiny, TextAnchor.MiddleCenter, UIStyles.TextDimColor);
            }
        }

        private RenderTexture EnsureTexture()
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return null;
            }

            bool want3D = ShouldUse3D();
            if (want3D != using3D)
            {
                // 2D 立绘 / 3D 模型切换：两条管线各有一份缓存，切换时都要重画一次。
                using3D = want3D;
                lastRender = -999f;
                lastModelRender = -999f;
            }
            if (using3D)
            {
                RenderTexture model = EnsureModelTexture();
                if (model != null)
                {
                    return model;
                }
                // 这一帧 3D 没画出来（模型还没准备好 / 离屏相机失败）：先回退 2D，下一帧再试。
                using3D = false;
            }

            if (tex == null || !tex.IsCreated() || texSize != TexSize)
            {
                PawnTextureRenderer.Release(ref tex);
                tex = PawnTextureRenderer.Create(TexSize);
                texSize = TexSize;
                lastRender = -999f;
            }

            TextureLensSettings s = TextureLensMod.Settings;
            float contentZoom = s != null ? s.ClampedPreviewContentZoom : 1f;
            if (Time.realtimeSinceStartup - lastRender >= RefreshInterval)
            {
                PawnTextureRenderer.Render(pawn, tex, Rot4.South, contentZoom, new Vector3(camPan.x, 0f, camPan.y));
                lastRender = Time.realtimeSinceStartup;
            }
            return tex != null && tex.IsCreated() ? tex : null;
        }

        /// <summary>
        /// 这个角色现在该画 IsoRim 的 3D 模型还是原版 2D 立绘。
        /// 没装 IsoRim、设置里关掉了、或者之前渲染失败过，都会回退 2D。
        /// </summary>
        private bool ShouldUse3D()
        {
            TextureLensSettings s = TextureLensMod.Settings;
            return s != null && s.previewShow3D && !modelFailed && IsoRimBridge.CanPreviewPawn(pawn);
        }

        /// <summary>
        /// 把角色按 IsoRim 的 3D figure 画进浮窗的 RT。缩放 / 平移语义与 2D 立绘一致：
        /// contentZoom 直接交给渲染器，camPan 先换算成「画面高度的几分之几」。
        /// </summary>
        private RenderTexture EnsureModelTexture()
        {
            TextureLensSettings s = TextureLensMod.Settings;
            if (s == null)
            {
                return null;
            }
            if (modelTex == null || !modelTex.IsCreated() || modelTexSize != TexSize)
            {
                PawnTextureRenderer.Release(ref modelTex);
                modelTex = PawnTextureRenderer.Create(TexSize);
                modelTexSize = TexSize;
                lastModelRender = -999f;
            }
            // 3D 模型每次刷新都要把角色重新截一遍部件（衣服、头发、手持武器、动作都在里面），
            // 比 2D 立绘贵，刷新间隔再小也没意义，和查看窗一样至少 0.05 秒。
            float interval = Mathf.Max(0.05f, RefreshInterval);
            if (Time.realtimeSinceStartup - lastModelRender >= interval)
            {
                IsoRimModelView view;
                if (!IsoRimBridge.TryGetPawnView(pawn, Rot4.South, out view))
                {
                    modelFailed = true;
                    return null;
                }
                float ortho2D = 1f / Mathf.Max(0.05f, PortraitProvider.CameraZoomFor(TexSize) * s.ClampedPreviewContentZoom);
                Vector2 pan = new Vector2(camPan.x / (2f * ortho2D), camPan.y / (2f * ortho2D));
                if (!IsoRimModelRenderer.Render(view, modelTex, s.ClampedPreviewContentZoom, pan))
                {
                    modelFailed = true;
                    return null;
                }
                lastModelRender = Time.realtimeSinceStartup;
            }
            return modelTex != null && modelTex.IsCreated() ? modelTex : null;
        }

        private float RefreshInterval
        {
            get
            {
                TextureLensSettings s = TextureLensMod.Settings;
                return s != null ? Mathf.Max(0.02f, s.previewRefreshInterval) : 0.033f;
            }
        }

        private void HandleInput(Rect inRect)
        {
            Event ev = Event.current;
            if (ev == null)
            {
                return;
            }
            TextureLensSettings s = TextureLensMod.Settings;
            if (s == null)
            {
                return;
            }

            switch (ev.type)
            {
                case EventType.ScrollWheel:
                    if (!Mouse.IsOver(inRect))
                    {
                        return;
                    }
                    if (ev.control)
                    {
                        // Ctrl + 滚轮：缩放画面里的内容（改渲染镜头，不是把贴图拉大）
                        float factor = ev.delta.y > 0f ? 1.1f : 0.9f;
                        s.previewContentZoom = Mathf.Clamp(s.ClampedPreviewContentZoom * factor, 0.5f, 4f);
                        lastRender = -999f;
                    }
                    else
                    {
                        // 普通滚轮：改窗口大小
                        float factor = ev.delta.y > 0f ? 1.1f : 0.9f;
                        s.previewZoom = Mathf.Clamp(s.previewZoom * factor, 0.2f, 3f);
                    }
                    ev.Use();
                    break;

                case EventType.MouseDown:
                    if (ev.button != 0 || !inRect.Contains(ev.mousePosition))
                    {
                        return;
                    }
                    if (ev.clickCount >= 2)
                    {
                        // 双击：内容复位（缩放回 1x、平移归零）
                        s.previewContentZoom = 1f;
                        camPan = Vector2.zero;
                        lastRender = -999f;
                        ev.Use();
                    }
                    else if (ev.control)
                    {
                        draggingContent = true;
                        lastDragMouse = ev.mousePosition;
                        ev.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (draggingContent && ev.button == 0)
                    {
                        PanBy(ev.mousePosition - lastDragMouse);
                        lastDragMouse = ev.mousePosition;
                        ev.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (ev.button == 0)
                    {
                        draggingContent = false;
                    }
                    break;
            }
        }

        /// <summary>按屏幕像素位移平移画面内容：换算成世界坐标后作用在相机上。</summary>
        private void PanBy(Vector2 deltaPixels)
        {
            TextureLensSettings s = TextureLensMod.Settings;
            float contentZoom = s != null ? s.ClampedPreviewContentZoom : 1f;
            float zoom = Mathf.Max(0.05f, PortraitProvider.CameraZoomFor(TexSize) * contentZoom);
            float orthoSize = 1f / zoom;
            float worldPerPixel = (orthoSize * 2f) / TexSize;
            camPan.x -= deltaPixels.x * worldPerPixel;
            camPan.y += deltaPixels.y * worldPerPixel;
            float limit = Mathf.Max(0.05f, orthoSize * 0.9f);
            camPan.x = Mathf.Clamp(camPan.x, -limit, limit);
            camPan.y = Mathf.Clamp(camPan.y, -limit, limit);
            lastRender = -999f;
        }
    }
}