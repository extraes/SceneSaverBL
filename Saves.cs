using BoneLib.BoneMenu;
using BoneLib.BoneMenu.Elements;
using Cysharp.Threading.Tasks;
using Jevil;
using SceneSaverBL.Exceptions;
using SceneSaverBL.Interfaces;
using SLZ.Bonelab;
using SLZ.Marrow.Pool;
using SLZ.Marrow.SceneStreaming;
using SLZ.Props;
using SLZ.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class Saves
{
    private static MenuCategory savesCategory;
    const string DATE_FORMAT = "s";
    const string FILE_EXTENSION = "ssbl";
    //todo: static FileSystemWatcher dupeWatcher;
    static FileSystemWatcher saveWatcher;
    static readonly char[] invalidChars = Path.GetInvalidFileNameChars();
    static readonly List<ISaveFile> saves = new();
    static float lastSaveTime;

    internal static void Init(MenuCategory parentCategory)
    {
        savesCategory = parentCategory.CreateCategory("SceneSaver Saves", Color.white);
        parentCategory.Elements.Remove(savesCategory);
        string savFolderRoot = Path.GetDirectoryName(SceneSaverBL.saveDir);
        string dupFolderRoot = Path.GetDirectoryName(SceneSaverBL.dupesDir);
        if (!Directory.Exists(savFolderRoot)) Directory.CreateDirectory(savFolderRoot);
        if (!Directory.Exists(dupFolderRoot)) Directory.CreateDirectory(dupFolderRoot);
        if (!Directory.Exists(SceneSaverBL.saveDir)) Directory.CreateDirectory(SceneSaverBL.saveDir);
        if (!Directory.Exists(SceneSaverBL.dupesDir)) Directory.CreateDirectory(SceneSaverBL.dupesDir);

        saveWatcher = new FileSystemWatcher(SceneSaverBL.saveDir, "*.ssbl");
        saveWatcher.Changed += CheckChangedSave;
        saveWatcher.EnableRaisingEvents = true;

        foreach (string save in Directory.EnumerateFiles(SceneSaverBL.saveDir, "*.ssbl")) 
            CreateSave(save);
    }

    private static void CheckChangedSave(object sender, FileSystemEventArgs e)
    {
        if (SceneSaverBL.currentlySaving) return;
        if (Directory.Exists(e.FullPath)) return; // if the path changed was a folder (for some reason)

        if (e.ChangeType.HasFlag(WatcherChangeTypes.Created))
            CreateSave(e.FullPath);
        else if (e.ChangeType.HasFlag(WatcherChangeTypes.Renamed))
            RenameSave(e.FullPath);
        else if (e.ChangeType.HasFlag(WatcherChangeTypes.Deleted))
            RemoveSave(e.FullPath);
        else throw new InvalidEnumArgumentException("Unrecognized file system event: " + e.ChangeType);
    }

    internal static void ShowBoneMenu()
    {
#if DEBUG
        SceneSaverBL.Log("Clearing saves category.");
#endif
        savesCategory.Elements.Clear();

        foreach (ISaveFile save in saves)
        {
            // i dont think it matters toooooooo tooooooooo much if we waste cycles like this, because BoneMenu UI elements are pooled
            // plus if the user changes levels we'd need to do this anyway
            save.PopulateBoneMenu(savesCategory);
        }

        MenuManager.SelectCategory(savesCategory);
    }

    static async void CreateSave(string path)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "SSBL create save " + Path.GetFileName(path));
