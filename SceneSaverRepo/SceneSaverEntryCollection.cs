using System;
using System.Collections.Generic;
using Tomlet;

namespace SceneSaverRepo.Data;

public class SceneSaverEntryCollection
{
#if NET7_0_OR_GREATER
    public SceneSaverEntryCollection(IEnumerable<SceneSaverSaveEntry> data)
    {
        Saves = data;
        TimeSinceLastUpdate = DateTime.Now - RepoInfoAccumulator.lastUpdated;
    }
#endif

    public TimeSpan TimeSinceLastUpdate { get; set; }
    public IEnumerable<SceneSaverSaveEntry> Saves { get; set; }
}
