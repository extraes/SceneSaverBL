namespace SceneSaverBL
{
    internal static class ConfigVars
    {
        //[RangePref(64, 1024, 64)]
        internal static int previewSize = Utilities.IsPlatformQuest() ? 64 : 256;
        //[RangePref(1, 10, 1)]
        internal static int timeSliceMs = 3;
        //[Pref("Attempts to avoid lag spikes by splitting the full-save (that don't occur in quicksaves) processes over time, as opposed to fullsaving/loading each object in its entirety all at once")]
        internal static bool fullsaveOverTime = true;
    }
}