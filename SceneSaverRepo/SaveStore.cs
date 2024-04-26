using SceneSaverRepo.Data;
using System.IO;
using Tomlet;

namespace SceneSaverRepo;

// the move here is basically to use the filesystem as a glorified hashtable
// this lets me avoid keeping everything in memory and lets me put off using proper databases for another day
public static class SaveStore
{
    public const string SAVE_PATH = "./SSBLRepo/Data";
    const string SAVE_FILENAME = "Save.ssbl";
    const string IP_ADDRS_FILE = "./ownerHashes.toml";

    private static Dictionary<string, string> ownerHashToIpAddrs = new();

    static SaveStore()
    {
        if (!File.Exists(IP_ADDRS_FILE))
            File.WriteAllText(IP_ADDRS_FILE, TomletMain.TomlStringFrom(ownerHashToIpAddrs));
        string cfgTxt = File.ReadAllText(IP_ADDRS_FILE);

        ownerHashToIpAddrs = TomletMain.To<Dictionary<string, string>>(cfgTxt);
    }

    internal static async Task CleanupExpiredFiles()
    {
        List<string> markedDirectoriesForDeletion = new();

        foreach (string directory in Directory.EnumerateDirectories(SAVE_PATH))
        {
            if (Directory.GetFiles(directory).Length < 2)
            {
                markedDirectoriesForDeletion.Add(directory);
                continue;
            }

            string metafile = Path.Combine(SAVE_PATH, SceneSaverSaveEntry.FILENAME);
            string savefile = Path.Combine(SAVE_PATH, SAVE_FILENAME);
            if (!File.Exists(metafile))
            {
                markedDirectoriesForDeletion.Add(directory);
                Program.logger.LogInformation("Deleting directory {directory} because metafile was missing!", directory);
                continue;
            }

            if (!File.Exists(savefile))
            {
                markedDirectoriesForDeletion.Add(directory);
                Program.logger.LogInformation("Deleting directory {directory} because savefile was missing!", directory);
                continue;
            }

            SceneSaverSaveEntry entry;
            try
            {
                entry = await SceneSaverSaveEntry.ReadFromFile(savefile);
            }
            catch (Exception ex)
            {
                markedDirectoriesForDeletion.Add(directory);
                Program.logger.LogError("Deleting directory {directory} because savefile failed to deserialize! Thrown exception: {ex}", directory, ex);
                continue;
            }

            if (entry.expire.HasValue && entry.expire < DateTime.Now)
            {
                markedDirectoriesForDeletion.Add(directory);
                Program.logger.LogInformation("Deleting directory {directory} because it has expired!", directory);
                continue;
            }
        }

        foreach (string directory in markedDirectoriesForDeletion) 
        {
            Directory.Delete(directory, true);
        }

        Program.logger.LogInformation("Pruned {count} expired/empty directories!", markedDirectoriesForDeletion.Count);
    }

    static string GetDirFor(SceneSaverSaveEntry entry)
    {
        return Path.GetFullPath(Path.Combine(SAVE_PATH, entry.tag));
    }

    // this WILL blow up! test thoroughly!
    public static async Task<string> GetTag(string incomingHash)
    {
        string tag = incomingHash.Substring(0, 4);
        string dir = Path.Combine(SAVE_PATH, tag);
        string entryFile = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return tag;
        }
        if (!File.Exists(entryFile))
        {
            return tag;
        }

        SceneSaverSaveEntry entry = await SceneSaverSaveEntry.ReadFromFile(entryFile);
        while (entry.hash != incomingHash)
        {
            if (tag.Length + 1 > incomingHash.Length)
            {
                throw new IndexOutOfRangeException("!!! Hashes should not collide this much! Hash causing problems: " + incomingHash);
            }

            tag = incomingHash.Substring(0, tag.Length + 1);
            dir = Path.Combine(SAVE_PATH, tag);


            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return tag;
            }

