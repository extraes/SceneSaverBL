using Jevil.Prefs;
using System.Diagnostics;
using Jevil.Patching;
using SceneSaverBL.Interfaces;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;
using Il2CppSLZ.Bonelab;
using Il2CppOculus.Platform;
using Il2CppOculus.Platform.Models;
using Il2CppSLZ.Marrow.Utilities;
using MelonLoader.Utils;
using Il2CppSLZ.Marrow.SceneStreaming;

namespace SceneSaverBL;

public static class BuildInfo
{
    public const string Name = "SceneSaverBL"; // Name of the Mod.  (MUST BE SET)
    public const string Author = "extraes"; // Author of the Mod.  (Set as null if none)
    public const string Company = null; // Company that made the Mod.  (Set as null if none)
    public const string Version = "2.0.0"; // Version of the Mod.  (MUST BE SET)
    public const string DownloadLink = "https://bonelab.thunderstore.io/package/extraes/SceneSaverBL/"; // Download Link for the Mod.  (Set as null if none)
}

public class SceneSaverBL : MelonMod
{
    public SceneSaverBL() : base() => instance = this;
    private static PrefEntries prefEntries;

    internal static bool isFullSave;
    internal static volatile bool currentlySaving;
    internal static string username;
    internal static SceneSaverBL instance;
    internal static ConcurrentQueue<ISaveFile> needInitialize = new();
    internal static ConcurrentQueue<Action> runOnMainThread = new();
    internal static Vector3? desiredDupePos = null;
    private readonly static Stopwatch updateSw = new();
    public static int[] supportedSaveVers;

    internal static IMGUIInputField? currentInputter;

    internal static string saveDir = Path.Combine(MelonEnvironment.UserDataDirectory, "SceneSaver", "Saves");
    internal static string dupesDir = Path.Combine(MelonEnvironment.UserDataDirectory, "SceneSaver", "Dupes");

    public override void OnEarlyInitializeMelon() => SaveChecks.EstablishMainThread();