#endif
        try
        {
            using FileStream fs = File.OpenRead(path);
            ISaveFile save = await DeserializationBroker.CreateSaveFromFile(path);
            SceneSaverBL.Log($"Created save (V{save.Version}) from file: {Path.GetFullPath(path)}");
            fs.Position = 5;
            await save.Read(fs);
            saves.Add(save);
            //SceneSaverBL.runOnMainThread.Enqueue(() => save.PopulateBoneMenu(savesCategory)); dont need to queue bonemenu population - opening bm calls PopulateBoneMenu anyway
        }
        catch (Exception ex) 
        { 
            SceneSaverBL.Error(ex); 
        }
    }

    static async void RenameSave(string newPath)
    {
        try
        {
            foreach (ISaveFile save in saves)
            {
                if (!save.ExistsOnDisk()) await save.SetFilePath(newPath);
            }
            }
        catch (Exception ex)
        {
            SceneSaverBL.Error(ex);
        }
    }

    static void RemoveSave(string path)
    {
        int removedIdx = saves.FindIndex(s => !s.ExistsOnDisk());
        if (removedIdx != -1)
            saves.RemoveAt(removedIdx);
        else
            throw new FilesystemStateException("SSBL was told that a file was deleted from the Saves folder, but no save seems to be deleted! What gives?");
    }

    internal static async Task DoSave()
    {
        // 3sec cooldown to prevent accidental duplicate saves
        if (lastSaveTime + 3 > Time.realtimeSinceStartup) return;
        lastSaveTime = Time.realtimeSinceStartup;

        string pathConflicting = Pathify(SceneSaverBL.saveDir, GetFilename(), FILE_EXTENSION);
        string path = GetAcceptableFilePath(pathConflicting);

        ISaveFile save =  SerializationBroker.CreateSaveAt(path);
#if DEBUG
        SceneSaverBL.Log($"Created SceneSaverBL save file at {path}");
#endif

        AssetPoolee[] poolees = SelectionZone.Instance.GetPoolees();
        
        Exception ex = await AsyncUtilities.WrapNoThrow(save.Construct, poolees, (ConstraintTracker[])GameObject.FindObjectsOfType<ConstraintTracker>());
#if DEBUG
        SceneSaverBL.Log($"Constructed save file with {poolees.Length} objects. Estimated file size: {EstFileSize(poolees)}");
#endif

        using (FileStream fileStream = File.OpenWrite(path))
        {
            await SaveUtils.WriteIdentifier(fileStream, save);
            await save.Write(fileStream);
#if DEBUG
            SceneSaverBL.Log("Wrote file successfully. Actual size: " + fileStream.Position);
#endif
        }

        CreateSave(path);
    }

    private static string GetAcceptableFilePath(string filePath)
    {
        int iteration = 0;
        string dir = Path.GetDirectoryName(filePath);
        string _fileName = RemoveChars(Path.GetFileNameWithoutExtension(filePath));
        string fileName = _fileName;
        string ext = Path.GetExtension(filePath);

        while(File.Exists(Pathify(dir, fileName, ext)))
        {
            fileName = _fileName + '_' + iteration++;
        }
        
        return Pathify(dir, fileName, ext);
    }

    private static string RemoveChars(string str)
    {
        foreach (char invalidChar in invalidChars)
        {
            str = str.Replace(invalidChar, ' ');
        }

        return str;
    }

    private static int EstFileSize(AssetPoolee[] poolees)
    {
        int sum = poolees.Length * Const.SizeV3 * 3;
        sum += poolees.Sum(p => p.spawnableCrate.Barcode.ID.Length);
        sum += Prefs.previewSize * Prefs.previewSize * 3 / 25; // the JPEG format, at 75 (default) quality has a compression ratio (vs BMP) of around 1/25
        return sum;
    }

    private static string Pathify(string dir, string nameNoExt, string ext) => Path.Combine(dir, nameNoExt) + "." + FILE_EXTENSION;

    public static string GetFilename()
    {
        string level = SceneStreamer.Session.Level.Title;
        int campaignSepIdx = level.IndexOf(" - ");
        level = campaignSepIdx == -1 ? level : level.Substring(campaignSepIdx + 2);
        level = level.Trim().Replace("BONELAB", "BL"); // shorten where possible
        level = level.Length <= 13 ? level : level.Substring(0, 7) + "...";
        const string separator = " - ";
        string date = RemoveChars(DateTime.Now.ToString(DATE_FORMAT)).Replace('T', '@');

        return level + separator + date;
    }
}