            entryFile = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);
            entry = await SceneSaverSaveEntry.ReadFromFile(entryFile);
        }

        return tag;
    }

    public static async Task WriteMetadata(SceneSaverSaveEntry entry)
    {
        string tomletStr = TomletMain.TomlStringFrom(entry);
        string path = Path.Combine(GetDirFor(entry), SceneSaverSaveEntry.FILENAME);
        await File.WriteAllTextAsync(path, tomletStr); // write metadata either way to update expiry date/time
    }

    public static async Task WriteSave(SceneSaverSaveEntry entry, byte[] file)
    {
        string saveDir = GetDirFor(entry);
        string savePath = Path.Combine(saveDir, SAVE_FILENAME);

        if (File.Exists(savePath)) return; // avoid writing. contents are already assured to be the same by MD5 hash (hopefully there are no hash collisions?)
        await File.WriteAllBytesAsync(savePath, file);
    }

    public static async Task<string> GetHashWithTag(string tag)
    {
        string dir = Path.Combine(SAVE_PATH, tag);
        string path = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);
        if (Directory.Exists(dir)) return (await SceneSaverSaveEntry.ReadFromFile(path)).hash;

        return "";
        //throw new FileNotFoundException();
    }

    public static bool TagExists(string tag)
    {
        string dir = Path.Combine(SAVE_PATH, tag);
        return Directory.Exists(dir);
        //throw new FileNotFoundException();
    }

    public static async Task<SceneSaverSaveEntry> ReadMetadata(string tag)
    {
        string dir = Path.Combine(SAVE_PATH, tag);
        string metaPath = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);

        if (Directory.Exists(dir))
        {
            return await SceneSaverSaveEntry.ReadFromFile(metaPath);
        }

        return SceneSaverSaveEntry.err;
    }

    public static async Task<(byte[]?, SceneSaverSaveEntry)> GetFileWithTag(string tag)
    {
        string dir = Path.Combine(SAVE_PATH, tag);
        string metaPath = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);
        string path = Path.Combine(dir, SAVE_FILENAME);
        if (Directory.Exists(dir))
        {
            return (File.ReadAllBytes(path), await SceneSaverSaveEntry.ReadFromFile(metaPath));
        }

        return (null, SceneSaverSaveEntry.err);
    }

    public static FileStream? GetFile(string tag)
    {
        string dir = Path.Combine(SAVE_PATH, tag);
        //string metaPath = Path.Combine(dir, SceneSaverSaveEntry.FILENAME);
        string path = Path.Combine(dir, SAVE_FILENAME);

        if (Directory.Exists(dir))
        {
            return File.OpenRead(path);
        }
        else return null;

        //throw new Exception($"Tag {tag} does not have a file");
    }

    public static async Task IncrementFileDownloadCount(SceneSaverSaveEntry metadata)
    {
        metadata.downloadCount++;

        if (metadata.expire is not null)
        {
            if (metadata.TimeUntilExpired() < TimeSpan.FromHours(12))
            {
                metadata.expire = DateTime.Now + TimeSpan.FromHours(18);
            }
            else
            {
                metadata.expire = metadata.expire + TimeSpan.FromHours(2);
            }
        }

        await WriteMetadata(metadata);
    }

    public static string HashIpForOwnerId(string ip)
    {
        Random randy = new Random(ip.GetHashCode() + RepoConfig.instance.ownerSaltSeed);
        byte[] ownerBytes = RepoConfig.encoder.GetBytes(ip);
        byte[] randomSalt = new byte[randy.Next(1024)];
        randy.NextBytes(randomSalt);
        byte[] combined = new byte[ownerBytes.Length + randomSalt.Length];

        Buffer.BlockCopy(ownerBytes, 0, combined, 0, ownerBytes.Length);
        Buffer.BlockCopy(randomSalt, 0, combined, ownerBytes.Length, randomSalt.Length);

        byte[] hashed = RepoConfig.hasher.ComputeHash(combined);

        return Convert.ToHexString(hashed).ToUpper();
    }
}