    public override async void OnInitializeMelon()
    {
#if DEBUG
        Log("This is a debug build of SSBL, intended for use with JeviLib V" + JevilBuildInfo.VERSION);

        Stopwatch stopwatch = Stopwatch.StartNew();
#endif 

        if (!JevilBuildInfo.RuntimeVersionGreaterThanOrEqual(JevilBuildInfo.VERSION))
        {
            Warn($"SceneSaver v{MelonAssembly.Assembly.GetName().Version} was built for JeviLib v{JevilBuildInfo.VERSION}!!! Update your dependencies!!!");
            return;
        }

        prefEntries = Preferences.Register(typeof(Prefs));
        Page aboutPage = prefEntries.BoneMenuPage["About"];
        Page repoPage = prefEntries.BoneMenuPage["Repo"];
        Prefs.PopulateAboutMenu(aboutPage);

        Utilities.CreateDirectoryRecursive(SaveUtils.PreviewDir); // subdir of userdatadir, should be able to call directly w/o recurse-creating
        
        Menu.OnPageOpened += PageSelected;

        BonemenuStringInput.Init();

        SpawnerStates.Init();

        FingerOffset.Init();

        await Assets.Init();

#if DEBUG
        Log($"Took {stopwatch.ElapsedMilliseconds}ms to init subsystems, but mainly load assets.");
#endif

        Hook.OntoMethod(typeof(PopUpMenuView), nameof(PopUpMenuView.Deactivate), MenuClosed);

        Instances<ObjectDestructible>.TryAutoCache(); // try patching to autocache but its a less important call. lol.
        Instances<Poolee>.TryAutoCache();
        Instances<AIBrain>.TryAutoCache();
        Instances<BoneLib.BoneMenu.UI.GUIMenu>.TryAutoCache();

        //Redirect.FromMethod(typeof(BoardGenerator).GetMethod(nameof(BoardGenerator.BoardSpawner)), (BoardGenerator inst, int idx, float mass) =>
        //{
        //    LogBoardGunState("pre boardspawner", inst);
        //    //Log($"pre boardspawner, idx = {idx}, mass = {mass}");
        //});

#if DEBUG
        // DONT UNCOMMENT THESE -- THEY BREAK BOARD JOINTS APPARENTLY

        Redirect.FromMethod(typeof(BoardGenerator._BoardSpawnerAsync_d__29).GetMethod(nameof(BoardGenerator._BoardSpawnerAsync_d__29.MoveNext))!, (BoardGenerator._BoardSpawnerAsync_d__29 inst) =>
        {
            try
            {
                Log($"PRE Boardspawner state = {inst.__1__state}, u1 = {inst.__u__1}");
                Log($"Current awaiter completed: " + inst.__u__1.IsCompleted);

                if (inst.__1__state == 0)
                    LogBoardGunState("post boardspawner", inst.__4__this);
                //Log($"pre boardspawner, idx = {idx}, mass = {mass}");
            }
            catch (Exception ex)
            {
                SceneSaverBL.Error($"Exception while molesting boardspawner: {ex}");
            }
        });

        Hook.OntoMethod(typeof(BoardGenerator._BoardSpawnerAsync_d__29), nameof(BoardGenerator._BoardSpawnerAsync_d__29.MoveNext), (BoardGenerator._BoardSpawnerAsync_d__29 inst) =>
        {
            try
            {
                Log($"POST Boardspawner state = {inst.__1__state}, u1 = {inst.__u__1}");
                Log($"Current awaiter completed: " + inst.__u__1.IsCompleted);

                if (inst.__1__state == 0)
                    LogBoardGunState("post boardspawner", inst.__4__this);
                //Log($"pre boardspawner, idx = {idx}, mass = {mass}");
            }
            catch (Exception ex)
            {
                SceneSaverBL.Error($"Exception while molesting boardspawner: {ex}");
            }
        });

        //Hook.OntoMethod(typeof(AssetSpawner._SpawnAsync_d__15), nameof(AssetSpawner._SpawnAsync_d__15.MoveNext), (AssetSpawner._SpawnAsync_d__15 inst) =>
        //{
        //    Log($"SpawnAsync state = {inst.__1__state}, u1 = {inst.__u__1} (u1 completed: {inst.__u__1.IsCompleted})");
        //    Log($"Current awaiter completed: " + inst.__u__1.IsCompleted);

        //    if (inst.__1__state == 0)
        //        Log($"SpawnAsync spawnable: Barcode = '{inst.spawnable.crateRef.Barcode.ID}' Name = '{inst.spawnable.crateRef.Crate.name}' " +
        //            $"Tags = {{ {string.Join(",", inst.spawnable.crateRef.Crate.Tags.ToArray().ToList())} }}");
        //});
#endif

        FetchUsername();

#if DEBUG
        //DateTime start = new DateTime(2025, 2, 20);
        //DateTime end = new(2025, 7, 30);
        //if (DateTime.Now < end)
        //{
        //    Log($"SSBL v{BuildInfo.Version} trial saving V{supportedSaveVers.Max()} files & reading files with versions {supportedSaveVers.Join()}");
        //    Log($"This trial debug version of SSBL will stop working on/after {end.ToShortDateString()}.");
        //    Log($"Please don't try modifying this build to remove this restriction. This version will likely be similar to the release build.");
        //}
        //else
        //{
        //    UnityWebRequest www = UnityWebRequest.Get("https://extraes.xyz/api/accesscontrol/ssbl/auth");
        //    var req = www.SendWebRequest();
        //    await AsyncUtilities.ToUniTask(req);
        //    const long SUCCESS = 200;
        //    if (req.webRequest.isNetworkError)
        //    {
        //        Error("Network error occurred.");
        //UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
        //    }
        //    if (req.webRequest.responseCode != SUCCESS)
        //    {
        //        Error("Expected " + SUCCESS + " but got " + req.webRequest.responseCode);
        //        UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
        //    }
        //}
#endif

        // do repo second to last because its not essential to the mod's function
        await RepoConsumer.Init(repoPage);

        // do startup last cuz its a non-essential call
        //await Stats.Startup();


        await Saves.Init(prefEntries.BoneMenuPage);
    }

    static void LogBoardGunState(string loggedFrom, BoardGenerator bg)
    {
        // ok so boardgun, cool and good stuff here. looks like the points are in worldspace for non-rigidbody'd things. i can work with this.
        Log($"Boardgun state as of {loggedFrom}:\n\tupDir = {bg.upDir}\n\tbuttonDown = {bg.ButtonDown}\n\tfirstPoint = {bg.firstPoint}\n\tEndPoint = {bg.EndPoint}\n\tFirstRb = {bg.FirstRb?.transform.GetFullPath() ?? "shid"}\n\tEndRb = {bg.EndRb?.transform.GetFullPath() ?? "shid"}");
    }

    public override void OnUpdate()
    {
        while (needInitialize.TryDequeue(out ISaveFile? save))
        {
            Log($"Save {save} - Now initializing from main thread Update");
            
            AsyncUtilities.WrapNoThrow(save.Initialize).RunOnFinish(ErrIfNotNull);
        }

        updateSw.Restart();
        while (runOnMainThread.TryDequeue(out Action? func) && updateSw.ElapsedMilliseconds < ConfigVars.timeSliceMs)
        {
#if DEBUG
            Log("Running on main thread (watch this shit kill itself)");
#endif
            func();
        }
        
        CameraFella.UpdatePosition();
        //FingerOffset.OnUpdate();
    }

