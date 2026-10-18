using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 全局快速查看快捷键（2.6.0 重写）。类名与 namespace 必须保留：旧存档 meta.modIds 里记录了它。
    /// 只负责「检测按键 + 解析目标」，真正开窗口交给 HarmonyPatches 的 Root.OnGUI 后置补丁，
    /// 避免在 Update 里直接操作 WindowStack。
    /// </summary>
    public class QuickViewController : GameComponent
    {
        public QuickViewController(Game game) : base()
        {
        }

        public override void GameComponentUpdate()
        {
            if (Diagnostics.Enabled) Diagnostics.Measure("QuickView tick", Tick, 20);
            else Tick();
        }

        private void Tick()
        {
            ItemTextureViewerSettings s = ItemTextureViewerMod.Settings;
            if (s == null || ItemTextureViewerMod.WaitingForKey) return;
            if (s.quickViewKey == KeyCode.None) return;

            // 正在输入框里打字时不触发，避免误开窗口。
            if (GUIUtility.keyboardControl != 0) return;
            if (!Input.GetKeyDown(s.quickViewKey)) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (s.quickViewCtrl && !ctrl) return;
            if (s.quickViewShift && !shift) return;
            if (s.quickViewAlt && !alt) return;

            object target = ResolveTarget();
            if (target == null) return;
            ItemTextureViewerMod.PendingTarget = target;
        }

        private static object ResolveTarget()
        {
            Thing selected = Find.Selector.SingleSelectedThing;
            if (selected != null) return selected;

            if (Find.WindowStack == null) return null;
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Dialog_InfoCard card = windows[i] as Dialog_InfoCard;
                if (card == null) continue;
                object target = InfoCardUtil.GetTarget(card);
                if (target != null) return target;
            }
            return null;
        }
    }
}
