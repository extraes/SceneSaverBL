using SceneSaverRepo.Data;

namespace SceneSaverRepo;

public static class RepoInfoAccumulator
{
    static async Task PeriodicallyUpdateStats()
    {
        TimeSpan waitTime = TimeSpan.FromMinutes(RepoConfig.instance.statUpdatePeriodMinutes);
        while (true)
        {
            await Task.Delay(waitTime);

            List<SceneSaverSaveEntry> entries = new();
            bool resetWeekly = lastUpdated.DayOfWeek == DayOfWeek.Sunday && DateTime.Now.DayOfWeek == DayOfWeek.Monday;
            int saveCount = 0;
            int downloadSum = 0;
            int downloadSumWeek = 0;
            foreach (string dir in Directory.EnumerateDirectories(SaveStore.SAVE_PATH))
            {
                try
                {
                    saveCount++;
                    FileInfo finf = new(Path.Combine(dir, SceneSaverSaveEntry.FILENAME));
                    SceneSaverSaveEntry metadata = await SaveStore.ReadMetadata(Path.GetFileName(dir));

                    if (resetWeekly)
                    {
                        metadata.downloadCountWeek = 0;
                        await SaveStore.WriteMetadata(metadata);
                    }

                    downloadSum += metadata.downloadCount;
                    downloadSumWeek += metadata.downloadCountWeek;
                    entries.Add(metadata);
                }
                catch (Exception ex)
                {
                    Program.logger.LogError("Exception when trying to read directory {dir} - {ex}", dir, ex);
                }
            }

            popularSaves.Clear();
            popularSaves.AddRange(entries.OrderByDescending(x => x.downloadCount).Take(RepoConfig.instance.maxPopularSaves));
            popularSavesWeekly.Clear();
            popularSavesWeekly.AddRange(entries.OrderByDescending(x => x.downloadCountWeek).Take(RepoConfig.instance.maxPopularSaves));

            totalSaves = saveCount;
            totalDownloads = downloadSum;
            totalDownloadsWeek = downloadSumWeek;
            lastUpdated = DateTime.Now;
        }
    }

    static RepoInfoAccumulator()
    {
        Init();
    }

    static readonly List<SceneSaverSaveEntry> popularSaves = new();
    static readonly List<SceneSaverSaveEntry> popularSavesWeekly = new();
    static readonly Queue<SceneSaverSaveEntry> recentSaves = new();

    public static IEnumerable<SceneSaverSaveEntry> PopularSaves => popularSaves;
    public static IEnumerable<SceneSaverSaveEntry> PopularSavesWeekly => popularSavesWeekly;
    public static IEnumerable<SceneSaverSaveEntry> RecentSaves => recentSaves;

    internal static int totalDownloads;
    internal static int totalDownloadsWeek;
    internal static int totalSaves;
    internal static DateTime lastUpdated = DateTime.Now;

    public static void NewSaveCreated(SceneSaverSaveEntry entry)
    {
        recentSaves.Enqueue(entry);
        if (recentSaves.Count > RepoConfig.instance.maxRecentSaves)
            recentSaves.Dequeue();
    }

    static void Init()
    {
        try
        {
            Task.Run(PeriodicallyUpdateStats);
        }
        catch (Exception ex)
        {
            Program.logger.LogError("Exception in periodic stat update: {ex}", ex);
            Init();
        }
    }
}
