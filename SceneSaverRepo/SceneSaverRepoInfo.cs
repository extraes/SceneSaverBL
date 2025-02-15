namespace SceneSaverRepo.Data;

#if NET7_0_OR_GREATER
[System.Text.Json.Serialization.JsonSerializable(typeof(SceneSaverRepoInfo))]
#endif
public partial class SceneSaverRepoInfo
{
#if NET7_0_OR_GREATER
    static readonly Version _version = new(1, 1, 0);
    static readonly TimeZoneInfo _timeZone = TimeZoneInfo.Local;

    public SceneSaverRepoInfo()
    {
        name = RepoConfig.instance.repoName;
        version = _version;
        timeZone = _timeZone.ToString();
        totalDownloads = RepoInfoAccumulator.totalDownloads;
        totalDownloadsWeek = RepoInfoAccumulator.totalDownloadsWeek;
        totalSaves = RepoInfoAccumulator.totalSaves;
        timeSinceLastUpdate = DateTime.Now - RepoInfoAccumulator.lastUpdated;
    }

#endif

    public string name { get; set; }
    public Version version { get; set; }
    public string timeZone { get; set; }

    public int totalDownloads { get; set; }
    public int totalDownloadsWeek { get; set; }
    public int totalSaves { get; set; }
    public TimeSpan timeSinceLastUpdate { get; set; }
}