    public override void OnLateUpdate()
    {
        FingerOffset.OnUpdate();
    }

    public override void OnGUI()
    {
        if (currentInputter is null)
            return;
        currentInputter.OnGUI(); // handles its own removal
    }

    static void FetchUsername()
    {
        if (Utilities.IsSteamVersion())
        {
            AsyncUtilities.WrapNoThrow(FetchSteamUsernameAsync).RunOnFinish(ErrIfNotNull);
        }
        else
        {
            AsyncUtilities.WrapNoThrow(FetchOculusUsername).RunOnFinish(ErrIfNotNull);
        }
    }

    static async Task FetchSteamUsernameAsync()
    {
        while (!JeviLib.DoneMappingNamespacesToAssemblies) await UniTask.Yield();

        Type? steamClient = Utilities.GetTypeFromString("Steamworks", "SteamClient");
        if (steamClient is null)
        {
            Warn("Steamworks not found! Are you using the Oculus/Meta version?");
            username = "Unknown";
            return;
        }
        username = (string)steamClient.GetProperty("Name")!.GetValue(null)!;

        PostcheckUsername();
    }

    static async Task FetchOculusUsername()
    {
        while (!Core.IsPlatformInitialized) await Task.Delay(1000);
        await Task.Delay(1000);

        var req = Users.GetLoggedInUser();
        while (req == null)
        {
            await Task.Yield();
            req = Users.GetLoggedInUser();
        }
        req.OnComplete(new Action<Message<User>>(SetOculusUsername));
    }

    static void SetOculusUsername(Message<User> msg)
    {
        if (msg.IsError)
        {
            Error("Error while getting OVR usernameBytes:" + msg.error.ToString());
            return;
        }
        
        username = msg.Data.DisplayName;
        PostcheckUsername();
    }

    static void PostcheckUsername()
    {
        Log("Detected usernameBytes as " + username);
        if (username.Length > 32)
        {
            username = username.Substring(0, 29) + "...";
            Log("Username is over 32 char, shortened to: " + username);
        }
    }

    static void PageSelected(Page category)
    {
#if DEBUG
        Log($"BoneMenu Category opened: {category.Name}");
#endif

        if (category != prefEntries.BoneMenuPage) return;

#if DEBUG
        Log("SSBL Menu opened");
#endif

        CameraFella.MenuOpened();
    }

    static void MenuClosed()
    {
#if DEBUG
        Log("Menu closed");
#endif

        Saves.MenuClosed();
        CameraFella.MenuClosed();
    }

    internal static Poolee? GetPooleeUpwards(Transform? t)
    {
        if (t == null) return null;

        Poolee ap = Poolee.Cache.Get(t.root.gameObject);
        if (ap == null) return GetPooleeUpwardsRecursive(t);
        
        return ap;
    }

    private static Poolee? GetPooleeUpwardsRecursive(Transform t)
    {
        Poolee poolee = Poolee.Cache.Get(t.gameObject);
        if (poolee) return poolee;

        Transform parent = t.parent;
        if (parent != null) return GetPooleeUpwardsRecursive(parent);
        else return null;
    }

    internal static AIBrain? GetBrainImmediateDownward(Transform t)
    {
        Transform parent = t;
        if (Instances<AIBrain>.TryGetFromCache(t.gameObject, out AIBrain? brain)) return brain;

        for (int i = 0; i < parent.childCount; i++)
        {
            t = parent.GetChild(i);
            if (Instances<AIBrain>.TryGetFromCache(t.gameObject, out brain)) return brain;
        }

        return null;
    }

    #region MelonLogger replacements

    static readonly SemaphoreSlim logSemaphore = new(1, 1);
    internal static void Log(string str)
    {
        try
        {
            logSemaphore.Wait();
            instance.LoggerInstance.Msg(str);
        }
        finally
        {
            logSemaphore.Release();
        }
    }

    internal static void Log(object? obj) => Log(obj?.ToString() ?? "null");
    internal static void Warn(string str)
    {
        try
        {
            logSemaphore.Wait();
            instance.LoggerInstance.Warning(str);
        }
        finally
        {
            logSemaphore.Release();
        }
    }

    internal static void Warn(object? obj) => Warn(obj?.ToString() ?? "null");
    internal static void WarnVariable(object? obj, [CallerArgumentExpression("obj")] string paramName = "<none>") => Warn($"{paramName}: {obj?.ToString()}" ?? "null");
    internal static void Error(string str)
    {
        try
        {
            logSemaphore.Wait();
            instance.LoggerInstance.Error(str);
        }
        finally
        {
            logSemaphore.Release();
        }
    }

    internal static void Error(object? obj) => Error(obj?.ToString() ?? "null");
    internal static void ErrIfNotNull(object? obj) { if (obj != null) Error(obj); }

    #endregion
}
