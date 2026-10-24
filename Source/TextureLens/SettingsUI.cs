using RimWorld;
using UnityEngine;
using Verse;

namespace TextureLens
{
    /// <summary>
    /// Mod 设置页。所有文字走 Keyed（键名以 TextureLens_ 开头），
    /// 布局：标题 / 滚动区（分区 + 行）/ 底部版本信息。
    /// </summary>
    public static class SettingsUI
    {
        private static Vector2 scrollPos;
        private static float cachedHeight = 900f;

        public static void Draw(Rect inRect)
        {
            TextureLensSettings s = TextureLensMod.Settings;
            if (s == null) return;

            HandleKeyCapture(s);

            Rect view = inRect.ContractedBy(4f);

            Rect header = new Rect(view.x, view.y, view.width, 50f);
            UIStyles.DrawText(new Rect(header.x, header.y + 2f, header.width, 26f), "Texture Lens", GameFont.Medium, TextAnchor.MiddleLeft, UIStyles.TextColor);
            UIStyles.DrawText(new Rect(header.x, header.y + 28f, header.width, 18f), "TextureLens_VersionInfo".Translate(TextureLensMod.Version), GameFont.Tiny, TextAnchor.MiddleLeft, UIStyles.TextDimColor);
            Widgets.DrawLineHorizontal(header.x, header.yMax - 2f, header.width, UIStyles.Border);

            Rect outer = new Rect(view.x, header.yMax + 4f, view.width, view.height - header.height - 4f);
            Rect viewRect = new Rect(0f, 0f, outer.width - 18f, Mathf.Max(outer.height, cachedHeight + 24f));
            Widgets.BeginScrollView(outer, ref scrollPos, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 0f, viewRect.width, 100000f));

            Section(listing, "TextureLens_SectionDisplay");
            SliderRow(listing, "TextureLens_ButtonHorizOffset", ref s.btnOffsetX, -60f, 60f);
            SliderRow(listing, "TextureLens_ButtonVertOffset", ref s.btnOffsetY, -60f, 60f);

            listing.Gap(8f);
            UIStyles.DrawText(listing.GetRect(22f), "TextureLens_PreviewLayout".Translate(), GameFont.Small, TextAnchor.MiddleLeft, UIStyles.Accent);

            ToggleRow(listing, "TextureLens_AutoPawnPreview", ref s.autoOpenPawnPreview);
            ToggleRow(listing, "TextureLens_FollowInspectPane", ref s.autoFollowInspectPane);
            if (s.autoFollowInspectPane)
            {
                AlignmentRow(listing, s);
                SliderRow(listing, "TextureLens_PreviewHorizontalOffset", ref s.previewHorizontalOffset, -500f, 500f);
                SliderRow(listing, "TextureLens_PreviewVerticalGap", ref s.previewVerticalGap, -200f, 200f);
            }
            else
            {
                SliderRow(listing, "TextureLens_PreviewPosX", ref s.previewPosX, 0f, 2000f);
                SliderRow(listing, "TextureLens_PreviewPosY", ref s.previewPosY, 0f, 2000f);
            }
            SliderRow(listing, "TextureLens_PreviewZoom", ref s.previewZoom, 0.2f, 3f);
            SliderRow(listing, "TextureLens_PreviewContentZoom", ref s.previewContentZoom, 0.5f, 4f);
            ToggleRow(listing, "TextureLens_PreviewTransparent", ref s.previewTransparent);
            ToggleRow(listing, "TextureLens_PreviewShow3D", ref s.previewShow3D);
            SliderRow(listing, "TextureLens_PreviewRefresh", ref s.previewRefreshInterval, 0.02f, 1f);

            Section(listing, "TextureLens_SectionFeatures");
            ToggleRow(listing, "TextureLens_ShowPreviewDebug", ref s.showPreviewDebugArea);
            ToggleRow(listing, "TextureLens_PreviewShowHumans", ref s.previewShowHumans);
            ToggleRow(listing, "TextureLens_PreviewShowAnimals", ref s.previewShowAnimals);
            ToggleRow(listing, "TextureLens_PreviewShowOthers", ref s.previewShowOthers);

            listing.Gap(10f);
            UIStyles.DrawText(listing.GetRect(22f), "TextureLens_QuickViewLabel".Translate(), GameFont.Small, TextAnchor.MiddleLeft, UIStyles.Accent);
            KeyCaptureRow(listing, s);

