using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SceneSaverRepo.Data;

#if NET7_0_OR_GREATER
[System.Text.Json.Serialization.JsonSerializable(typeof(SceneSaverSaveEntry))]
#endif
public class SceneSaverSaveEntry
{
    public const string FILENAME = "ssblmeta";

    public static readonly SceneSaverSaveEntry err = new()
    {
        name = "error",
        hash = "0000000000000000",
        tag = "0000"
    };

#if NET7_0_OR_GREATER
    public static async Task<SceneSaverSaveEntry> ReadFromFile(string path)
    {
        string fileStr = await File.ReadAllTextAsync(path);
        return Tomlet.TomletMain.To<SceneSaverSaveEntry>(fileStr);
    }
#endif


    public string name { get; set; } = "";
    public string hash { get; set; } = "";
    public string tag { get; set; } = "";
    public string owner { get; set; } = "";
    public int version { get; set; }
    public int downloadCount { get; set; }
    public int downloadCountWeek { get; set; }
    public DateTime? expire { get; set; }

#if !NET7_0_OR_GREATER

    public override bool Equals(object obj)
    {
        return obj is SceneSaverSaveEntry entry &&
               entry == this;
    }

#else

    public override bool Equals(object? obj)
    {
        return obj is SceneSaverSaveEntry entry &&
               entry == this;
    }

#endif

#pragma warning disable IDE0070 // Use 'System.HashCode'
    public override int GetHashCode()
#pragma warning restore IDE0070 // Use 'System.HashCode'
    {
        int hashCode = -1527566069;
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(name);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(hash);
        hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(tag);
        hashCode = hashCode * -1521134295 + version.GetHashCode();
        hashCode = hashCode * -1521134295 + downloadCount.GetHashCode();
        hashCode = hashCode * -1521134295 + expire.GetHashCode();
        return hashCode;
    }

    public TimeSpan? TimeUntilExpired()
    {
        if (expire is null) return null;

        return expire - DateTime.Now;
    }

    public static bool operator ==(SceneSaverSaveEntry lhs, SceneSaverSaveEntry rhs) {
        return lhs.hash == rhs.hash;
    }

    public static bool operator !=(SceneSaverSaveEntry lhs, SceneSaverSaveEntry rhs)
    {
        return !(lhs == rhs);
    }
}
