using Jevil.Tweening;
using SceneSaverBL.Exceptions;
using SceneSaverBL.Interfaces;
using Il2CppSLZ.Marrow.SceneStreaming;
using System.ComponentModel;
using BoneLib.Notifications;

namespace SceneSaverBL;

internal static class Saves
{
    private static Page savesCategory;
    const string DATE_FORMAT = "s";
    //todo: static FileSystemWatcher dupeWatcher;

    static FileSystemWatcher saveWatcher;
    static readonly char[] invalidChars = Path.GetInvalidFileNameChars();
    static readonly List<ISaveFile> saves = new();
    static float lastSaveTime;
    
    private static Page dupeMenu;
    static Vector3 extraDupeOffset;
    static Bounds displayBounds;
    static Bounds calcBounds;
    static Transform boundsVis;
    static PositionTween currPosTween;
    static bool boxKeepAlive;

    static Vector3 GetTotalOffset()
    {
        Vector3 centerBottom = displayBounds.center;
        centerBottom.y = displayBounds.min.y;
        Vector3 wsOffset = SceneSaverBL.desiredDupePos.HasValue ? SceneSaverBL.desiredDupePos.Value - centerBottom : Vector3.zero;
        wsOffset += extraDupeOffset;
        return wsOffset;
    }

    internal static async Task Init(Page parentCategory)
    {
        savesCategory = parentCategory.CreatePage("SceneSaver Saves", Color.white);
        dupeMenu = savesCategory.CreatePage("Dupe spawning", Color.green);

        var pageLink = parentCategory.Elements.First(e => e.ElementName == savesCategory.Name);
        parentCategory.Remove(pageLink);
        string savFolderRoot = Path.GetDirectoryName(SceneSaverBL.saveDir)!;
        string dupFolderRoot = Path.GetDirectoryName(SceneSaverBL.dupesDir)!;
        if (!Directory.Exists(savFolderRoot)) Directory.CreateDirectory(savFolderRoot);
        //if (!Directory.Exists(dupFolderRoot)) Directory.CreateDirectory(dupFolderRoot);
        if (!Directory.Exists(SceneSaverBL.saveDir)) Directory.CreateDirectory(SceneSaverBL.saveDir);
        //if (!Directory.Exists(SceneSaverBL.dupesDir)) Directory.CreateDirectory(SceneSaverBL.dupesDir);

        saveWatcher = new FileSystemWatcher(SceneSaverBL.saveDir, "*.ssbl");
        saveWatcher.Changed += CheckChangedSave;
        saveWatcher.Created += CheckChangedSave;
        saveWatcher.Deleted += CheckChangedSave;
        saveWatcher.EnableRaisingEvents = true;
        await InitSaves();
    }

    private static async Task InitSaves()
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "Initialize " + Directory.GetFiles(SceneSaverBL.saveDir, "*.ssbl").Length + " file(s)");
#endif

        foreach (string savePath in Directory.EnumerateFiles(SceneSaverBL.saveDir, "*.ssbl"))
        {
            ISaveFile? save = await CreateSave(savePath);
            if (save is not null)
                saves.Add(save);
            else
                SceneSaverBL.Warn("Failed to load save file: " + savePath);
        }
    }

    private static void CheckChangedSave(object sender, FileSystemEventArgs e)
    {
        if (SceneSaverBL.currentlySaving) return;
        if (Directory.Exists(e.FullPath)) return; // if the path changed was a folder (for some reason)

        SceneSaverBL.Log($"Filesystem watcher: File {e.ChangeType} @ {e.FullPath}");

        // wait a bit in case the file is still being written to
        if (e.ChangeType.HasFlag(WatcherChangeTypes.Created))
            Task.Delay(100).RunOnFinish(() => CreateSave(e.FullPath).RunOnFinish(file => { if (file is not null) saves.Add(file); }));
        else if (e.ChangeType.HasFlag(WatcherChangeTypes.Renamed))
            RenameSave(e.FullPath);
        else if (e.ChangeType.HasFlag(WatcherChangeTypes.Deleted))
            RemoveSave(e.FullPath);
        else SceneSaverBL.Log("Not intreracting with change type: " + e.ChangeType);
    }

    internal static void ShowBoneMenu()
    {
#if DEBUG
        SceneSaverBL.Log("Clearing saves category.");
#endif
        savesCategory.RemoveAll();

        foreach (ISaveFile save in saves)
        {
            // i dont think it matters toooooooo tooooooooo much if we waste cycles like this, because BoneMenu UI elements are pooled
            // plus if the user changes levels we'd need to do this anyway
            save.PopulateBoneMenu(savesCategory);
        }

        Menu.OpenPage(savesCategory);
    }

    public static async Task<ISaveFile?> CreateSave(string path)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "SSBL create save " + Path.GetFileName(path));
