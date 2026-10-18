using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 统一配色与自绘控件。所有窗口/设置页共用，避免各处重复处理 GUI.color / Text 状态。
    /// </summary>
    public static class UIStyles
    {
        public static readonly Color WindowBg = new Color(0.10f, 0.10f, 0.12f, 0.99f);
        public static readonly Color HeaderBg = new Color(0.07f, 0.07f, 0.09f, 1f);
        public static readonly Color FooterBg = new Color(0.07f, 0.07f, 0.09f, 1f);
        public static readonly Color ViewerBg = new Color(0.05f, 0.05f, 0.06f, 1f);
        public static readonly Color PanelBg = new Color(0.17f, 0.17f, 0.19f, 0.95f);
        public static readonly Color PanelHover = new Color(0.25f, 0.25f, 0.28f, 0.98f);
        public static readonly Color PanelActive = new Color(0.24f, 0.42f, 0.66f, 0.98f);
        public static readonly Color Border = new Color(0.33f, 0.33f, 0.38f, 0.9f);
        public static readonly Color TextColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        public static readonly Color TextDimColor = new Color(0.60f, 0.60f, 0.64f, 1f);
        public static readonly Color Accent = new Color(0.42f, 0.65f, 0.95f, 1f);

        public static void DrawText(Rect rect, string text, GameFont font = GameFont.Small,
            TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color ?? TextColor;
            Widgets.Label(rect, text ?? string.Empty);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>自绘按钮：深色底 + 细边框，悬停/选中状态由本方法负责。</summary>
        public static bool DrawButton(Rect rect, string label, bool selected = false, bool active = true,
            string tooltip = null, GameFont font = GameFont.Small)
        {
            bool hover = active && Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, selected ? PanelActive : (hover ? PanelHover : PanelBg));
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, selected ? Accent : Border);
            DrawText(rect, label, font, TextAnchor.MiddleCenter, active ? (selected ? Color.white : TextColor) : TextDimColor);
            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, new TipSignal(tooltip));
            }
            return active && Widgets.ButtonInvisible(rect);
        }

        public static void DrawPanel(Rect rect, bool highlighted = false)
        {
            Widgets.DrawBoxSolid(rect, highlighted ? PanelHover : PanelBg);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, Border);
        }
    }
}
