using System;
using System.Text.Encodings;
using System.Text;
using System.Security.Cryptography;
using Tomlet;

namespace SceneSaverRepo;

public class RepoConfig
{
    internal static RepoConfig instance;
    const string FILE_PATH = "./repoConfig.toml";
    internal static MD5 hasher = MD5.Create();
    internal static Encoding encoder = Encoding.UTF8;

    static RepoConfig()
    {
        if (!File.Exists(FILE_PATH)) 
            File.WriteAllText(FILE_PATH, TomletMain.TomlStringFrom(new RepoConfig()));
        string cfgTxt = File.ReadAllText(FILE_PATH);

        instance = TomletMain.To<RepoConfig>(cfgTxt);
        if (instance.ownerSaltSeed == -1)
            instance.ownerSaltSeed = Random.Shared.Next(int.MaxValue / 2);
        File.WriteAllText(FILE_PATH, TomletMain.TomlStringFrom(instance)); // update fields between updates
    }

    public string repoName = "SSBL Repo";
    public string url = "";
    public int statUpdatePeriodMinutes = 15;
    public int maxFilesizeKB = 512;
    public int maxPopularSaves = 100;
    public int maxRecentSaves = 100;
    public int ownerSaltSeed = -1; // lol

    public string[] dontAllowSaveNamesWith =
    {
        "fag", // its ok i can say it, i suck dick
    };

    public string[] dontAllowSaveNamesWithRegex =
    {
        @"f *(a|4)+ *g+ *(i|o|e)+ *(t|7)+",
    };
}
