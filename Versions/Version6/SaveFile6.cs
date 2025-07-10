using BoneLib.Notifications;
using Il2CppSLZ.Marrow.SceneStreaming;
using Jevil.Tweening;
using MelonLoader.Utils;
using SceneSaverBL.Interfaces;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace SceneSaverBL.Versions.Version6;

public class SaveFile6 : ISaveFile
{
#if DEBUG
    static Thread mainThread;
#endif
    internal static readonly Encoding StringEncoding = Encoding.UTF8;

    Page myPage;

    private Header6 header = new();
    Page headerCategory;

    SaveContext6 ctx;

    string filePath;
    bool readCompleted;
    Texture2D previewTexture;
    byte[] previewBytes;
    SavedPoolee6[] poolees;
    SavedConstraint6[] constraints;
    SavedPlank6[] planks;
    SavedTransform6[][] transforms;
    Action? executePostInit;

#if DEBUG
    bool dbgColors;
#endif

    public byte Version => 6;

    public async Task Construct(Poolee[] savingPoolees, ConstraintTracker[] allConstraints)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V6 Construct");
        Stopwatch sw = Stopwatch.StartNew();
#endif
        // puts planks at the very end of the poolee list. this shouldnt be too bad.
        savingPoolees = savingPoolees.OrderBy(p => SaveUtils.IsNewPlank(p.SpawnableCrate.Barcode.ID).HasValue ? PlankComparer6.GetOrderValue(p, savingPoolees) : -99).ToArray();
#if DEBUG
        SceneSaverBL.Log("Sorted poolees/planks to be saved in " + sw.ElapsedMilliseconds + "ms");
        SceneSaverBL.Log($"Find below a list of poolees to be saved in {filePath}");
        foreach (var poolee in savingPoolees)
            SceneSaverBL.Log($"    {poolee.name} from crate {poolee.SpawnableCrate.Barcode.ID}");
#endif

        allConstraints = allConstraints.Where(ct => ct.isHost).ToArray();
        List<Transform>[]? savingTransforms = null;
        ObjectDestructible[] savingPlanks = await GetPlanksToBeSaved(savingPoolees);
        byte[] levelBarcodeBytes = StringEncoding.GetBytes(SceneStreamer.Session.Level.Barcode.ID);
        byte[] usernameBytes = StringEncoding.GetBytes(SceneSaverBL.username ?? "Unknown");
        Bounds szBounds = SelectionZone.Instance.Bounds;
        header.previewData.size = szBounds.size;
        header.previewData.centerBottom = new Vector3(szBounds.center.x, szBounds.min.y, szBounds.center.z);
        header.previewData.pooleeBoundingBoxes = new Bounds[savingPoolees.Length];
        header.poolees = savingPoolees.Length;
        header.planks = savingPlanks.Length;
        header.hasSerializedTransforms = SceneSaverBL.isFullSave;
        header.mapBarcodeLen = (ushort)levelBarcodeBytes.Length;
        header.usernameLen = (byte)usernameBytes.Length;

        // cache obj completed material
        await Assets.Materials.SavingObjectCompletedMaterial.GetAsync();
        GameObject cameraFlash = await Assets.Prefabs.CameraFlash.GetAsync();
        GameObject polaroid = await Assets.Prefabs.Polaroid.GetAsync();
        Camera cam = SelectionZone.Instance.CreateCamera();

        constraints = new SavedConstraint6[allConstraints.Length];
        poolees = new SavedPoolee6[savingPoolees.Length];
        planks = new SavedPlank6[savingPlanks.Length];

        if (header.hasSerializedTransforms)
        {
            savingTransforms = await GetTransformsToBeSaved(savingPoolees);
            transforms = new SavedTransform6[savingTransforms.Length][];
            header.serializedTransformCounts = new ushort[transforms.Length];

            for (int i = 0; i < savingTransforms.Length; i++)
            {
                // initialize arrays in 2d array
                transforms[i] = new SavedTransform6[savingTransforms[i].Count];
                header.serializedTransformCounts[i] = (ushort)savingTransforms[i].Count;
            }
        }
        else
        {
            header.serializedTransformCounts = Array.Empty<ushort>();
            transforms = Array.Empty<SavedTransform6[]>();
        }

        //todo: rearchitect savedplank to work with poolees because planks can connect to each other, and thats an important thing we need to preserve

        ctx = new()
        {
            poolees = savingPoolees,
            allTrackers = allConstraints,
            planks = savingPlanks,
            transformsByPoolee = savingTransforms,
            mapBarcodeBytes = levelBarcodeBytes,
            usernameBytes = usernameBytes,
            strings = new(),
        };

        await AsyncUtilities.ForTimeSlice(0, poolees.Length, ConstructPoolee);

        await AsyncUtilities.ForTimeSlice(0, constraints.Length, ConstructConstraint);

        await AsyncUtilities.ForTimeSlice(0, planks.Length, ConstructPlank);

        // sort them so that *hopefully* the chance of a plank relying on something past its own index is unlikely
        //int _plankI = 0; //todo: this is bad code tbh. but whatever! fix it later!
        //planks = planks.OrderBy(p => Math.Max(Mathf.Max(p.DependsOnPoolees.Item1, p.DependsOnPoolees.Item2), _plankI++)).ToArray();
        
        //// and then we reconstruct the planks because if one depended on another, shit'd get "quirky" to say the least :)
        //await AsyncUtilities.ForTimeSlice(0, planks.Length, ConstructPlank);