#endif
        try
        {
            using FileStream fs = File.OpenRead(path);
            using BufferedStream bs = new(fs);
            ISaveFile save = await DeserializationBroker.CreateSaveFromFile(path);
            SceneSaverBL.Log($"Created save (V{save.Version}) from file: {Path.GetFullPath(path)}");
            bs.Position = SaveUtils.FORMAT_ID_LEN; // THIS IS SO CLUDGY
            await save.Read(bs);
            return save;
            //SceneSaverBL.runOnMainThread.Enqueue(() => save.PopulateBoneMenu(savesCategory)); dont need to queue bonemenu population - opening bm calls PopulateBoneMenu anyway
        }
        catch (Exception ex) 
        { 
            SceneSaverBL.Error(ex);
            return null;
        }
    }

    static async void RenameSave(string newPath)
    {
        try
        {
            foreach (ISaveFile save in saves)
            {
                if (!save.ExistsOnDisk())
                    await save.SetFilePath(newPath);
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
        {
#if DEBUG
            SceneSaverBL.Log($"Removing save @ idx {removedIdx} from {saves[removedIdx]}");
#endif
            saves.RemoveAt(removedIdx);
        }
#if DEBUG
        else
            SceneSaverBL.Warn("SSBL was told that a file was deleted from the Saves folder, but no save seems to be deleted! What gives?");
#endif
    }

    internal static async Task DoSave()
    {
        // 3sec cooldown to prevent accidental duplicate saves
        if (lastSaveTime + 3 > Time.realtimeSinceStartup) return;
        lastSaveTime = Time.realtimeSinceStartup;

        string pathConflicting = Pathify(SceneSaverBL.saveDir, GetFilename(), SaveUtils.FILE_EXTENSION);
        string path = GetAcceptableFilePath(pathConflicting);

        ISaveFile save =  SerializationBroker.CreateSaveAt(path);
#if DEBUG
        SceneSaverBL.Log($"Created SceneSaverBL save file at {path}");
#endif

        Poolee[] poolees = SelectionZone.Instance.GetPoolees();

        if (poolees.Length == 0)
        {
            var notif = new Notification()
            {
                Message = "You can't make a save out of zero objects!",
                Type = NotificationType.Error,
            };
            return;
        }
        
        Exception? ex = await AsyncUtilities.WrapNoThrow(save.Construct, poolees, (ConstraintTracker[])GameObject.FindObjectsOfType<ConstraintTracker>());
#if DEBUG
        if (ex is not null)
        {
            SceneSaverBL.Error($"Exception when constructing V{save.Version} save: " + ex);
            return;
        }

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

        ISaveFile loadedSave = await CreateSave(path);
        saves.Add(loadedSave);
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

    private static int EstFileSize(Poolee[] poolees)
    {
        int sum = poolees.Length * Const.SizeV3 * 3;
        sum += poolees.Sum(p => p.SpawnableCrate.Barcode.ID.Length);
        sum += ConfigVars.previewSize * ConfigVars.previewSize * 3 / 25; // the JPEG format, at 75 (default) quality has a compression ratio (vs BMP) of around 1/25
        return sum;
    }

    private static string Pathify(string dir, string nameNoExt, string ext) => Path.Combine(dir, nameNoExt) + "." + SaveUtils.FILE_EXTENSION;

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

    public static void OpenDupeMenu(ISaveFile save, string name, Action<Vector3?> load)
    {
        DupeTutorial.ShowIfUnseen();

        (displayBounds, calcBounds) = save.GetBoundsForDupeAndDisplay();
        dupeMenu.RemoveAll();
        dupeMenu.Name = $"Dupe: {name}";
        dupeMenu.CreateFunction("Load", Color.Lerp(Color.white, Color.green, 0.5f), () => { load.InvokeSafeSync(GetTotalOffset()); CloseDupeBox(); });
        dupeMenu.CreateFunction("Set position", Color.white, () => AsyncUtilities.WrapNoThrow(GetPositionAndUpdate).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        //todo: pass back to save to add load/preview maybe? or possibly just make saves auto-do it when they see an existing position

        dupeMenu.CreateFunction("Tutorial", Color.gray, () => DupeTutorial.Show());

        //dupeMenu.CreateFunction("How to set pos?", Color.gray, () => Menu.DisplayDialog("SSBL Dupe Tutorial", "This is a prerelease build of SSBL v2.0.0, so I haven't finished the dupe tutorial.\nJust make a finger gun at the ground wherever you want to spawn the dupe."));

        Page offsetPage = dupeMenu.CreatePage("Extra offsets", Color.gray);
        offsetPage.CreateFloat("X", Color.red, extraDupeOffset.x, 0.25f, -10, 10, x =>
        {
            extraDupeOffset.x = x;
            UpdateDupeBox();
        });
        offsetPage.CreateFloat("Y", Color.green, extraDupeOffset.y, 0.25f, -10, 10, y =>
        {
            extraDupeOffset.y = y;
            UpdateDupeBox();
        });
        offsetPage.CreateFloat("Z", Color.blue, extraDupeOffset.z, 0.25f, -10, 10, z =>
        {
            extraDupeOffset.z = z;
            UpdateDupeBox();
        });

        Menu.OpenPage(dupeMenu);

        AsyncUtilities.WrapNoThrow(SpawnDupeBox).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }
    
    static async Task GetPositionAndUpdate()
    {
        boxKeepAlive = true;
        await FingerOffset.MenuGetPosition();

        UpdateDupeBox();
        boxKeepAlive = false;
    }

    static async Task SpawnDupeBox()
    {
        if (boundsVis != null)
        {
#if DEBUG
            SceneSaverBL.Log("Closing previous preview visualization");
#endif
            CloseDupeBox();
            await UniTask.Yield();
        }

        GameObject boundsLinesPrefab = await Assets.Prefabs.FullsavePreviewBounds.GetAsync();
        GameObject boundsLinesInstance = GameObject.Instantiate(boundsLinesPrefab);
        boundsVis = boundsLinesInstance.transform;

        boundsVis.localScale = displayBounds.size;
        boundsVis.position = Vector3.zero;
        boundsLinesInstance.SetActive(true);

        await UniTask.Yield();

        Vector3 centerBottom = displayBounds.center;
        centerBottom.y = displayBounds.min.y;
        Vector3 wsOffset = GetTotalOffset();
        boundsVis.transform.position = centerBottom + wsOffset;
    }

    static void UpdateDupeBox()
    {
        if (boundsVis == null)
        {
#if DEBUG
            SceneSaverBL.Warn("Cannot update dupe box when it's not spawned!");
#endif
            return;
        }
        //Vector3 currPosCenterBottom = boundsVis.position;
        //currPosCenterBottom.y -= displayBounds.size.y / 2;

        Vector3 centerBottom = displayBounds.center;
        centerBottom.y = displayBounds.min.y;
        Vector3 wsOffset = GetTotalOffset();
        
        Vector3 desiredPos = centerBottom + wsOffset;
        //desiredPos.y -= displayBounds.size.y / 2;
        float dist = Vector3.Distance(boundsVis.position, desiredPos);
        if (dist < 0.5f)
            dist = 0.5f;

        float tweenLen = Mathf.Log(dist, 5f);
        tweenLen = Mathf.Clamp(tweenLen, 0.1f, 5f);

#if DEBUG
        SceneSaverBL.Log($"Tweening dupe box from {boundsVis.position} to {desiredPos} ({Vector3.Distance(boundsVis.position, desiredPos):0.00}m) in {tweenLen} sec");
#endif

        if (currPosTween?.Active ?? false)
            currPosTween.Stop();

        currPosTween = boundsVis.TweenPosition(desiredPos, tweenLen)
            .UseCustomInterpolator(inVal => Mathf.Pow(inVal, 0.25f));
        //const float EXIST_TIME = 25;
    }

    static void CloseDupeBox()
    {
        if (boundsVis == null)
        {
#if DEBUG
            SceneSaverBL.Warn("Cannot close dupe box when it's not even open!");
#endif
            return;
        }

        FingerOffset.End();
        float tweenLen = Mathf.Pow(Vector3.Magnitude(displayBounds.size), 0.25f);
        TweenPreviewBoundsForEnd(boundsVis, tweenLen);
        extraDupeOffset = Vector3.zero;
        SceneSaverBL.desiredDupePos = null;
    }

    static void TweenPreviewBoundsForEnd(Transform bounds, float originalTweenLen)
    {
#if DEBUG
        SceneSaverBL.Log("Tweening dupe bounds vis scale to 0");
#endif
        bounds.TweenLocalScale(Vector3.zero, originalTweenLen * 2)
                .UseCustomInterpolator(inVal => Mathf.Pow(inVal, 4f))
                .RunOnFinish(bounds.gameObject.Destroy);
        //.RunOnFinish(() => GameObject.Instantiate(Assets.Prefabs.ObjectBoundsDestroyed))
    }

    public static void MenuClosed()
    {
        if (boundsVis == null)
            return;

        if (!boxKeepAlive)
            CloseDupeBox();
#if DEBUG
        else
            SceneSaverBL.Log("Keeping box alive due to flag being set.");
#endif
    }
}
