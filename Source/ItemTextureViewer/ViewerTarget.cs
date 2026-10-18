using Verse;

namespace ItemTextureViewer
{
    /// <summary>全屏查看窗口的输入：可能是被选中的 Thing、信息卡里的 Def、或选中的 Pawn。</summary>
    public class ViewerTarget
    {
        public Thing thing;
        public ThingDef thingDef;
        public Pawn pawn;

        public bool IsPawn => pawn != null;
        public bool IsThing => thing != null;

        public static ViewerTarget From(object obj)
        {
            if (obj == null)
            {
                return null;
            }
            if (obj is Pawn pawn)
            {
                return new ViewerTarget { pawn = pawn, thing = pawn, thingDef = pawn.def };
            }
            if (obj is Thing thing)
            {
                return new ViewerTarget { thing = thing, thingDef = thing.def };
            }
            if (obj is ThingDef def)
            {
                return new ViewerTarget { thingDef = def };
            }
            return null;
        }

        public string Label
        {
            get
            {
                if (pawn != null && pawn.Name != null)
                {
                    return pawn.Name.ToStringShort;
                }
                if (thing != null)
                {
                    return thing.LabelCap.ToString();
                }
                if (thingDef != null)
                {
                    return thingDef.LabelCap.ToString();
                }
                return string.Empty;
            }
        }

        public string DefLabel
        {
            get
            {
                return thingDef != null ? thingDef.LabelCap.ToString() : string.Empty;
            }
        }
    }
}
