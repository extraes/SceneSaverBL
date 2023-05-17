using Jevil;
using Jevil.Prefs;
using MelonLoader;
using UnityEngine;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using BoneLib.BoneMenu;
using BoneLib.BoneMenu.Elements;
using System.IO;
using Jevil.IMGUI;
using SLZ.Marrow.Pool;
using SLZ.Props;
using Jevil.Spawning;
using SLZ.Marrow.Data;
using System.Collections.Generic;
using UnhollowerRuntimeLib;
using SLZ.AI;
using Jevil.Patching;
using SLZ.UI;
using System;
using SceneSaverBL.Interfaces;
using System.Collections.Concurrent;
using Oculus.Platform;
using Oculus.Platform.Models;

namespace SceneSaverBL;

public static class BuildInfo
{
    public const string Name = "SceneSaverBL"; // Name of the Mod.  (MUST BE SET)
    public const string Author = "extraes"; // Author of the Mod.  (Set as null if none)
    public const string Company = null; // Company that made the Mod.  (Set as null if none)
    public const string Version = "1.0.0"; // Version of the Mod.  (MUST BE SET)
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
    private readonly static Stopwatch updateSw = new();

    internal static string saveDir = Path.Combine(MelonUtils.UserDataDirectory, "SceneSaver", "Saves");
    internal static string dupesDir = Path.Combine(MelonUtils.UserDataDirectory, "SceneSaver", "Dupes");

    public override void OnEarlyInitializeMelon() => SaveChecks.EstablishMainThread();

    public override async void OnInitializeMelon()
    {
#if DEBUG
        Log("This is a debug build of SSBL, intended for use with JeviLib V" + JevilBuildInfo.VERSION);

        Stopwatch stopwatch = Stopwatch.StartNew();
#endif 

        prefEntries = Preferences.Register(typeof(Prefs));
        MenuCategory mCat = prefEntries.BoneMenuCategory.Elements.First(e => e.Name == "About") as MenuCategory;
        Prefs.PopulateAboutMenu(mCat);
        
        Saves.Init(prefEntries.BoneMenuCategory);
        
        MenuManager.OnCategorySelected += CategorySelected;
        
        await Assets.Init();

#if DEBUG
        Log($"Took {stopwatch.ElapsedMilliseconds}ms to load particle wire bundle");
#endif

        Hook.OntoMethod(typeof(PopUpMenuView).GetMethod(nameof(PopUpMenuView.Deactivate)), MenuClosed);
        //return;
        FetchUsername();
        // do startup last cuz its a non-essential call
        await Stats.Startup();
    }

    public override void OnUpdate()
    {
        while (needInitialize.TryDequeue(out ISaveFile save))
        {
            Log("Save " + save.ToString() + " - Now initializing from main thread Update");
            
            AsyncUtilities.WrapNoThrow(save.Initialize).RunOnFinish(ErrIfNotNull);
        }

        updateSw.Restart();
        while (runOnMainThread.TryDequeue(out Action func) && updateSw.ElapsedMilliseconds < Prefs.timeSliceMs)
        {
            Log("Running on main thread (watch this shit kill itself)");
            func();
        }
        
        CameraFella.UpdatePosition();
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
        while (!JeviLib.DoneMappingNamespacesToAssemblies) await Task.Yield();

        username = Utilities.GetTypeFromString("Steamworks", "SteamClient").GetProperty("Name").GetValue(null) as string;

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

    static void CategorySelected(MenuCategory category)
    {
        if (category != prefEntries.BoneMenuCategory) return;

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

        CameraFella.MenuClosed();
    }

    internal static AssetPoolee GetPooleeUpwards(Transform t)
    {
        AssetPoolee ap = AssetPoolee.Cache.Get(t.root.gameObject);
        if (ap == null) return GetPooleeUpwardsRecursive(t);
        
        return ap;
    }

    private static AssetPoolee GetPooleeUpwardsRecursive(Transform t)
    {
        AssetPoolee poolee = AssetPoolee.Cache.Get(t.gameObject);
        if (poolee) return poolee;

        Transform parent = t.parent;
        if (parent != null) return GetPooleeUpwardsRecursive(parent);
        else return null;
    }

    internal static AIBrain GetBrainImmediateDownward(Transform t)
    {
        Transform parent = t;
        if (AIBrain.Cache.TryGet(t.gameObject, out AIBrain brain)) return brain;

        for (int i = 0; i < parent.childCount; i++)
        {
            t = parent.GetChild(i);
            if (AIBrain.Cache.TryGet(t.gameObject, out brain)) return brain;
        }

        return null;
    }

    #region MelonLogger replacements

    internal static void Log(string str) => instance.LoggerInstance.Msg(str);
    internal static void Log(object obj) => instance.LoggerInstance.Msg(obj?.ToString() ?? "null");
    internal static void Warn(string str) => instance.LoggerInstance.Warning(str);
    internal static void Warn(object obj) => instance.LoggerInstance.Warning(obj?.ToString() ?? "null");
    internal static void Error(string str) => instance.LoggerInstance.Error(str);
    internal static void Error(object obj) => instance.LoggerInstance.Error(obj?.ToString() ?? "null");
    internal static void ErrIfNotNull(object obj) { if (obj != null) Error(obj); }

    #endregion
}
