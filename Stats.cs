using Jevil;
using Jevil.ModStats;
using Jevil.Prefs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL;

internal static class Stats
{
    const string STATS_CATEGORY = "SceneSaverBL";

    public static async Task SaveCreated(bool full)
    {
        string prefix = full ? "full" : "quick";
        bool success = await StatsEntry.IncrementValueAsync(STATS_CATEGORY, prefix + "savesCreated");

#if DEBUG
        SceneSaverBL.Log($"Stats request for {prefix}save creation {(success ? "succeeded" : "failed")}");
#endif
    }

    public static async Task SaveLoaded(bool full)
    {
        string prefix = full ? "full" : "quick";
        bool success = await StatsEntry.IncrementValueAsync(STATS_CATEGORY, prefix + "savesLoaded");

#if DEBUG
        SceneSaverBL.Log($"Stats request for {prefix}save load {(success ? "succeeded" : "failed")}");
#endif
    }

    public static async Task Startup()
    {
        // removed CreateCategories because i only needed it once to create the categories and it had passkeys! hee hee hee!
        //#if DEBUG
        //        CreateCategories();
        //#endif
        string prefix = Utilities.IsPlatformQuest() ? "quest" : "pcvr";

        SceneSaverBL.Log($"Sending stats request for {prefix} platform launch!");
        bool success = await StatsEntry.IncrementValueAsync(STATS_CATEGORY, prefix + "Launches");

#if DEBUG
        SceneSaverBL.Log($"Stats request for {prefix} platform launch {(success ? "succeeded" : "failed")}");
#endif
    }

}
