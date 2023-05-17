using BoneLib.BoneMenu;
using BoneLib.BoneMenu.Elements;
using Cysharp.Threading.Tasks;
using Jevil;
using Jevil.Tweening;
using PuppetMasta;
using SceneSaverBL.Interfaces;
using SLZ.AI;
using SLZ.Marrow.Pool;
using SLZ.Marrow.SceneStreaming;
using SLZ.Props;
using SLZ.VRMK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Versions.Version5;

public class SaveFile5 : ISaveFile
{
#if DEBUG
    static Thread mainThread;
#endif

    private Header5 header;

    SaveContext5 ctx;

    string filePath;
    bool readCompleted;
    Texture2D previewTexture;
    byte[] previewBytes;
    SavedConstraint5[] constraints;
    SavedPoolee5[] poolees;
    SavedTransform5[][] transforms;
    Action executePostInit;

    public byte Version => 5;

    public async Task Construct(AssetPoolee[] savingPoolees, ConstraintTracker[] allConstraints)
    {
#if DEBUG
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V5 Construct");
#endif
        allConstraints = allConstraints.Where(ct => ct.isHost).ToArray();
        List<Transform>[] savingTransforms = null;
        byte[] levelBarcodeBytes = Encoding.UTF8.GetBytes(SceneStreamer.Session.Level.Barcode.ID);
        byte[] usernameBytes = Encoding.UTF8.GetBytes(SceneSaverBL.username ?? "Unknown");
        Bounds szBounds = SelectionZone.Instance.Bounds;
        header.size = szBounds.size;
        header.centerBottom = new Vector3(szBounds.center.x, szBounds.min.y, szBounds.center.z);
        header.poolees = savingPoolees.Length;
        header.hasSerializedTransforms = SceneSaverBL.isFullSave;
        header.barcodeLen = (ushort)levelBarcodeBytes.Length;
        header.usernameLen = (byte)usernameBytes.Length;

        // cache obj completed material
        await Assets.Materials.SavingObjectCompletedMaterial.GetAsync();
        GameObject cameraFlash = await Assets.Prefabs.CameraFlash.GetAsync();
        GameObject polaroid = await Assets.Prefabs.Polaroid.GetAsync();
        Camera cam = SelectionZone.Instance.CreateCamera();

        constraints = new SavedConstraint5[allConstraints.Length];
        poolees = new SavedPoolee5[savingPoolees.Length];

        if (header.hasSerializedTransforms)
        {
            savingTransforms = await GetTransformsToBeSaved(savingPoolees);
            transforms = new SavedTransform5[savingTransforms.Length][];
            header.serializedTransformCounts = new ushort[transforms.Length];

            for (int i = 0; i < savingTransforms.Length; i++)
            {
                // initialize arrays in 2d array
                transforms[i] = new SavedTransform5[savingTransforms[i].Count];
                header.serializedTransformCounts[i] = (ushort)savingTransforms[i].Count;
            }
        }
        else
        {
            header.serializedTransformCounts = Array.Empty<ushort>();
            transforms = Array.Empty<SavedTransform5[]>();
        }

        ctx = new()
        {
            poolees = savingPoolees,
            allTrackers = allConstraints,
            transformsByPoolee = savingTransforms,
            barcodeBytes = levelBarcodeBytes,
            usernameBytes = usernameBytes,
        };

        await AsyncUtilities.ForTimeSlice(0, poolees.Length, ConstructPoolee);

        await AsyncUtilities.ForTimeSlice(0, constraints.Length, ConstructConstraint);

        if (header.hasSerializedTransforms)
        {
            if (Prefs.fullsaveOverTime)
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
        previewBytes = ImageConversion.EncodeToJPG(previewTexture);

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
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "V5 Write");
#endif

        // configureawait because none of this needs to be kept on the main thread lol.
        await header.Write(stream).ConfigureAwait(false);

        await stream.WriteAsync(ctx.barcodeBytes, 0, ctx.barcodeBytes.Length).ConfigureAwait(false);
        
        await stream.WriteAsync(ctx.usernameBytes, 0, ctx.usernameBytes.Length).ConfigureAwait(false);

        await stream.WriteAsync(previewBytes, 0, previewBytes.Length).ConfigureAwait(false);

        for (int i = 0; i < poolees.Length; i++)
            await poolees[i].Write(stream).ConfigureAwait(false);

        for (int i = 0; i < constraints.Length; i++)
            await constraints[i].Write(stream).ConfigureAwait(false);

        for (int i = 0; i < transforms.Length; i++)
        {
            SavedTransform5[] currArr = transforms[i];
            for (int j = 0; j < currArr.Length; j++)
            {
                await currArr[j].Write(stream).ConfigureAwait(false);
            }
        }

        SceneSaverBL.runOnMainThread.Enqueue(async () => await Stats.SaveCreated(header.hasSerializedTransforms));
    }

