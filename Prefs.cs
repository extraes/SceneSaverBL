using Jevil.Prefs;
using System.Diagnostics.CodeAnalysis;

namespace SceneSaverBL;

[Preferences("SceneSaver", true)]
internal static class Prefs
{
    [Pref("Always shows where the preview image will be taken from, as opposed to only when SSBL's BoneMenu is open. (By showing an orange smiley face)")]
    internal static bool showPreviewLocation = false;
    [Pref]
    internal static bool disablePolaroid = false;
    [Pref]
    internal static bool filterByLevel = true;
    [Pref("Freezes objects while other objects are still loading. This may cause saves to load slower than if it was disabled.")]
    internal static bool freezeWhileLoading = true;
    [Pref("Performs extra checks while saving, to make sure data will be able to be loaded properly (has fallback behavior)")]
    internal static bool saveChecks = true;
    [Pref("Loads WELD constraints that are between the current object and a non-saved object")]
    internal static bool loadStaticWelds = false;
    [Pref("If true, will move wire when only Grip+Trigger are held, otherwise will require Grip+Trigger+StickClick")]
    internal static bool dontUseStickClick = false;
    [Pref("The URL base to be used when fetching from the online save repository. (if you replace this, dont include a slash at the end.)")]
    internal static string ssblRepo = "https://ssbl.extraes.xyz";
    [Pref($"Disables stats. SSBL uses stats to inform the developer ({BuildInfo.Author}) of: How many times the mod was used on Q2/PC, how many saves were saved/loaded")]
    internal static bool disableStats = false;
    [Pref("If true, will use the right hand for the dupe line, otherwise will use the left hand")]
    internal static bool rightHandDupeLine = true;
    [Pref("If true, dupes will be allowed to spawn in walls")]
    internal static bool dontAdjustDupePos = false;
    [Pref("If true, tells fusion to sync spawned objects.")]
    internal static bool fusionSync = true;

    internal static SelectionZone? activeSelection;

    [MemberNotNull(nameof(activeSelection))]
    static void CreateSelectionWire()
    {
        GameObject go = new("SceneSaver Selection Wire");
        Utilities.MoveAndFacePlayer(go);
        go.transform.localRotation = Quaternion.identity;
        activeSelection = go.AddComponent<SelectionZone>();
        activeSelection.gameObject.SetActive(true);
    }

    public static void PopulateAboutMenu(Page category)
    {
        Type[] saves = SceneSaverBL.instance.MelonAssembly.Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(t => t == typeof(Interfaces.ISaveFile)) && t != typeof(Versions.FailedSaveFile)).ToArray();
        int[] supportedVersions = new int[saves.Length];
        for (int i = 0; i < saves.Length; i++)
        {
            string nspace = saves[i].Namespace ?? "";
            char[] numSequence = nspace.SkipWhile(c => !char.IsDigit(c)).ToArray();
            int.TryParse(new(numSequence), out int ver);
            supportedVersions[i] = ver;
        }

        SceneSaverBL.supportedSaveVers = supportedVersions;

#if DEBUG
        const bool IS_DBG = true;
#else
        const bool IS_DBG = false;
#endif

        category.CreateFunction($"Version {BuildInfo.Version} ({(IS_DBG ? "Debug" : "Release")})", Color.white, SaveUtils.NothingAction);
        category.CreateFunction($"Created by {BuildInfo.Author}", Color.white, SaveUtils.NothingAction);
        category.CreateFunction($"Creates V{supportedVersions.Max()} saves", Color.white, SaveUtils.NothingAction);
        category.CreateFunction($"Reads V{string.Join(" , V", supportedVersions)}", Color.white, SaveUtils.NothingAction);
        category.CreateFunction("Donate (opens browser)", new Color(0.9f, 0.75f, 0.75f), () => Application.OpenURL("https://ko-fi.com/extraes"));
    }

    [Pref("", 0.75f, 1f, 0.75f)]
    public static void ShowSaves()
    {
        Saves.ShowBoneMenu();
    }

    [Pref("Saving")]
    public static void ToggleSelectionWire()
    {
        if (activeSelection == null)
        {
            CreateSelectionWire();
            return;
        }

        if (!activeSelection.gameObject.active)
        {
            Utilities.MoveAndFacePlayer(activeSelection.gameObject);
            activeSelection.transform.rotation = Quaternion.identity;
            activeSelection.gameObject.SetActive(true);
        }
        else
        {
            activeSelection.gameObject.SetActive(false);
            CameraFella.MenuClosed();
        }
    }


    [Pref("Saving")]
    public static void SaveSelection()
    {
        if (activeSelection == null || !activeSelection.gameObject.active) return;

        SceneSaverBL.isFullSave = true;
        SceneSaverBL.currentlySaving = true;
        AsyncUtilities.WrapNoThrow(Saves.DoSave).RunOnFinish(SaveFinished);
    }

    [Pref("Saving", UnityDefaultColor.GRAY)]
    public static void ShowTutorial()
    {
        ControllerTutorial.Show();
    }


    // same thing for repo, but leave this "there was a bug" element in case RepoConsumer never gets to init and clear out the category
    [Pref("Repo")]
    public static void ThereWasABugYay() { } 

    // creates a new category for the "About" menu with SceneSaver in it, lol.
    [Pref("About")]
    public static void SceneSaver() { }

    // pointless, just make everything a fullsave
    //[Pref("Saving")]
    //public static void QuickSaveSelection()
    //{
    //    if (activeSelection == null || !activeSelection.gameObject.active) return;
    //    SceneSaverBL.isFullSave = false;
    //    SceneSaverBL.currentlySaving = true;
    //    AsyncUtilities.WrapNoThrow(Saves.DoSave).RunOnFinish(SaveFinished);
    //}

    private static void SaveFinished(Exception? ex)
    {
        SceneSaverBL.currentlySaving = false;

        if (ex is null)
            return;
        

        SceneSaverBL.Error($"SAVING FAILED! Cleaning saves directory of unfilled saves.\n\t More details: {ex}");
        foreach (string path in Directory.EnumerateFiles(SceneSaverBL.saveDir, "*.ssbl"))
        {
            long fileSize = new FileInfo(path).Length;
            if (fileSize > 5)
                continue;
            SceneSaverBL.Warn($"Deleting : " + path);
            File.Delete(path);
        }
    }
}