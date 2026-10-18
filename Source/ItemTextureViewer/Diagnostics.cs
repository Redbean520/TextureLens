using System;
using System.Diagnostics;
using Verse;

namespace ItemTextureViewer
{
    /// <summary>
    /// Opt-in timing diagnostics.
    /// Enable "Diagnostic logging" in the mod settings, reproduce a stutter/hitch,
    /// then look for lines starting with [ItemTextureViewer] in RimWorld's Player.log.
    /// Disabled by default: when disabled the only cost is a bool check.
    /// </summary>
    public static class Diagnostics
    {
        private static readonly Stopwatch sw = new Stopwatch();

        public static bool Enabled => ItemTextureViewerMod.Settings != null && ItemTextureViewerMod.Settings.verboseLog;

        /// <summary>Runs action and logs its duration when it takes at least thresholdMs.</summary>
        public static void Measure(string label, Action action, int thresholdMs = 20)
        {
            if (!Enabled)
            {
                action();
                return;
            }
            sw.Reset();
            sw.Start();
            try
            {
                action();
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds >= thresholdMs)
                    Verse.Log.Message("[ItemTextureViewer] " + label + ": " + sw.ElapsedMilliseconds + " ms");
            }
        }

        /// <summary>Runs func and logs its duration when it takes at least thresholdMs.</summary>
        public static T Measure<T>(string label, Func<T> func, int thresholdMs = 20)
        {
            if (!Enabled) return func();
            sw.Reset();
            sw.Start();
            try
            {
                return func();
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds >= thresholdMs)
                    Verse.Log.Message("[ItemTextureViewer] " + label + ": " + sw.ElapsedMilliseconds + " ms");
            }
        }

        public static void Info(string message)
        {
            if (Enabled) Verse.Log.Message("[ItemTextureViewer] " + message);
        }
    }
}
