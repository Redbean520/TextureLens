using RimWorld;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// 角色预览浮窗的调度组件（2.6.0 重写）。类名与 namespace 必须保留：旧存档 meta.modIds 里记录了它。
    /// 职责收窄为：判断该不该显示、创建/关闭窗口、跟随详情窗口计算位置。
    /// </summary>
    public class PawnPreviewManager : GameComponent
    {
        public static PawnPreviewManager Instance;

        private PawnPreviewWindow window;

        public PawnPreviewManager(Game game) : base()
        {
            Instance = this;
        }

        public override void GameComponentUpdate()
        {
            // 每帧都跑，不在诊断模式下不要为 Measure 生成委托（避免无谓的 GC 分配）。
            if (Diagnostics.Enabled) Diagnostics.Measure("PawnPreview tick", Tick, 40);
            else Tick();
        }

        private void Tick()
        {
            TextureLensSettings s = TextureLensMod.Settings;
            if (s == null)
            {
                CloseWindow();
                return;
            }
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                CloseWindow();
                return;
            }

            Pawn selected = Find.Selector.SingleSelectedThing as Pawn;
            bool show = selected != null && s.autoOpenPawnPreview && s.ShowPreviewFor(selected);
            if (show && s.autoFollowInspectPane && Find.WindowStack.WindowOfType<MainTabWindow_Inspect>() == null) show = false;

            if (!show)
            {
                CloseWindow();
                return;
            }

            if (window == null)
            {
                window = new PawnPreviewWindow();
                window.SetPawn(selected);
                Find.WindowStack.Add(window);
            }
            window.SetPawn(selected);
            // 点过 × 之后只显示角落的小按钮，点一下按钮即可重新打开（PreviewHidden 只在本次游戏内有效）。
            window.Minimized = TextureLensMod.PreviewHidden;
            PositionWindow(window, s);
        }

        private void PositionWindow(PawnPreviewWindow w, TextureLensSettings s)
        {
            // 跟随时位置由本组件每帧计算；非跟随模式用设置里的坐标（窗口本身不允许拖动，避免和每帧定位打架）。
            w.draggable = false;
            UnityEngine.Vector2 size = w.WindowSize;
            float ww = size.x;
            float hh = size.y;
            float screenW = UI.screenWidth;
            float screenH = UI.screenHeight;

            float x;
            switch (s.previewAlignment)
            {
                case PreviewAlignment.Center:
                    x = (screenW - ww) * 0.5f + s.previewHorizontalOffset;
                    break;
                case PreviewAlignment.Right:
                    x = screenW - ww - s.previewHorizontalOffset;
                    break;
                default:
                    x = s.previewHorizontalOffset;
                    break;
            }

            float y = s.previewVerticalGap;
            if (s.autoFollowInspectPane)
            {
                MainTabWindow_Inspect inspect = Find.WindowStack.WindowOfType<MainTabWindow_Inspect>();
                if (inspect != null) y = inspect.windowRect.y - hh - s.previewVerticalGap;
            }
            else
            {
                if (s.previewPosX >= 0f) x = s.previewPosX;
                if (s.previewPosY >= 0f) y = s.previewPosY;
            }

            x = UnityEngine.Mathf.Clamp(x, 0f, UnityEngine.Mathf.Max(0f, screenW - ww));
            y = UnityEngine.Mathf.Clamp(y, 0f, UnityEngine.Mathf.Max(0f, screenH - hh));
            w.windowRect = new UnityEngine.Rect(x, y, ww, hh);
        }

        private void CloseWindow()
        {
            if (window == null) return;
            if (window.IsOpen) window.Close();
            window = null;
        }
    }
}