    public async Task Read(Stream stream)
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(header);

        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.STOPWATCH_EXECUTION_TIME, "V5 Read");
#endif

        stream.Position = header.DataReadPos;

        PrepareArrays();

        // just to get off the main thread and not halt frametimes
        await Task.Run(() => ReadImpl(stream));
    }

    public async Task Initialize()
    {
        await Stats.SaveLoaded(header.hasSerializedTransforms);
#if DEBUG
        mainThread = Thread.CurrentThread;
#endif
        ctx.constrainer = await SaveUtils.GetDummyConstrainer();
        SceneSaverBL.Log("Retrieved dummy constrainer " + ctx.constrainer.name);

        await AsyncUtilities.ForTimeSlice(0, header.poolees, InitializePoolee, Prefs.timeSliceMs);

        // wait for the game to initialize all poolees
        // dont use UniTask.WhenAll because im a lazy fucker who doesnt want to deal with seeing which IL2CPP array i need to use for it to work properly
#if DEBUG
        Stopwatch sw = Stopwatch.StartNew();
#endif
        while (ctx.pooleeTasks.Any(t => !t.IsCompleted))
            await UniTask.Yield();
#if DEBUG
        SceneSaverBL.Log($"Waited an extra {sw.ElapsedMilliseconds} ms for poolees to finish initializing");
        SaveChecks.ThrowIfOffMainThread();
#endif

        if (header.hasSerializedTransforms)
        {
            await InitializeTransforms();
        }

        //todo: make constraints initialize immediately after their poolee inits (array is sorted by DependentOn, see SaveUtils.CleanTrackers)

        await AsyncUtilities.ForTimeSlice(0, header.constraints, InitializeConstraint, Prefs.timeSliceMs);

        if (Prefs.freezeWhileLoading)
        {
#if DEBUG
            using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V5 Unfreeze post-init");
#endif
            await AsyncUtilities.ForEachTimeSlice(ctx.frozenDuringLoad, kvp => InitializeFinishedUnfreeze(kvp.Key, kvp.Value), Prefs.timeSliceMs);
        }
    }

    public async Task SetFilePath(string filePath)
    {
        if (header != default)
        {
            this.filePath = filePath;
            return;
        }

        using FileStream fs = File.OpenRead(filePath);
        // 5SSBL length
        fs.Seek(5, SeekOrigin.Begin);
        header = await Header5.ReadFrom(fs);
        ctx.barcodeBytes = new byte[header.barcodeLen];
        await fs.ReadAsync(ctx.barcodeBytes, 0, ctx.barcodeBytes.Length);
        ctx.usernameBytes = new byte[header.usernameLen];
        await fs.ReadAsync(ctx.usernameBytes, 0, ctx.usernameBytes.Length);
        this.filePath = filePath;
    }

    public void PopulateBoneMenu(MenuCategory category)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        SaveChecks.ThrowIfDefault(header);
        SaveChecks.ThrowIfDefault(ctx.barcodeBytes);
        SaveChecks.ThrowIfDefault(ctx.usernameBytes);
