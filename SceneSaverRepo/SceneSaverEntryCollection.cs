namespace SceneSaverRepo.Data;

public class SceneSaverEntryCollection
{
#if NET7_0_OR_GREATER
    public SceneSaverEntryCollection(IEnumerable<SceneSaverSaveEntry> data)
    {
        Saves = data.ToArray();
        TimeSinceLastUpdate = DateTime.Now - RepoInfoAccumulator.lastUpdated;
    }
#endif

    public TimeSpan TimeSinceLastUpdate { get; set; }
    public SceneSaverSaveEntry[] Saves { get; set; }
}