            Section(listing, "TextureLens_SectionDiagnostics");
            ToggleRow(listing, "TextureLens_VerboseLog", ref s.verboseLog);
            if (ButtonRow(listing, "TextureLens_ClearTextureCache"))
            {
                IconProvider.ClearCaches();
                ThingImageProvider.ClearCaches();
                Messages.Message("TextureLens_TextureCacheCleared".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            if (ButtonRow(listing, "TextureLens_OpenExportFolder")) ExportUtility.OpenExportFolder();

            listing.Gap(10f);
            if (ButtonRow(listing, "TextureLens_Reset"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("TextureLens_ConfirmReset".Translate(), delegate
                {
                    s.ResetToDefault();
                    s.Write();
                }));
            }

            listing.Gap(14f);
            Widgets.DrawLineHorizontal(0f, listing.CurHeight, listing.ColumnWidth, UIStyles.Border);
            listing.Gap(6f);
            listing.Label("TextureLens_VersionInfo".Translate(TextureLensMod.Version));
            listing.Label("TextureLens_HarmonyOk".Translate());

            listing.End();
            cachedHeight = listing.CurHeight;
            Widgets.EndScrollView();
        }

        // ---------- 行控件 ----------

        private static void Section(Listing_Standard listing, string titleKey)
        {
            listing.Gap(10f);
            Rect row = listing.GetRect(26f);
            Widgets.DrawBoxSolid(row, UIStyles.PanelBg);
            Widgets.DrawBoxSolid(new Rect(row.x, row.y, 4f, row.height), UIStyles.Accent);
            UIStyles.DrawText(new Rect(row.x + 12f, row.y, row.width - 12f, row.height), titleKey.Translate(), GameFont.Small, TextAnchor.MiddleLeft, UIStyles.Accent);
            listing.Gap(6f);
        }

        private static void SliderRow(Listing_Standard listing, string labelKey, ref float value, float min, float max)
        {
            listing.Label(labelKey.Translate(value));
            Rect slider = listing.GetRect(18f);
            value = Widgets.HorizontalSlider(slider, value, min, max);
            listing.Gap(10f);
        }

        private static void ToggleRow(Listing_Standard listing, string labelKey, ref bool value)
        {
            Rect row = listing.GetRect(26f);
            Rect box = new Rect(row.x + 2f, row.y + 4f, 18f, 18f);
            Widgets.DrawBoxSolidWithOutline(box, value ? UIStyles.Accent : UIStyles.PanelBg, UIStyles.Border);
            UIStyles.DrawText(new Rect(box.xMax + 8f, row.y, row.width - 28f, row.height), labelKey.Translate(), GameFont.Small, TextAnchor.MiddleLeft, UIStyles.TextColor);
            if (Widgets.ButtonInvisible(row)) value = !value;
            listing.Gap(4f);
        }

        private static bool ButtonRow(Listing_Standard listing, string labelKey)
        {
            Rect row = listing.GetRect(30f);
            bool clicked = UIStyles.DrawButton(row, labelKey.Translate());
            listing.Gap(6f);
            return clicked;
        }

        private static void AlignmentRow(Listing_Standard listing, TextureLensSettings s)
        {
            Rect row = listing.GetRect(26f);
            UIStyles.DrawText(new Rect(row.x, row.y, row.width * 0.4f, row.height), "TextureLens_PreviewAlignment".Translate(), GameFont.Small, TextAnchor.MiddleLeft, UIStyles.TextColor);
            float bw = (row.width * 0.6f - 8f) / 3f;
            PreviewAlignment[] values = { PreviewAlignment.Left, PreviewAlignment.Center, PreviewAlignment.Right };
            string[] keys = { "TextureLens_PreviewAlignmentLeft", "TextureLens_PreviewAlignmentCenter", "TextureLens_PreviewAlignmentRight" };
            for (int i = 0; i < 3; i++)
            {
                Rect b = new Rect(row.x + row.width * 0.4f + i * (bw + 4f), row.y + 2f, bw, 22f);
                if (UIStyles.DrawButton(b, keys[i].Translate(), s.previewAlignment == values[i])) s.previewAlignment = values[i];
            }
            listing.Gap(8f);
        }

        private static void KeyCaptureRow(Listing_Standard listing, TextureLensSettings s)
        {
            Rect row = listing.GetRect(30f);
            bool waiting = TextureLensMod.WaitingForKey;
            string label = TextureLensSettings.FormatKeyCombination(s.quickViewCtrl, s.quickViewShift, s.quickViewAlt, s.quickViewKey);
            if (UIStyles.DrawButton(row, label, waiting)) TextureLensMod.WaitingForKey = !waiting;
            listing.Gap(4f);
            if (TextureLensMod.WaitingForKey) listing.Label("TextureLens_PressKeys".Translate());
        }

        // ---------- 快捷键捕获 ----------

        private static void HandleKeyCapture(TextureLensSettings s)
        {
            if (!TextureLensMod.WaitingForKey) return;
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown) return;

            KeyCode key = ev.keyCode;
            if (key == KeyCode.None) return;
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl || key == KeyCode.LeftShift || key == KeyCode.RightShift || key == KeyCode.LeftAlt || key == KeyCode.RightAlt) return;

            if (key == KeyCode.Escape)
            {
                TextureLensMod.WaitingForKey = false;
                ev.Use();
                return;
            }

            s.quickViewKey = key;
            s.quickViewCtrl = ev.control;
            s.quickViewShift = ev.shift;
            s.quickViewAlt = ev.alt;
            TextureLensMod.WaitingForKey = false;
            s.Write();
            ev.Use();
        }
    }
}
