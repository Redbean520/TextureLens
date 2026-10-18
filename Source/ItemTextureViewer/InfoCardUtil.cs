using System.Reflection;
using HarmonyLib;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// 1.6 的 Dialog_InfoCard 把 thing / def 等字段放成了 private（Traverse 类型在本版本已不可用），
    /// 这里用 Harmony 的 AccessTools 读私有字段；读不到就返回 null（例如只显示 Def 的世界物体）。
    /// </summary>
    internal static class InfoCardUtil
    {
        private static readonly FieldInfo ThingField = AccessTools.Field(typeof(Dialog_InfoCard), "thing");
        private static readonly FieldInfo DefField = AccessTools.Field(typeof(Dialog_InfoCard), "def");

        public static object GetTarget(Dialog_InfoCard card)
        {
            if (card == null) return null;
            try
            {
                Thing thing = ThingField != null ? ThingField.GetValue(card) as Thing : null;
                if (thing != null) return thing;
                Def def = DefField != null ? DefField.GetValue(card) as Def : null;
                if (def != null) return def;
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