#endif
        string barcode = Encoding.UTF8.GetString(ctx.barcodeBytes);
        bool barcodeMismatch = barcode != SceneStreamer.Session.Level.Barcode.ID;
        if (barcodeMismatch && Prefs.filterByLevel) return;

        string name = Path.GetFileNameWithoutExtension(filePath);
        MenuCategory headerCategory = category.CreateCategory($"Internal details: {name}", Color.white);
        category.Elements.Remove(headerCategory);

        SubPanelElement spe = category.CreateSubPanel(name, barcodeMismatch ? Color.yellow : Color.white);
        spe.CreateFunctionElement($"{header.poolees} obj(s)", Color.gray, SaveUtils.NothingAction);
        spe.CreateFunctionElement(header.hasSerializedTransforms ? "Full save" : "Quick save", Color.gray, SaveUtils.NothingAction);
        spe.CreateFunctionElement("Preview", Color.white, Preview);
        spe.CreateFunctionElement("Load", Color.white, Load);
        if (header.hasSerializedTransforms)
            spe.CreateFunctionElement("Load as Quicksave", Color.white, LoadAsQuicksave);
        spe.CreateFunctionElement("Delete", Color.red, () => File.Delete(filePath));
        spe.CreateFunctionElement("View all information", Color.gray, () => MenuManager.SelectCategory(headerCategory));

        // this effectively places mismatching files at the end of the list
        // and if EnumerateFiles orders by date then this should place recent files at the top
        if (!barcodeMismatch)
        {
            category.Elements.Remove(spe);
            category.Elements.Insert(0, spe);
        }

        PopulateHeaderCategory(headerCategory);
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

    #region Construction

    static async Task<List<Transform>[]> GetTransformsToBeSaved(AssetPoolee[] poolees)
    {
        List<Transform>[] result = new List<Transform>[poolees.Length];
        // i got bored with saving state to an object. im just gonna have the compiler do it for me by allocating even more garbage in the form of lambda closures (JOY!)
        
        await AsyncUtilities.ForTimeSlice(0, poolees.Length, idx => GetTransformsToBeSavedImpl(poolees, result, idx));

        return result;
    }

    static async Task GetTransformsToBeSavedImpl(AssetPoolee[] poolees, List<Transform>[] resultList, int idx)
    {
        AssetPoolee poolee = poolees[idx];
        List<Transform> section;
        bool dontWalkHierarchy = Prefs.saveChecks && !SaveChecks.IsHierarchyConsistent(poolee);

        if (dontWalkHierarchy)
        {
            SceneSaverBL.Warn($"The hierarchy for the spawnable '{poolee.name}' from the crate {poolee.spawnableCrate.Barcode.ID} has an inconsistent hierarchy!!! (It changes its hierarchy after being spawned!)");
            SceneSaverBL.Warn($"This means it will not be loaded properly in SSBL saves. If you want it to be saved, tell the mod creator ({poolee.spawnableCrate.Pallet.Author}) that it cannot be saved because of this!");
            section = new List<Transform> { poolee.transform };
        }
        else
        {
            if (Prefs.fullsaveOverTime) 
                section = await SaveUtils.WalkHierarchyAsync(poolee.transform);
            else
                section = SaveUtils.WalkHierarchy(poolee.transform);
        }

        resultList[idx] = section;
    }

    void ConstructPoolee(int idx)
    {
        AssetPoolee poolee = ctx.poolees[idx];
        if (poolee.INOC()) return; // ignore collected objects

        poolees[idx].Construct(poolee);
        // use blocking call/conversion because it should already be cached, and if its not, the spike should only happen once as its re-cached
        SelectionParticles.SetMaterial(poolee.GetInstanceID(), Assets.Materials.SavingObjectCompletedMaterial);
    }

    void ConstructConstraint(int idx)
    {
        ConstraintTracker ctr = ctx.allTrackers[idx];
        if (ctr.INOC()) return; // ignore collected objects

        bool firstHasPoolee = SceneSaverBL.GetPooleeUpwards(ctr.attachPoint.transform);
        bool secondHasPoolee = SceneSaverBL.GetPooleeUpwards(ctr.otherTracker.attachPoint.transform);
        bool pooleeAttachedToStatic = firstHasPoolee != secondHasPoolee;

        // ignore trackers that arent attached to any of our poolees
        if (!(firstHasPoolee || secondHasPoolee)) return;

        // cannot save object constrained to non-static object without weld
        if (pooleeAttachedToStatic && ctr.mode != Constrainer.ConstraintMode.Weld) return;

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
        SavedTransform5[] savedTransformsForPoolee = transforms[pooleeNum];
        Transform transformToSave = transformsForPoolee[transformNum];
        
        ref SavedTransform5 savedTransformForPoolee = ref savedTransformsForPoolee[transformNum];
        savedTransformForPoolee.Construct(transformToSave);
    }

    void ConstructTransforms(int pooleeNum)
    {
        for (int i = 0; i < transforms[pooleeNum].Length; i++)
        {
            // i mean hey, use em if you got em
            ConstructTransform(pooleeNum, i);
        }
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
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V5 Preview");
#endif
        if (previewTexture.INOC()) LoadPreview();

        GameObject polaroidPrefab = await Assets.Prefabs.Polaroid.GetAsync();
        GameObject boundsLinesPrefab = await Assets.Prefabs.FullsavePreviewBounds.GetAsync();
        GameObject boundsLinesInstance = GameObject.Instantiate(boundsLinesPrefab);
        boundsLinesInstance.transform.localScale = header.size;
        boundsLinesInstance.transform.position = Vector3.zero;
        boundsLinesInstance.SetActive(true);

        await UniTask.Yield();
        
        boundsLinesInstance.transform.position = header.centerBottom;
        const float EXIST_TIME = 25;
        float tweenLen = Mathf.Pow(Vector3.Magnitude(header.size), 0.25f);
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

    void LoadPreview()
    {
        using FileStream fs = File.OpenRead(filePath);
        previewBytes = new byte[header.previewLen];

        fs.Position = header.dataStartStreamPos + header.usernameLen + header.barcodeLen;
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
        if (!header.hasSerializedTransforms) throw new Exception("Quicksave is being 'loaded as quicksave'. This option should only be presented on fullsaves!");
#endif
        header.hasSerializedTransforms = false;
        AsyncUtilities.WrapNoThrow(LoadAsync).RunOnFinish(SceneSaverBL.ErrIfNotNull);
        executePostInit += () => header.hasSerializedTransforms = true;
    }

    void Load()
    {
#if DEBUG
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
        fs.Position = header.dataStartStreamPos + header.barcodeLen + header.previewLen;
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

    void PopulateHeaderCategory(MenuCategory category)
    {
        SubPanelElement countSpe = category.CreateSubPanel("Counts", Color.white);
        countSpe.CreateFunctionElement($"{header.poolees} poolee(s)", Color.gray, SaveUtils.NothingAction);
        countSpe.CreateFunctionElement($"{header.constraints} constraint(s)", Color.gray, SaveUtils.NothingAction);
        countSpe.CreateFunctionElement(header.hasSerializedTransforms ? "Saved child transform(s) (fullsave)" : "No saved child transforms (quicksave)", Color.gray, SaveUtils.NothingAction);
        countSpe.CreateFunctionElement($"{header.serializedTransformCounts.Sum(ush => ush)} child transform(s)", Color.gray, SaveUtils.NothingAction);

        SubPanelElement dataSpe = category.CreateSubPanel("Data", Color.white);
        dataSpe.CreateFunctionElement($"Header size: {header.dataStartStreamPos - 1}B", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Preview size: {Math.Round(header.previewLen / 1024.0, 2)}KB", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Barcode size: {header.barcodeLen} bytes", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Barcode: {Encoding.UTF8.GetString(ctx.barcodeBytes)}", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Author name size: {header.usernameLen} bytes", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Author: {Encoding.UTF8.GetString(ctx.usernameBytes)}", Color.white, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Center bottom (meters): {header.centerBottom}", Color.gray, SaveUtils.NothingAction);
        dataSpe.CreateFunctionElement($"Size (meters): {header.size}", Color.gray, SaveUtils.NothingAction);
    }

    #endregion

    #region Read/Deserialize

    void PrepareArrays()
    {
        poolees = new SavedPoolee5[header.poolees];
        constraints = new SavedConstraint5[header.constraints];
        if (header.hasSerializedTransforms)
        {
            transforms = new SavedTransform5[header.serializedTransformCounts.Length][];
            for (int i = 0; i < transforms.Length; i++)
                transforms[i] = new SavedTransform5[header.serializedTransformCounts[i]];
        }
        else transforms = Array.Empty<SavedTransform5[]>();

        ctx.poolees = new AssetPoolee[header.poolees];
        ctx.pooleeTasks = new Task<AssetPoolee>[header.poolees];
        ctx.allTrackers = new ConstraintTracker[header.constraints];
        ctx.frozenDuringLoad = new(header.poolees, UnityObjectComparer<Rigidbody>.Instance);
    }
    
    // moved down here because structs use blocking calls because async methods on structs cannot modify their "this"
    void ReadImpl(Stream stream)
    {
        for (int i = 0; i < poolees.Length; i++)
            poolees[i].Read(stream);

        for (int i = 0; i < constraints.Length; i++) 
            constraints[i].Read(stream);

        for (int i = 0; i < transforms.Length; i++) 
        {
            SavedTransform5[] currArr = transforms[i];
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
            Task<AssetPoolee> task = poolees[idx].Initialize();
            ctx.pooleeTasks[idx] = task;
            ctx.poolees[idx] = await task;


            if (!Prefs.freezeWhileLoading)
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
        finally
        {
            executePostInit?.InvokeSafeSync();
            executePostInit = null;
        }
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
        SavedTransform5[] savedTransformArr = transforms[pooleeNum];
        List<Transform> pooleeTransforms = ctx.transformsByPoolee[pooleeNum];
        TransformInitializationContext5 tic5 = new()
        {
            transform = pooleeTransforms[transformNum],
        };

        savedTransformArr[transformNum].Initialize(tic5);
    }

    void InitializeTransformsMultiple(int idx)
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V5 Transform Init (Mult) - idx={idx} name='{poolees[idx]}'");
#endif
        int subtransformCountRuntime = ctx.transformsByPoolee[idx].Count;
        int subtransformCountFile = header.serializedTransformCounts[idx];
#if DEBUG
        SceneSaverBL.Log($"Expected {subtransformCountFile}, got {subtransformCountRuntime}");
#endif
        if (subtransformCountRuntime != subtransformCountFile) return;

        for (int j = 0; j < subtransformCountRuntime; j++)
        {
            InitializeTransformSingle(idx, j);
        }
    }

    async Task TraverseHierarchies()
    {
#if DEBUG
        SaveChecks.ThrowIfOffMainThread();
        using var ps = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V5 Async hierarchy traversal");
#endif

        ctx.transformsByPoolee = new List<Transform>[header.poolees];

        if (Prefs.fullsaveOverTime)
        {
            for (int i = 0; i < header.poolees; i++)
            {
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

        if (Prefs.fullsaveOverTime)
        {
#if DEBUG
            using var ps1 = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V5 Transform Init - ALL");
#endif
            for (int i = 0; i < header.poolees; i++)
            {
#if DEBUG
                using var ps2 = new ProfilingScope(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, $"V5 Transform Init - idx={i} name='{poolees[i]}'");
#endif
                int subtransformCountRuntime = ctx.transformsByPoolee[i].Count;
                int subtransformCountFile = header.serializedTransformCounts[i];
#if DEBUG
                SceneSaverBL.Log($"Expected {subtransformCountFile}, got {subtransformCountRuntime}");
#endif
                if (subtransformCountRuntime != subtransformCountFile) continue;

                await AsyncUtilities.ForTimeSlice(0, subtransformCountFile, j => InitializeTransformSingle(i, j), Prefs.timeSliceMs);
            }
        }
        else await AsyncUtilities.ForTimeSlice(0, header.poolees, InitializeTransformsMultiple, Prefs.timeSliceMs);
    }

    void InitializeFinishedUnfreeze(Rigidbody rb, bool preFreeze)
    {
        rb.isKinematic = preFreeze;
    }

    #endregion
}