        //#if DEBUG
        //        int tId = Thread.CurrentThread.ManagedThreadId;
        //#endif
        //foreach (var pt in ctx.pooleeTasks)
        //{
        //    if (pt is not null)
        //        await pt;
        //    else
        //}
        //#if DEBUG
        //        if (tId != Thread.CurrentThread.ManagedThreadId)
        //            SceneSaverBL.Warn($"SSBL V6 Construct went from being executed on thread {tId} to {Thread.CurrentThread.ManagedThreadId} - this is not good!");

        //#endif

        if (header.hasSerializedTransforms)
        {
            if (ConfigVars.fullsaveOverTime)
            {
                for (int i = 0; i < savingTransforms.Length; i++)
                {
                    List<Transform> tfms = savingTransforms[i];
                    await AsyncUtilities.ForTimeSlice(0, tfms.Count, idx => ConstructTransform(i, idx));
                }
            }
            else
            {
                await AsyncUtilities.ForTimeSlice(0, transforms.Length, ConstructTransforms);
            }
        }

        await UniTask.Yield();
        
        SaveUtils.CleanTrackers(ref constraints);
        header.constraints = constraints.Length;

#if DEBUG
        SceneSaverBL.Log("Pre-screenshot check-in");
        ps.Log();
#endif

        await UniTask.Yield();

        previewTexture = await Screenshotting.TakeScreenshotWith(cam);
        previewBytes = ImageConversion.EncodeToJPG(previewTexture, 90);

        header.previewLen = previewBytes.Length;

#if DEBUG
        ps.Log();
#endif

        CameraFella.PlayScreenshot();
        await Screenshotting.PerformEffects(cam.transform, previewTexture, cameraFlash, polaroid);
        cam.gameObject.Destroy();
    }

    public async Task Write(Stream stream)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "V6 Write");
        using var dlc = new DebugLineCounter(SceneSaverBL.instance.LoggerInstance, DebugLineCounter.Kind.LINE_NUMBER, "V6 Write");

        long lastPos = stream.Position;
        (long head, long codes, long preview, long pools, long straints, long planks, long trans) dataSizes = default;
        dataSizes.preview = previewBytes.Length;

        dlc.UpdateProgress();
#endif
        // not "using" because that closes its underlying streams apparently, which is a really bad thing considering the caller wants to use its stream afterward
        //BZip2OutputStream zipStream = new(stream);
        BufferedStream buffStream = new(stream);
        stream = buffStream;
        //using InflaterInputStream zipStream = new(stream, new Inflater(true),

        // configureawait because none of this needs to be kept on the main thread lol.
        await header.Write(stream).ConfigureAwait(false);

#if DEBUG
        dlc.UpdateProgress();
        dataSizes.head = stream.Position - lastPos;
        lastPos = stream.Position;
#endif

        await stream.WriteAsync(ctx.mapBarcodeBytes).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
#endif

        await stream.WriteAsync(ctx.usernameBytes).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
#endif

        await stream.WriteAsync(previewBytes).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
#endif

#if DEBUG
        lastPos = stream.Position;
        SceneSaverBL.Log("SaveFile6 Write: Strings begin @ pos " + buffStream.Position);
#endif

        await ctx.strings.Write(buffStream).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
        dataSizes.codes = stream.Position - lastPos;
        lastPos = stream.Position;
        SceneSaverBL.Log("SaveFile6 Write: Poolees begin @ pos " + buffStream.Position);
#endif

        for (int i = 0; i < poolees.Length; i++)
            await poolees[i].Write(stream).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
        await stream.FlushAsync().ConfigureAwait(false);
        dataSizes.pools = stream.Position - lastPos;
        lastPos = stream.Position;
        SceneSaverBL.Log("SaveFile6 Write: Constraints begin @ pos " + buffStream.Position);
#endif

        for (int i = 0; i < constraints.Length; i++)
            await constraints[i].Write(stream).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
        await stream.FlushAsync().ConfigureAwait(false);
        dataSizes.straints = stream.Position - lastPos;
        lastPos = stream.Position;
        SceneSaverBL.Log("SaveFile6 Write: Planks begin @ pos " + buffStream.Position);
#endif

        for (int i = 0; i < planks.Length; i++)
            await planks[i].Write(stream).ConfigureAwait(false);
#if DEBUG
        dlc.UpdateProgress();
        await stream.FlushAsync().ConfigureAwait(false);
        dataSizes.planks = stream.Position - lastPos;
        lastPos = stream.Position;
#endif

        for (int i = 0; i < transforms.Length; i++)
        {
            SavedTransform6[] currArr = transforms[i];
            for (int j = 0; j < currArr.Length; j++)
            {
                await currArr[j].Write(stream).ConfigureAwait(false);
            }
        }
#if DEBUG
        await stream.FlushAsync().ConfigureAwait(false);
        dataSizes.trans = stream.Position - lastPos;
        lastPos = stream.Position;

        SceneSaverBL.Log($"Wrote {dataSizes.head} ({Math.Round(dataSizes.head / 1024.0, 2)}KB) bytes of header data, " +
            $"{dataSizes.codes} ({Math.Round(dataSizes.codes / 1024.0, 2)}KB) bytes of barcode data, " +
            $"{dataSizes.preview} ({Math.Round(dataSizes.preview / 1024.0, 2)}KB) bytes of preview data, " +
            $"{dataSizes.pools} ({Math.Round(dataSizes.pools / 1024.0, 2)}KB) bytes of poolee data, " +
            $"{dataSizes.straints} ({Math.Round(dataSizes.straints / 1024.0, 2)}KB) bytes of constraint data, " +
            $"{dataSizes.planks} ({Math.Round(dataSizes.planks / 1024.0, 2)}KB) bytes of plank data, " +
            $"and {dataSizes.trans} ({Math.Round(dataSizes.trans / 1024.0, 2)}KB) bytes of transform data.");
        dlc.Success();
