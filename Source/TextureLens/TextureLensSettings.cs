using UnityEngine;
using Verse;

namespace TextureLens
{
    public enum PreviewAlignment { Left, Center, Right }

    /// <summary>
    /// 本模组的持久化设置。字段名与 Scribe 键名与 2.5.0 完全保持一致，
    /// 老玩家的 ModSettings 文件可以无损升级。
    /// </summary>
    public class TextureLensSettings : ModSettings
    {
        // 信息卡 T 按钮偏移
        public float btnOffsetX = 19f;
        public float btnOffsetY = 3f;

        // 角色实时预览
        public bool autoOpenPawnPreview = true;
        public bool autoFollowInspectPane = true;
        public float previewPosX = 100f;
        public float previewPosY = 100f;
        public float previewZoom = 0.4f;
        public PreviewAlignment previewAlignment = PreviewAlignment.Left;
        public float previewHorizontalOffset = 0f;
        public float previewVerticalGap = 25f;
        public float previewRefreshInterval = 0.033f;
        // 2.7.0：预览窗内容缩放（1 = 原尺寸）与透明背景开关
        public float previewContentZoom = 1f;
        public bool previewTransparent = true;
        public bool showPreviewDebugArea = false;
        public bool previewShowHumans = true;
        public bool previewShowAnimals = true;
        public bool previewShowOthers = true;

        // 诊断：开启后把耗时操作写进 Player.log（排查卡顿用，默认关闭）
        public bool verboseLog = false;

        // 全局快速查看快捷键
        public KeyCode quickViewKey = KeyCode.Y;
        public bool quickViewCtrl = false;
        public bool quickViewShift = false;
        public bool quickViewAlt = false;

        // 全屏窗口记忆
        public float currentZoom = 1f;
        public float lastWindowPosX = -1f;
        public float lastWindowPosY = -1f;
        public float lastWindowZoom = 1f;
        public int lastSelectedSize = 1024;

        /// <summary>预览窗缩放的安全区间，窗口与设置页共用。</summary>
        public float ClampedPreviewZoom
        {
            get
            {
                if (previewZoom < 0.2f) return 0.2f;
                if (previewZoom > 3f) return 3f;
                return previewZoom;
            }
        }

        /// <summary>预览窗内容缩放的安全区间（Ctrl+滚轮调整）。</summary>
        public float ClampedPreviewContentZoom
        {
            get
            {
                if (previewContentZoom < 0.5f) return 0.5f;
                if (previewContentZoom > 4f) return 4f;
                return previewContentZoom;
            }
        }

        /// <summary>按玩家选择的分类（人类 / 动物 / 其他）决定是否给这只动物显示预览。</summary>
        public bool ShowPreviewFor(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.RaceProps == null) return previewShowOthers;
            if (pawn.RaceProps.Humanlike) return previewShowHumans;
            if (pawn.RaceProps.Animal) return previewShowAnimals;
            return previewShowOthers;
        }

        public static string FormatKeyCombination(bool ctrl, bool shift, bool alt, KeyCode key)
        {
            string r = string.Empty;
            if (ctrl) r += "Ctrl+";
            if (shift) r += "Shift+";
            if (alt) r += "Alt+";
            if (key == KeyCode.None) r += "TextureLens_NotSet".Translate();
            else r += key.ToString();
            return r;
        }

        public void ResetToDefault()
        {
            btnOffsetX = 19f;
            btnOffsetY = 3f;
            autoOpenPawnPreview = true;
            autoFollowInspectPane = true;
            previewPosX = 100f;
            previewPosY = 100f;
            previewZoom = 0.4f;
            previewAlignment = PreviewAlignment.Left;
            previewHorizontalOffset = 0f;
            previewVerticalGap = 25f;
            previewRefreshInterval = 0.033f;
            previewContentZoom = 1f;
            previewTransparent = true;
            showPreviewDebugArea = false;
            previewShowHumans = true;
            previewShowAnimals = true;
            previewShowOthers = true;
            verboseLog = false;
            lastWindowPosX = -1f;
            lastWindowPosY = -1f;
            lastWindowZoom = 1f;
            lastSelectedSize = 1024;
            quickViewKey = KeyCode.Y;
            quickViewCtrl = false;
            quickViewShift = false;
            quickViewAlt = false;
            currentZoom = 1f;
            TextureLensMod.WaitingForKey = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref btnOffsetX, "btnOffsetX", 19f);
            Scribe_Values.Look(ref btnOffsetY, "btnOffsetY", 3f);
            Scribe_Values.Look(ref autoOpenPawnPreview, "autoOpenPawnPreview", true);
            Scribe_Values.Look(ref autoFollowInspectPane, "autoFollowInspectPane", true);
            Scribe_Values.Look(ref previewPosX, "previewPosX", 100f);
            Scribe_Values.Look(ref previewPosY, "previewPosY", 100f);
            Scribe_Values.Look(ref previewZoom, "previewZoom", 0.4f);
            Scribe_Values.Look(ref previewAlignment, "previewAlignment", PreviewAlignment.Left);
            Scribe_Values.Look(ref previewHorizontalOffset, "previewHorizontalOffset", 0f);
            Scribe_Values.Look(ref previewVerticalGap, "previewVerticalGap", 25f);
            Scribe_Values.Look(ref previewRefreshInterval, "previewRefreshInterval", 0.033f);
            Scribe_Values.Look(ref previewContentZoom, "previewContentZoom", 1f);
            Scribe_Values.Look(ref previewTransparent, "previewTransparent", true);
            Scribe_Values.Look(ref showPreviewDebugArea, "showPreviewDebugArea", false);
            Scribe_Values.Look(ref previewShowHumans, "previewShowHumans", true);
            Scribe_Values.Look(ref previewShowAnimals, "previewShowAnimals", true);
            Scribe_Values.Look(ref previewShowOthers, "previewShowOthers", true);
            Scribe_Values.Look(ref verboseLog, "verboseLog", false);
            Scribe_Values.Look(ref lastWindowPosX, "lastWindowPosX", -1f);
            Scribe_Values.Look(ref lastWindowPosY, "lastWindowPosY", -1f);
            Scribe_Values.Look(ref lastWindowZoom, "lastWindowZoom", 1f);
            Scribe_Values.Look(ref lastSelectedSize, "lastSelectedSize", 1024);
            Scribe_Values.Look(ref quickViewKey, "quickViewKey", KeyCode.Y);
            Scribe_Values.Look(ref quickViewCtrl, "quickViewCtrl", false);
            Scribe_Values.Look(ref quickViewShift, "quickViewShift", false);
            Scribe_Values.Look(ref quickViewAlt, "quickViewAlt", false);
            Scribe_Values.Look(ref currentZoom, "currentZoom", 1f);
        }
    }
}
