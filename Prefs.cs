using BoneLib.BoneMenu.Elements;
using Jevil;
using Jevil.Prefs;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

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
    [RangePref(1, 10, 1)]
    internal static int timeSliceMs = 3;
    [RangePref(64, 2048, 64)]
    internal static int previewSize = 256;
    [Pref("Performs extra checks while saving, to make sure data will be able to be loaded properly (has fallback behavior)")]
    internal static bool saveChecks = true;
    [Pref("Attempts to avoid lag spikes by splitting the full-save (that don't occur in quicksaves) processes over time, as opposed to fullsaving/loading each object in its entirety all at once")]
    internal static bool fullsaveOverTime = true;
    [Pref("Loads WELD constraints that are between the current object and a non-saved object")]
    internal static bool loadStaticWelds = false;
    [Pref("If true, will move wire when only Grip+Trigger are held, otherwise will require Grip+Trigger+StickClick")]
    internal static bool dontUseStickClick = false;

    internal static SelectionZone activeSelection;

    static void CreateSelectionWire()
    {
        GameObject go = new("SceneSaver Selection Wire");
        Utilities.MoveAndFacePlayer(go);
        go.transform.localRotation = Quaternion.identity;
        activeSelection = go.AddComponent<SelectionZone>();
    }

    public static void PopulateAboutMenu(MenuCategory category)
    {
        Type[] saves = SceneSaverBL.instance.MelonAssembly.Assembly.GetTypes().Where(t => t.GetInterfaces().FirstOrDefault() == typeof(Interfaces.ISaveFile) && t != typeof(Versions.FailedSaveFile)).ToArray();
        int[] supportedVersions = new int[saves.Length];
        for (int i = 0; i < saves.Length; i++)
        {
            string nspace = saves[i].Namespace;
            char[] numSequence = nspace.SkipWhile(c => !char.IsDigit(c)).ToArray();
            int.TryParse(new(numSequence), out int ver);
            supportedVersions[i] = ver;
        }

#if DEBUG
        const bool IS_DBG = true;
#else
        const bool IS_DBG = false;
#endif

        category.CreateFunctionElement($"Version {BuildInfo.Version} ({(IS_DBG ? "Debug" : "Release")})", Color.white, SaveUtils.NothingAction);
        category.CreateFunctionElement($"Created by {BuildInfo.Author}", Color.white, SaveUtils.NothingAction);
        category.CreateFunctionElement($"Creates V{supportedVersions.Max()} saves", Color.white, SaveUtils.NothingAction);
        category.CreateFunctionElement($"Reads V{string.Join(" , V", supportedVersions)}", Color.white, SaveUtils.NothingAction);
        category.CreateFunctionElement("Donate (opens browser)", new Color(0.9f, 0.75f, 0.75f), () => Application.OpenURL("https://ko-fi.com/extraes"));
    }

    [Pref("About")]
    public static void SceneSaver() { }

    [Pref("", 0.75f, 1f, 0.75f)]
    public static void ShowSaves()
    {
        Saves.ShowBoneMenu();
        //AsyncUtilities.WrapNoThrow().RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    [Pref("Wire")]
    public static void ShowWire()
    {
        if (activeSelection.INOC()) CreateSelectionWire();
        if (activeSelection.gameObject.active) return;

        Utilities.MoveAndFacePlayer(activeSelection.gameObject);
        activeSelection.transform.rotation = Quaternion.identity;
        //activeSelection.transform.localScale = Vector3.one;
        activeSelection.gameObject.SetActive(true);
    }

    [Pref("Wire", UnityDefaultColor.GRAY)]
    public static void HideWire()
    {
        if (activeSelection.INOC()) CreateSelectionWire();
        if (!activeSelection.gameObject.active) return;

        activeSelection.gameObject.SetActive(false);
    }

    [Pref("Wire", UnityDefaultColor.GRAY)]
    public static void ShowTutorial()
    {
        ControllerTutorial.Show();
    }

    [Pref("Saving")]
    public static void FullSaveSelection()
    {
        if (activeSelection.INOC() || !activeSelection.gameObject.active) return;

        SceneSaverBL.isFullSave = true;
        AsyncUtilities.WrapNoThrow(Saves.DoSave).RunOnFinish(SaveFinished);
    }

    [Pref("Saving")]
    public static void QuickSaveSelection()
    {
        if (activeSelection.INOC() || !activeSelection.gameObject.active) return;

        SceneSaverBL.isFullSave = false;
        SceneSaverBL.currentlySaving = true;
        AsyncUtilities.WrapNoThrow(Saves.DoSave).RunOnFinish(SaveFinished);
    }

    private static void SaveFinished(Exception ex)
    {
        SceneSaverBL.currentlySaving = false;

        if (ex is not null)
        {
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
}