#endif

        SceneSaverBL.runOnMainThread.Enqueue(async () => await Stats.SaveCreated(header.hasSerializedTransforms));
    }

    public async Task Read(Stream stream)
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(header);

        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "V6 Read");
#endif

        stream.Position = header.DataReadPos;

        PrepareArrays();

        ctx.strings = new();
        await ctx.strings.Read(stream);

        // just to get off the main thread and not halt frametimes
        await Task.Run(() => ReadImpl(stream));
    }

    public async Task Initialize()
    {
        await Stats.SaveLoaded(header.hasSerializedTransforms);
#if DEBUG
        mainThread = Thread.CurrentThread;
        using DebugLineCounter dlc = new(SceneSaverBL.instance.LoggerInstance, DebugLineCounter.Kind.LINE_NUMBER, "V6 Initialize");
#endif
        ctx.nextPlankIdx = 0;
        ctx.constrainer = await SaveUtils.GetDummyConstrainer();
        ctx.boardGun = await SaveUtils.GetDummyBoardGun();
        SceneSaverBL.Log("Retrieved dummy constrainer " + ctx.constrainer.name);

        // clear spawned board cache so WaitForAnyBoard doesnt return way old boards
        SpawnerStates.Board.ClearBoardBacklog();

#if DEBUG
        dlc.UpdateProgress();
#endif

        await AsyncUtilities.ForTimeSlice(0, header.poolees, InitializePoolee, ConfigVars.timeSliceMs);

        // wait for the game to initialize all poolees
        // dont use UniTask.WhenAll because im a lazy fucker who doesnt want to deal with seeing which IL2CPP array i need to use for it to work properly
#if DEBUG
        dlc.UpdateProgress();

        Stopwatch sw = Stopwatch.StartNew();
#endif
        while (ctx.pooleeTasks.Any(t => t is not null && !t.IsCompleted))
            await UniTask.Yield();

#if DEBUG
        dlc.UpdateProgress();
        sw.Stop();
        SceneSaverBL.Log($"Waited an extra {sw.ElapsedMilliseconds} ms for poolees to finish initializing");
        SaveChecks.ThrowIfOffMainThread();
#endif

        // VVV doesnt do shit dumbass try harder. leave it to traversehierarchies to be resilient.
        //        int nullCount = ctx.poolees.Count(Extensions.INOC);
        //        if (nullCount != 0)
        //        {
        //            int timeoutMs = 2500;
        //#if DEBUG
        //            SceneSaverBL.Log($"!!! {nullCount} poolee(s) was/were null after initialization was supposed to be finished!!! Waiting an extra {timeoutMs}ms!");
        //            sw.Restart();
        //#endif

        //            Task noNullTimeout = Task.Delay(2500);

        //            while (!noNullTimeout.IsCompleted && ctx.poolees.Any(Extensions.INOC))
        //                await UniTask.Yield();

        //#if DEBUG
        //            sw.Stop();
        //            SceneSaverBL.Log($"Waited an EXTRA extra {sw.ElapsedMilliseconds} ms for poolees to de-null (from {nullCount} null(s) to {ctx.poolees.Count(Extensions.INOC)} null(s))");
        //#endif
        //        }

        // wait a bit for any UltEvents to fire/things to load sub-assets, if necessary
        await UniTask.Delay(125, true);

        if (header.hasSerializedTransforms)
        {
            await InitializeTransforms();
        }

#if DEBUG
        dlc.UpdateProgress();
#endif

        //todo: make constraints initialize immediately after their poolee inits (array is sorted by DependentOn, see SaveUtils.CleanTrackers)

        await AsyncUtilities.ForTimeSlice(0, header.constraints, InitializeConstraint, ConfigVars.timeSliceMs);

#if DEBUG
        dlc.UpdateProgress();
#endif

        await AsyncUtilities.ForTimeSlice(0, header.planks, PostInitializePlank, ConfigVars.timeSliceMs);

        // possibly improve repeatability?
        ctx.boardGun.Thingy();

        if (Prefs.freezeWhileLoading)
        {
#if DEBUG
            using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V6 Unfreeze post-init");
#endif
            await AsyncUtilities.ForEachTimeSlice(ctx.frozenDuringLoad, kvp => InitializeFinishedUnfreeze(kvp.Key, kvp.Value), ConfigVars.timeSliceMs);
        }

#if DEBUG
        dlc.UpdateProgress();
#endif

        executePostInit?.InvokeSafeSync();
        executePostInit = null;

#if DEBUG
        dlc.Success();
#endif
    }

    public async Task SetFilePath(string filePath)
    {
        if (!header.IsEmpty)
        {
            this.filePath = filePath;
            return;
        }

        using FileStream fs = File.OpenRead(filePath);
        // 5SSBL length
        fs.Seek(5, SeekOrigin.Begin);
        header = new();
        await header.Read(fs);
        ctx.mapBarcodeBytes = new byte[header.mapBarcodeLen];
        await fs.ReadAsync(ctx.mapBarcodeBytes, 0, ctx.mapBarcodeBytes.Length);
        ctx.usernameBytes = new byte[header.usernameLen];
        await fs.ReadAsync(ctx.usernameBytes, 0, ctx.usernameBytes.Length);
        this.filePath = filePath;
    }

    public void PopulateBoneMenu(Page parentCategory)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        SaveChecks.ThrowIfDefault(header);
        SaveChecks.ThrowIfDefault(ctx.mapBarcodeBytes);
        SaveChecks.ThrowIfDefault(ctx.usernameBytes);
