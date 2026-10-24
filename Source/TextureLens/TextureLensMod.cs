using UnityEngine;
using Verse;

namespace TextureLens
{
    public class TextureLensMod : Mod
    {
        /// <summary>必须与 About.xml 的 modVersion、csproj 的 Version 一致。</summary>
        public const string Version = "2.9.0";

        public static TextureLensSettings Settings;

        /// <summary>设置页正在等待玩家按快捷键。</summary>
        public static bool WaitingForKey;

        /// <summary>
        /// 玩家点了预览浮窗右上角的 × 之后为 true：窗口缩成角落的小按钮，点按钮即可重新打开。
        /// 只在本次游戏内有效（不写进设置，重开游戏恢复自动显示）。
        /// </summary>
        public static bool PreviewHidden;

        /// <summary>
        /// 待打开查看器的目标（Thing / ThingDef / Pawn），由 QuickViewController 或预览窗写入，
        /// 在 Verse.Root.OnGUI 的后置补丁里消费 —— 保证窗口只在 GUI 事件阶段被创建。
        /// </summary>
        public static object PendingTarget;

        public TextureLensMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TextureLensSettings>();
            HarmonyPatches.Initialize();
            Log.Message("[TextureLens] loaded v" + Version);
        }

        public override void DoSettingsWindowContents(Rect inRect) => SettingsUI.Draw(inRect);

        public override string SettingsCategory() => "TextureLens_SettingsCategory".Translate();
    }
}