#endif
        Color mint = Color.Lerp(Color.white, Color.green, 0.5f);
        string barcode = StringEncoding.GetString(ctx.mapBarcodeBytes);
        bool barcodeMismatch = barcode != SceneStreamer.Session.Level.Barcode.ID;
        if (barcodeMismatch && Prefs.filterByLevel) return;

        string name = Path.GetFileNameWithoutExtension(filePath);

        myPage = parentCategory.CreatePage(name, barcodeMismatch ? Color.yellow : Color.white);

        //#if DEBUG
        //        myPage.CreateBool("Debug colors", Color.white, false, val => dbgColors = val);
        //#endif

        myPage.CreateFunction("Preview", Color.white, Preview);
        if (header.planks > 0)
            myPage.CreateBool("Skip loading planks", Color.white, false, val => ctx.ignorePlanks = val);
        myPage.CreateFunction("Load save", Color.white, () => Load(Vector3.zero));
        myPage.CreateFunction("Load as dupe", Color.white, () => Saves.OpenDupeMenu(this, name, Load));
        //if (header.hasSerializedTransforms)
        //    myPage.CreateFunction("Load as Quicksave", Color.white, LoadAsQuicksave);
        if (Utilities.IsPlatformQuest())
        {
            myPage.CreateFunction("Rename", Color.white, () => AsyncUtilities.WrapNoThrow(BonemenuRename).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        }
        else
        {
            var renamePage = myPage.CreatePage("Rename", Color.white);
            renamePage.CreateFunction("Rename (SSBL Bonemenu)", Color.white, () => AsyncUtilities.WrapNoThrow(BonemenuRename).RunOnFinish(SceneSaverBL.ErrIfNotNull));
            renamePage.CreateFunction("Rename (On spectator screen)", Color.white, () => AsyncUtilities.WrapNoThrow(ImguiRename).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
            myPage.CreateFunction("No internet", Color.gray, SaveUtils.NothingAction);
        else if (Path.GetDirectoryName(filePath) == SceneSaverBL.saveDir)
            myPage.CreateFunction("Share via Repo", mint, () => AsyncUtilities.WrapNoThrow(RepoConsumer.UploadSave, filePath).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        myPage.CreateFunction("Delete", Color.red, () => SaveUtils.DeleteSave(filePath));
        myPage.CreateFunction("View all information", Color.gray, OpenHeaderCategory);

        //// this effectively places mismatching files at the end of the list
        //// and if EnumerateFiles orders by date then this should place recent files at the top
        //if (!barcodeMismatch)
        //{
        //    parentCategory.Elements.Remove(myPage);
        //    parentCategory.Elements.Insert(0, myPage);
        //}

    }

    private void OpenHeaderCategory()
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        if (headerCategory is null)
        {
            headerCategory = new($"Details: {name}", Color.white);
            headerCategory.Parent = myPage;
        }
        else
        {
            headerCategory.Name = $"Details: {name}";
        }

        headerCategory.RemoveAll();
        PopulateHeaderCategory();

        Menu.OpenPage(headerCategory);
    }

    public (Bounds display, Bounds elementBounds) GetBoundsForDupeAndDisplay()
    {
        Vector3 center = header.previewData.centerBottom;
        center.y += header.previewData.size.y / 2;
        Bounds displayBounds = new(center, header.previewData.size);
        return (displayBounds, header.previewData.GetBoundsOfPoolees());
    }

    public async Task Test()
    {
        using var ms = new MemoryStream();
        await Write(ms);
    }

    public bool ExistsOnDisk()
    {
        return File.Exists(filePath);
    }

    public override string ToString()
    {
        return $"SaveFile6: {filePath}";
    }

    #region Construction

    static async Task<List<Transform>[]> GetTransformsToBeSaved(Poolee[] poolees)
    {
        List<Transform>[] result = new List<Transform>[poolees.Length];
        // i got bored with saving state to an object. im just gonna have the compiler do it for me by allocating even more garbage in the form of lambda closures (JOY!)
        
        await AsyncUtilities.ForTimeSlice(0, poolees.Length, idx => GetTransformsToBeSavedImpl(poolees, result, idx));

        return result;
    }

    static async Task GetTransformsToBeSavedImpl(Poolee[] poolees, List<Transform>[] resultList, int idx)
    {
        Poolee poolee = poolees[idx];
        List<Transform> section;
        bool dontWalkHierarchy = Prefs.saveChecks && !SaveChecks.IsHierarchyConsistent(poolee);

        if (dontWalkHierarchy)
        {
            SceneSaverBL.Warn($"The hierarchy for the spawnable '{poolee.name}' from the crate {poolee.SpawnableCrate.Barcode.ID} has an inconsistent hierarchy!!! (It changes its hierarchy after being spawned!)");
            SceneSaverBL.Warn($"This means it will not be loaded properly in SSBL saves. If you want it to be saved, tell the mod creator ({poolee.SpawnableCrate.Pallet.Author}) that it cannot be saved because of this!");
            section = new List<Transform> { poolee.transform };
        }
        else
        {
            if (ConfigVars.fullsaveOverTime) 
                section = await SaveUtils.WalkHierarchyAsync(poolee.transform);
            else
                section = SaveUtils.WalkHierarchy(poolee.transform);
        }

        resultList[idx] = section;
    }

    static async Task<ObjectDestructible[]> GetPlanksToBeSaved(Poolee[] allPoolees)
    {
        List<ObjectDestructible> objs = new();
        await AsyncUtilities.ForEachTimeSlice(allPoolees, poolee =>
        {
            if (!SaveUtils.IsNewPlank(poolee.SpawnableCrate.Barcode.ID).HasValue)
                return;

            // objectdestructible component is placed on targetTransform object
            ObjectDestructible objDest = Instances<ObjectDestructible>.Get(poolee.transform);

            if (objDest)
                objs.Add(objDest);
#if DEBUG
            else
                SceneSaverBL.Warn("Plank was found without objectdestructible component! " + poolee.name);
#endif

        }, ConfigVars.timeSliceMs);

        return objs.ToArray();
    }

    //static ObjectDestructible GetPlanksToBeSavedImpl(Poolee checkPooleeFor)
    //{
        
    //}

    void ConstructPoolee(int idx)
    {
        Poolee poolee = ctx.poolees[idx];
        if (poolee == null) return; // ignore collected objects

        PooleeInitializationContext6 pooleeCtx = new(ctx.strings, Vector3.zero);
        poolees[idx].Construct(poolee, pooleeCtx);

        Bounds b = poolee.SpawnableCrate.ColliderBounds;
        b.center = poolee.transform.position + new Vector3(0, b.size.y / 2, 0);
        header.previewData.pooleeBoundingBoxes[idx] = b;
        // use blocking call/conversion because it should already be cached, and if its not, the spike should only happen once as its re-cached
        SelectionParticles.SetMaterial(poolee.GetInstanceID(), Assets.Materials.SavingObjectCompletedMaterial);
    }

    void ConstructConstraint(int idx)
    {
        ConstraintTracker ctr = ctx.allTrackers[idx];
        if (ctr == null) return; // ignore collected objects

        bool firstHasPoolee = SceneSaverBL.GetPooleeUpwards(ctr.attachPoint.transform);
        bool secondHasPoolee = SceneSaverBL.GetPooleeUpwards(ctr.otherTracker.attachPoint.transform);
        bool pooleeAttachedToStatic = firstHasPoolee != secondHasPoolee;

        // ignore trackers that arent attached to any of our poolees
        if (!(firstHasPoolee || secondHasPoolee)) return;

        // cannot save object constrained to non-static object without weld
        if (pooleeAttachedToStatic && SpawnerStates.Constraint.GetModeWhenSpawned(ctr) != Constrainer.ConstraintMode.Weld) return;

        try
        {
            constraints[idx].Construct(ctx.poolees, ctr);
        }
        catch (Exception ex)
        {
#if DEBUG
            SceneSaverBL.Warn(ex);
#endif
        }
    }

    void ConstructTransform(int pooleeNum, int transformNum)
    {
        List<Transform> transformsForPoolee = ctx.transformsByPoolee[pooleeNum];
        SavedTransform6[] savedTransformsForPoolee = transforms[pooleeNum];
        Transform transformToSave = transformsForPoolee[transformNum];
        
        ref SavedTransform6 savedTransformForPoolee = ref savedTransformsForPoolee[transformNum];
        savedTransformForPoolee.Construct(transformToSave, new(ctx.poolees, default));
    }

    void ConstructTransforms(int pooleeNum)
    {
        for (int i = 0; i < transforms[pooleeNum].Length; i++)
        {
            // i mean hey, use em if you got em
            ConstructTransform(pooleeNum, i);
        }
    }

    void ConstructPlank(int plankNum)
    {
        PlankInitializationContext6 plankCtx = new()
        {
            poolees = ctx.poolees
        };
        planks[plankNum].Construct(ctx.planks[plankNum], plankCtx);
    }

    #endregion

    #region BoneMenu

    void Preview()
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(filePath);
#endif
        AsyncUtilities.WrapNoThrow(PreviewImpl).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    async Task PreviewImpl()
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V6 Preview");
#endif
        if (previewTexture == null) LoadPreview();

        GameObject polaroidPrefab = await Assets.Prefabs.Polaroid.GetAsync();
        GameObject boundsLinesPrefab = await Assets.Prefabs.FullsavePreviewBounds.GetAsync();
        GameObject boundsLinesInstance = GameObject.Instantiate(boundsLinesPrefab);
        boundsLinesInstance.transform.localScale = header.previewData.size;
        boundsLinesInstance.transform.position = Vector3.zero;
        boundsLinesInstance.SetActive(true);

        await UniTask.Yield();
        
        boundsLinesInstance.transform.position = header.previewData.centerBottom + ctx.worldspaceOffset;
        const float EXIST_TIME = 25;
        float tweenLen = Mathf.Pow(Vector3.Magnitude(header.previewData.size), 0.25f);
        Jevil.Waiting.CallDelayed.CallAction(() => TweenPreviewBoundsForEnd(boundsLinesInstance, tweenLen), EXIST_TIME - tweenLen);

        (Vector3 pos, Quaternion rot) = SaveUtils.GetIdealMenuPolaroidLocation();
        GameObject polaroidInstance = Screenshotting.SpawnPolaroidAt(polaroidPrefab, previewTexture, pos, rot);
        GameObject.Destroy(polaroidInstance, EXIST_TIME);
    }

    void TweenPreviewBoundsForEnd(GameObject bounds, float originalTweenLen)
    {
#if DEBUG
        SceneSaverBL.Log("Tweening bounds scale to 0");
#endif
        bounds.transform.TweenLocalScale(Vector3.zero, originalTweenLen * 2)
                        .UseCustomInterpolator(inVal => Mathf.Pow(inVal, 4f))
                        .RunOnFinish(bounds.Destroy); 
                        //.RunOnFinish(() => GameObject.Instantiate(Assets.Prefabs.ObjectBoundsDestroyed))
    }

    [MemberNotNull(nameof(previewBytes), nameof(previewTexture))]
    void LoadPreview()
    {
        using FileStream fs = File.OpenRead(filePath);
        previewBytes = new byte[header.previewLen];

        fs.Position = header.dataStartStreamPos + header.usernameLen + header.mapBarcodeLen;
#if DEBUG
        SceneSaverBL.Log("Think preview starts at " + fs.Position);
#endif
        fs.Read(previewBytes, 0, previewBytes.Length);

        previewTexture = new Texture2D(2, 2);
        ImageConversion.LoadImage(previewTexture, previewBytes);
    }

    void LoadAsQuicksave() 
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(filePath);
        if (!header.hasSerializedTransforms)
            throw new Exception("Quicksave is being 'loaded as quicksave'. This option should only be presented on fullsaves!");
#endif
        var previousHasSerializedTransforms = header.hasSerializedTransforms;
        header.hasSerializedTransforms = false;
        AsyncUtilities.WrapNoThrow(LoadAsync).RunOnFinish(SceneSaverBL.ErrIfNotNull);
        executePostInit += () => header.hasSerializedTransforms = previousHasSerializedTransforms;
    }

    async Task BonemenuRename()
    {
        string filename = await BonemenuStringInput.GetFileNameInput();
        RenameFileTo(filename);
    }

    async Task ImguiRename()
    {
        string newName = await IMGUIInputField.GetStringAsync(45, Path.GetFileName(filePath));

        SceneSaverBL.Log("IMGUI rename complete. Inputted string: " + newName);
        RenameFileTo(newName);
    }

    //todo: test
    void RenameFileTo(string newName)
    {
        string dir = Path.GetDirectoryName(filePath)!;

        newName = Path.Combine(dir, newName);
        if (newName.EndsWith("." + SaveUtils.FILE_EXTENSION))
        {
            File.Move(filePath, newName);
            filePath = newName;
        }
        else
        {
            string newPath = Path.ChangeExtension(newName, SaveUtils.FILE_EXTENSION);
            File.Move(filePath, newPath);
            filePath = newPath;
        }

        myPage.Name = Path.GetFileNameWithoutExtension(newName);
        Menu.OpenPage(myPage);
    }

    void Load(Vector3? offset)
    {
        if (offset.HasValue)
        {
            ctx.worldspaceOffset = offset.Value;
            executePostInit += () => FingerOffset.Fadeout();
        }
        else ctx.worldspaceOffset = Vector3.zero;

#if DEBUG
        SceneSaverBL.Log($"Loading save with offset {ctx.worldspaceOffset}");
        SaveChecks.ThrowIfDefault(filePath);
#endif

        AsyncUtilities.WrapNoThrow(LoadAsync).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    async Task LoadAsync()
    {
        if (readCompleted)
        {
            SceneSaverBL.needInitialize.Enqueue(this);
            return;
        }

        using FileStream fs = File.OpenRead(filePath);
        fs.Position = header.dataStartStreamPos + header.mapBarcodeLen + header.previewLen;
#if DEBUG
        Utilities.InspectInUnityExplorer(this);
#endif
        Exception ex = await AsyncUtilities.WrapNoThrow(Read, fs);
        if (ex != null)
        {
            // we dont want to init if loading failed
            SceneSaverBL.Error(ex);
            return;
        }

#if DEBUG
        SceneSaverBL.Log($"Read successfully: {poolees.Length} serialized poolees, {constraints.Length} serialized constraints");
#endif

        SceneSaverBL.needInitialize.Enqueue(this);
    }

    void PopulateHeaderCategory()
    {
        string countsStr = $"<b>{header.poolees}</b> poolees, <b>{header.constraints}</b> constraints, <b>{header.planks}</b> planks\n" +
                            (header.hasSerializedTransforms ?
                                $"{header.serializedTransformCounts.Sum(ush => (int)ush)} child transform(s)\n" :
                                "No saved child transforms (quicksave)\n") +
                            "<b></b>";
        headerCategory.CreateFunction("View counts", Color.white, () => Menu.DisplayDialog($"Object Counts", countsStr));


        string infoStr = $"Header size: {header.dataStartStreamPos - 1}B, Preview size: {Math.Round(header.previewLen / 1024.0, 2)}KB\n" +
                         $"Author <i>({header.usernameLen}B)</i>: {StringEncoding.GetString(ctx.usernameBytes)}\n" +
                         $"Center bottom pos (meters): {header.previewData.centerBottom}\n" +
                         $"Size (meters): {header.previewData.size}\n" +
                         $"Map barcode <i>({header.mapBarcodeLen}B)</i>:\n{StringEncoding.GetString(ctx.mapBarcodeBytes)}\n";

        headerCategory.CreateFunction("View extra info", Color.white, () => Menu.DisplayDialog($"Extra Info", infoStr));

        headerCategory.CreateFunction($"Save preview to file", Color.white, () => AsyncUtilities.WrapNoThrow(SavePreviewToFile).RunOnFinish(SceneSaverBL.ErrIfNotNull));

//#if DEBUG
//        headerCategory.CreateFloat("X", Color.red, 0, 0.5f, -100, 100, val => ctx.worldspaceOffset.x = val);
//        headerCategory.CreateFloat("Y", Color.green, 0, 0.5f, -100, 100, val => ctx.worldspaceOffset.y = val);
//        headerCategory.CreateFloat("Z", Color.blue, 0, 0.5f, -100, 100, val => ctx.worldspaceOffset.z = val);
//#endif
    }

    async Task SavePreviewToFile()
    {
        string filename = Path.GetFileNameWithoutExtension(filePath);
        string path = Path.Combine(SaveUtils.PreviewDir, filename + ".png");
        if (previewBytes is null || previewBytes.Length == 0)
        {
#if DEBUG
            SceneSaverBL.Warn("Preview not loaded. Reading now from " + filePath);
#endif
            LoadPreview();
            await UniTask.Yield();
#if DEBUG
            SceneSaverBL.Log("Read completed. Now saving preview to " + path);
#endif
        }

        File.WriteAllBytes(path, previewBytes);

        

        var notif = new Notification()
        {
            Type = NotificationType.Success,
            Message = $"Saved! Check {SaveUtils.PreviewDir.Replace('\\', '/').Replace(MelonEnvironment.MelonBaseDirectory.Replace('\\', '/'), "")}"
        };
        Notifier.Send(notif);

        if (!Utilities.IsPlatformQuest())
        {
            headerCategory.CreateFunction("Open preview folder", Color.gray, () => Process.Start("explorer.exe", "/select, \"" + path + "\""));
        }
    }

    #endregion

    #region Read/Deserialize

    void PrepareArrays()
    {
        poolees = new SavedPoolee6[header.poolees];
        constraints = new SavedConstraint6[header.constraints];
        planks = new SavedPlank6[header.planks];
        if (header.hasSerializedTransforms)
        {
            // could probably be easily done via linq, but linq creates garbo so i dont wanna
            transforms = new SavedTransform6[header.serializedTransformCounts.Length][];
            for (int i = 0; i < transforms.Length; i++)
                transforms[i] = new SavedTransform6[header.serializedTransformCounts[i]];
        }
        else transforms = Array.Empty<SavedTransform6[]>();

        ctx.poolees = new Poolee[header.poolees];
        ctx.pooleeTasks = new Task<Poolee>[header.poolees];
        ctx.allTrackers = new ConstraintTracker[header.constraints];
        ctx.planks = new ObjectDestructible[header.planks];
        ctx.frozenDuringLoad = new(header.poolees, UnityObjectComparer<Rigidbody>.Instance);
    }
    
    // moved down here because structs use blocking calls because async methods on structs cannot modify their "this"
    void ReadImpl(Stream stream)
    {
        for (int i = 0; i < poolees.Length; i++)
            poolees[i].Read(stream);

        for (int i = 0; i < constraints.Length; i++) 
            constraints[i].Read(stream);

        for (int i = 0; i < planks.Length; i++)
            planks[i].Read(stream);

        for (int i = 0; i < transforms.Length; i++) 
        {
            SavedTransform6[] currArr = transforms[i];
            for (int j = 0; j < currArr.Length; j++)
            {
                try
                {
                    currArr[j].Read(stream);
                }
                catch(Exception ex)
                {
                    SceneSaverBL.Log($"Exception while reading transform ({i}, {j}) (poolee, tIdx) : {ex}");
                }
            }
        }

        readCompleted = true;
    }

    #endregion

    #region Initialization

    async void InitializePoolee(int idx)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
#endif
        try
        {
            PooleeInitializationContext6 pooleeCtx = new()
            {
                barcodes = ctx.strings,
                offset = ctx.worldspaceOffset,
            };

            string pooleeBarcode = poolees[idx].GetBarcodeStr(ctx.strings);
            bool isPlank = SaveUtils.IsNewPlank(pooleeBarcode).HasValue;
            Task<Poolee> pooleeTask;

            if (isPlank && !ctx.ignorePlanks)
            {
                pooleeTask = InitializePlankInsteadOfPoolee(pooleeCtx, idx);
            }
            else
            {
#if DEBUG
                SceneSaverBL.Log($"Poolee (idx {idx}) has barcode {pooleeBarcode}");
#endif
                pooleeTask = poolees[idx].Initialize(pooleeCtx);
            }

            ctx.pooleeTasks[idx] = pooleeTask;
            ctx.poolees[idx] = await pooleeTask;
#if DEBUG
            if (ctx.poolees[idx] == null)
                SceneSaverBL.Warn($"Poolee {idx} is null after initialization! Why!!!! waaaah!");
#endif


            if (!Prefs.freezeWhileLoading || isPlank) // dont freeze planks, it causes them to break when theyre unfrozen
                return;

            foreach (Rigidbody rb in ctx.poolees[idx].GetComponentsInChildren<Rigidbody>())
            {
                Instances<Rigidbody>.AddManual(rb.gameObject, rb);
                ctx.frozenDuringLoad[rb] = rb.isKinematic;
                rb.isKinematic = true;
            }
        }
        catch (Exception e)
        {
            SceneSaverBL.Error(e);
        }
    }

    async Task<Poolee> InitializePlankInsteadOfPoolee(PooleeInitializationContext6 pooleeCtx, int pooleeIdx)
    {
        int plankIdx = ctx.nextPlankIdx++;

#if DEBUG
        SceneSaverBL.Log($"SaveFile6 Initialize: Now initializing plank @ plankIdx {plankIdx} (and pooleeIdx {pooleeIdx}) (from array of {planks.Length})");
#endif

        PlankInitializationContext6 plankCtx = new()
        {
            boardGun = ctx.boardGun,
            poolees = ctx.poolees,
            worldspaceOffset = ctx.worldspaceOffset,
        };
        SavedPlank6 plank = planks[plankIdx];
        (int poolee1, int poolee2) = plank.DependsOnPoolees;

        // this is kinda really a dumb ass hack, it just prevents a plank from depending on itself.
        if (poolee1 == pooleeIdx)
            poolee1 = SavedHierarchyLocation6.NO_POOLEE;
        if (poolee2 == pooleeIdx)
            poolee2 = SavedHierarchyLocation6.NO_POOLEE;

#if DEBUG
        if (Math.Max(poolee1 == SavedHierarchyLocation6.NO_POOLEE ? 0 : poolee1, poolee2 == SavedHierarchyLocation6.NO_POOLEE ? 0 : poolee2) > pooleeIdx)
                throw new IndexOutOfRangeException($"!!! Plank relies on indices {poolee1} and {poolee2}, but is poolee index {pooleeIdx}!!! This should not happen and is not allowed!");
        SceneSaverBL.Log($"V6 PlankInit: Plank {plankIdx} (Poolee {pooleeIdx}) is waiting on poolees {poolee1} & {poolee2}");
#endif

        //Task poolee1Task = poolee1 == SavedHierarchyLocation6.NO_POOLEE ? Task.CompletedTask : ctx.pooleeTasks[poolee1];
        //Task poolee2Task = poolee2 == SavedHierarchyLocation6.NO_POOLEE ? Task.CompletedTask : ctx.pooleeTasks[poolee2];

        //if (!poolee1Task.IsCompleted)
        //    await poolee1Task;
        //if (!poolee2Task.IsCompleted)
        //    await poolee2Task;

#if DEBUG
        SceneSaverBL.Log($"V6 PlankInit: Plank {plankIdx} (Poolee {pooleeIdx}) has stopped waiting for poolees {poolee1} & {poolee2}");
#endif

        ObjectDestructible objDest = await plank.Initialize(plankCtx);
        ctx.planks[plankIdx] = objDest;
        Poolee ap = Instances<Poolee>.Get(objDest.transform);
        
        return ap;
    }

    void InitializeConstraint(int idx)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        SceneSaverBL.Log($"Initializing constraint {idx}");
#endif
        constraints[idx].Initialize(ctx.poolees, ctx.constrainer);
    }

    void InitializeTransformSingle(int pooleeNum, int transformNum)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
#endif
        SavedTransform6[] savedTransformArr = transforms[pooleeNum];
        List<Transform> pooleeTransforms = ctx.transformsByPoolee[pooleeNum];
        TransformInitializationContext6 tic = new(ctx.poolees, ctx.worldspaceOffset);

        // realistically? not needed. iirc tic init is synchronous anyway.
        _ = savedTransformArr[transformNum].Initialize(tic);
    }

    void InitializeTransformsMultiple(int idx)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V6 Transform Init (Mult) - idx={idx} name='{poolees[idx]}'");
#endif
        int subtransformCountRuntime = ctx.transformsByPoolee[idx].Count;
        int subtransformCountFile = header.serializedTransformCounts[idx];
#if DEBUG
        SceneSaverBL.Log($"Expected {subtransformCountFile}, got {subtransformCountRuntime} from save file {filePath}");
#endif
        if (subtransformCountRuntime != subtransformCountFile) return;

        for (int j = 0; j < subtransformCountRuntime; j++)
        {
            InitializeTransformSingle(idx, j);
        }
    }

    void PostInitializePlank(int idx)
    {
#if DEBUG
        SceneSaverBL.Log($"Post-initializing plank {idx} (of {planks.Length})");
#endif
        PlankInitializationContext6 plankCtx = new()
        {
            boardGun = ctx.boardGun,
            poolees = ctx.poolees,
            worldspaceOffset = ctx.worldspaceOffset,
        };

        if (ctx.planks[idx] == null)
            return; // ignore objects that didnt spawn

        // everything down the chain in postinitialize is synchronous. making this method async would just introduce instability. keep that shit outttt.
        _ = planks[idx].PostInitialize(ctx.planks[idx], plankCtx);
    }

    async Task TraverseHierarchies()
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V6 Async hierarchy traversal");
#endif

        ctx.transformsByPoolee = new List<Transform>[header.poolees];

        if (ConfigVars.fullsaveOverTime)
        {
            for (int i = 0; i < header.poolees; i++)
            {
//#if DEBUG
//                if (ctx.poolees[i] == null)
//                {
//                    SceneSaverBL.Warn($"Expect an error! Poolee @ idx {i} didn't initialize in time! It's supposed to be a spawnable with the barcode {poolees[i].GetBarcodeStr(ctx.strings)}");
//                }
//#endif

                if (ctx.poolees[i] == null) // yaay null handling
                    ctx.transformsByPoolee[i] = new List<Transform>();
                else
                    ctx.transformsByPoolee[i] = await SaveUtils.WalkHierarchyAsync(ctx.poolees[i].transform);
                //var oneOfRes = await AsyncUtilities.WrapNoThrowWithResult(SaveUtils.WalkHierarchyAsync, ctx.poolees[i].transform);
                //if (oneOfRes.HasResult) ctx.transformsByPoolee[i] = oneOfRes.Result;
                //else ctx.transformsByPoolee[i] = new List<Transform>(1) { ctx.poolees[i].transform };
            }
        }
        else
        {
            for (int i = 0; i < header.poolees; i++)
                ctx.transformsByPoolee[i] = SaveUtils.WalkHierarchy(ctx.poolees[i].transform);
        }
        SaveChecks.ThrowIfOffMainThread();
    }

    private async Task InitializeTransforms()
    {
        await TraverseHierarchies();

        if (ConfigVars.fullsaveOverTime)
        {
#if DEBUG
            using var ps1 = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V6 Transform Init - ALL");
#endif
            for (int i = 0; i < header.poolees; i++)
            {
#if DEBUG
                using var ps2 = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V6 Transform Init - idx={i} name='{poolees[i]}'");
#endif
                int subtransformCountRuntime = ctx.transformsByPoolee[i].Count;
                int subtransformCountFile = header.serializedTransformCounts[i];
#if DEBUG
                SceneSaverBL.Log($"Expected {subtransformCountFile} subtransforms, got {subtransformCountRuntime} from runtime object");
#endif
                if (subtransformCountRuntime != subtransformCountFile) continue;

                await AsyncUtilities.ForTimeSlice(0, subtransformCountFile, j => InitializeTransformSingle(i, j), ConfigVars.timeSliceMs);
            }
        }
        else await AsyncUtilities.ForTimeSlice(0, header.poolees, InitializeTransformsMultiple, ConfigVars.timeSliceMs);
    }

    void InitializeFinishedUnfreeze(Rigidbody rb, bool preFreeze)
    {
        if (rb == null) // this shouldnt happen but it *has* happened, for whatever reason
            return;
        rb.isKinematic = preFreeze;
    }

    #endregion
}
