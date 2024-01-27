using BoneLib.BoneMenu;
using BoneLib.BoneMenu.Elements;
using Cysharp.Threading.Tasks;
using Harmony;
using Jevil;
using MelonLoader.TinyJSON;
using Newtonsoft.Json;
using SceneSaverRepo.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Tomlet;
using UnhollowerBaseLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Profiling.Memory.Experimental;

namespace SceneSaverBL;

internal static class RepoConsumer
{
    const string RECENTS_MENU_NAME_FORMAT = "Recents as of {0}m ago";
    static readonly string DownloadedSavePath = Path.Combine(SceneSaverBL.saveDir, "Downloaded");

    static MenuCategory repoMenu;
    static SceneSaverRepoInfo repoInfo;

    static MenuCategory workingMenu;
    static MenuCategory genericErrorMenu;
    static MenuCategory recentsMenu;
    static MenuCategory saveMetadataMenu;
    static MenuCategory uploadMenu;

    public static async Task Init(MenuCategory repoCategory)
    {
        Prefs.ssblRepo = Prefs.ssblRepo.TrimEnd('/');
        repoMenu = repoCategory;
        workingMenu = new("Working...", Color.gray);
        genericErrorMenu = new("Error!", Color.Lerp(Color.white, Color.red, 0.5f));
        recentsMenu = new(RECENTS_MENU_NAME_FORMAT, Color.white);
        saveMetadataMenu = new("Save metadata placeholder name", Color.white);
        uploadMenu = new("Upload a save", Color.white);

        Utilities.CreateDirectoryRecursive(DownloadedSavePath);

        repoMenu.Elements.Clear();

        UnityWebRequest repoInfoReq = UnityWebRequest.Get(Prefs.ssblRepo + "/api/repo/info");
        await AsyncUtilities.ToUniTask(repoInfoReq.SendWebRequest());

        if (repoInfoReq.WasCollected)
        {
            CreateMainErroredMenu("Web req GC'd");
            return;
        }

        if (repoInfoReq.downloadHandler.WasCollected)
        {
            CreateMainErroredMenu("DL handler GC'd");
            return;
        }

        switch (repoInfoReq.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                CreateMainErroredMenu("Net err - " + repoInfoReq.error);
                return;
            case UnityWebRequest.Result.ProtocolError:
                CreateMainErroredMenu($"HTTP err ({repoInfoReq.responseCode}) - " + repoInfoReq.error);
                return;
            case UnityWebRequest.Result.DataProcessingError:
                CreateMainErroredMenu($"Data err (from HTTP{repoInfoReq.responseCode}) - " + repoInfoReq.error);
                return;
        }

        try
        {
            repoInfo = JsonConvert.DeserializeObject<SceneSaverRepoInfo>(repoInfoReq.downloadHandler.text);
        }
        catch (Exception e)
        {
            SceneSaverBL.Log("Raw response from webserver: " + repoInfoReq.downloadHandler.text);
            SceneSaverBL.Error("Exception while deserializing SSBL Repo info: " + e);
            CreateMainErroredMenu("Parse fail: " + e.Message);
            return;
        }

        PopulateMenu();
    }

    private static void PopulateMenu()
    {
        string repoShortName;
        int idx = Prefs.ssblRepo.IndexOf("://");
        if (idx != -1 && idx < Prefs.ssblRepo.Length - 3)
            repoShortName = Prefs.ssblRepo.Split(new string[] { "://" }, StringSplitOptions.None)[1];
        else
            repoShortName = Prefs.ssblRepo;

        SubPanelElement repoInfoPanel = repoMenu.CreateSubPanel(repoShortName, Color.white);
        repoInfoPanel.CreateFunctionElement("Running V" + repoInfo.version, Color.white, SaveUtils.NothingAction);
        repoInfoPanel.CreateFunctionElement("Timezone " + repoInfo.timeZone, Color.white, SaveUtils.NothingAction);

        SubPanelElement repoStats = repoMenu.CreateSubPanel("Repo stats", Color.white);
        //repoStats.CreateFunctionElement($"Refreshed {Math.Round(repoInfo.timeSinceLastUpdate.TotalMinutes)}m ago", Color.white, SaveUtils.NothingAction);
        repoStats.CreateFunctionElement(repoInfo.totalDownloads + " total DLs", Color.white, SaveUtils.NothingAction);
        repoStats.CreateFunctionElement(repoInfo.totalDownloadsWeek + " DLs this week", Color.white, SaveUtils.NothingAction);

        repoMenu.CreateFunctionElement("Recents", Color.white, () => AsyncUtilities.WrapNoThrow(ShowRecents).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        //todo
        if (Utilities.IsPlatformQuest())
            repoMenu.CreateFunctionElement("DL from tag", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, false).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        else
        {
            SubPanelElement dlPanel = repoMenu.CreateSubPanel("DL from tag", Color.white);
            dlPanel.CreateFunctionElement("Input from BoneMenu", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, false).RunOnFinish(SceneSaverBL.ErrIfNotNull));
            dlPanel.CreateFunctionElement("Input from IMGUI", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, true).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        }

        //todo
        repoMenu.CreateFunctionElement("Upload", Color.white, () => AsyncUtilities.WrapNoThrow(ShowUploadMenu).RunOnFinish(SceneSaverBL.ErrIfNotNull));

    }



    private static async Task ShowRecents()
    {
#if DEBUG
        using DebugLineCounter dlc = new(SceneSaverBL.instance.LoggerInstance, DebugLineCounter.Kind.LINE_NUMBER, "ShowRecents");
#endif
        MenuManager.SelectCategory(workingMenu);

        DownloadHandler downloader = await SendRequestForOrShowError(Prefs.ssblRepo + "/api/repo/recent?skip=0&take=10");
        if (downloader is null)
            return;

#if DEBUG
        dlc.UpdateProgress();
#endif

        SceneSaverEntryCollection entries;
        try
        {
            entries = JsonConvert.DeserializeObject<SceneSaverEntryCollection>(downloader.text);
        }
        catch (Exception e)
        {
            SceneSaverBL.Log("Raw response from webserver: " + downloader.text);
            SceneSaverBL.Error("Exception while deserializing recent save files: " + e);
            SetAndSelectError("Parse fail: " + e.Message);
            return;
        }


#if DEBUG
        dlc.UpdateProgress();
#endif

        recentsMenu.SetName(string.Format(RECENTS_MENU_NAME_FORMAT, Math.Round(entries.TimeSinceLastUpdate.TotalMinutes)));
        recentsMenu.Elements.Clear();


#if DEBUG
        dlc.UpdateProgress();
#endif

        foreach (SceneSaverSaveEntry saveMeta in entries.Saves)
        {
            recentsMenu.CreateFunctionElement(saveMeta.name, Color.white, () => SelectSaveMetadata(saveMeta));
        }


#if DEBUG
        dlc.UpdateProgress();
#endif

        MenuManager.SelectCategory(recentsMenu);

#if DEBUG
        dlc.Success();
#endif
    }

    private static void SelectSaveMetadata(SceneSaverSaveEntry saveMeta)
    {
        TimeSpan? expiry = saveMeta.TimeUntilExpired();
        saveMetadataMenu.Elements.Clear();
        saveMetadataMenu.CreateFunctionElement("Tag: " + saveMeta.tag, Color.white, SaveUtils.NothingAction);
        //saveMetadataMenu.CreateFunctionElement("Share the tag ^^^", Color.white, SaveUtils.NothingAction);
        saveMetadataMenu.CreateFunctionElement("Hash: " + saveMeta.hash, Color.gray, SaveUtils.NothingAction);

        saveMetadataMenu.CreateFunctionElement("Save file version: " + saveMeta.version, Color.green, SaveUtils.NothingAction);
        saveMetadataMenu.CreateFunctionElement("Weekly downloads: " + saveMeta.downloadCountWeek, Color.gray, SaveUtils.NothingAction);
        saveMetadataMenu.CreateFunctionElement("Lifetime downloads: " + saveMeta.downloadCount, Color.gray, SaveUtils.NothingAction);

        if (expiry.HasValue)
            saveMetadataMenu.CreateFunctionElement($"Expires in {(int)expiry.Value.TotalDays}d {expiry.Value.Hours}h {expiry.Value.Minutes}m", Color.green, SaveUtils.NothingAction);
        else
            saveMetadataMenu.CreateFunctionElement("Doesn't expire", Color.green, SaveUtils.NothingAction);

        if (SceneSaverBL.supportedSaveVers.Contains(saveMeta.version))
            saveMetadataMenu.CreateFunctionElement("Download", Color.green, () => AsyncUtilities.WrapNoThrow(DownloadSave, saveMeta).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        else
            saveMetadataMenu.CreateFunctionElement($"Can't read V{saveMeta.version} saves", Color.green, SaveUtils.NothingAction);
    }

    static async Task DownloadSave(SceneSaverSaveEntry metadata)
    {
        MenuManager.SelectCategory(workingMenu);

        DownloadHandler downloader = await SendRequestForOrShowError(Prefs.ssblRepo + "/api/saves/download?tag=" + metadata.tag);
        if (downloader is null)
            return;
        
        string path = Path.Combine(DownloadedSavePath, "Downloaded " + metadata.name);
        File.WriteAllBytes(path, downloader.data);

        Saves.ShowBoneMenu();
    }

    private static void CreateMainErroredMenu(string text, string otherText = "")
    {
        repoMenu.CreateFunctionElement("Repo client error", Color.red, SaveUtils.NothingAction);
        repoMenu.CreateFunctionElement(text, Color.Lerp(Color.white, Color.red, 0.5f), SaveUtils.NothingAction);
        if (!string.IsNullOrEmpty(otherText))
            repoMenu.CreateFunctionElement(otherText, Color.Lerp(Color.white, Color.red, 0.25f), SaveUtils.NothingAction);

        repoMenu.CreateFunctionElement("Retry", Color.Lerp(Color.white, Color.green, 0.25f), () => AsyncUtilities.WrapNoThrow(Init, repoMenu).RunOnFinish(SceneSaverBL.ErrIfNotNull));
    }

    private static async Task DownloadFromTag(bool useImgui)
    {
        string inputTag;
        if (useImgui)
        {
            MenuManager.SelectCategory(workingMenu);
            inputTag = await IMGUIInputField.GetStringAsync(45);
        }
        else
        {
            inputTag = await BonemenuStringInput.GetStringInput("ABCDEF", BonemenuStringInput.ALL_NUMBERS_ALLOWED, string.Empty);
        }

        const string HEXADEC = "0123456789ABCDEF";
        foreach (char character in inputTag)
        {
            if (!HEXADEC.Contains(char.ToUpper(character)))
            {
                SetAndSelectError($"Tags don't contain: " + character);
                return;
            }
        }

        MenuManager.SelectCategory(workingMenu);

        DownloadHandler infoDownloader = await SendRequestForOrShowError(Prefs.ssblRepo + "/api/saves/info?tag=" + inputTag);
        if (infoDownloader is null)
            return;

        SceneSaverSaveEntry saveMeta;
        try
        {
            saveMeta = JsonConvert.DeserializeObject<SceneSaverSaveEntry>(infoDownloader.text);
        }
        catch (Exception e)
        {
            SceneSaverBL.Log("Raw response from webserver: " + infoDownloader.text);
            SceneSaverBL.Error("Exception while deserializing recent save files: " + e);
            SetAndSelectError("Parse fail: " + e.Message);
            return;
        }

        SelectSaveMetadata(saveMeta);
    }

    private static async Task ShowUploadMenu()
    {
        MenuManager.SelectCategory(workingMenu);

        uploadMenu.Elements.Clear();

        await AsyncUtilities.ForEachTimeSlice(Directory.EnumerateFiles(SceneSaverBL.saveDir, "*.ssbl"), savePath =>
        {
            if (savePath.StartsWith(DownloadedSavePath))
                return;

            SubPanelElement subPanel = uploadMenu.CreateSubPanel(Path.GetFileNameWithoutExtension(savePath), Color.white);
            subPanel.CreateFunctionElement("Delete", Color.red, () => File.Delete(savePath));
            subPanel.CreateFunctionElement("Upload", Color.white, () => AsyncUtilities.WrapNoThrow(UploadSave, savePath).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        }, 1);

        MenuManager.SelectCategory(uploadMenu);
    }

    // public: to be used in V6 save menus
    public static async Task UploadSave(string savePath)
    {
#if DEBUG
        if (!File.Exists(savePath))
            throw new FileNotFoundException("Save file not found, cannot upload! File: " + savePath);
#endif
        MenuManager.SelectCategory(workingMenu);

        string base64 = null;

        Task convertToBase64 = Task.Run(() => {
            byte[] fileBytes = File.ReadAllBytes(savePath);
            base64 = Convert.ToBase64String(fileBytes);
        });

        while (!convertToBase64.IsCompleted)
            await UniTask.Yield();

        if (convertToBase64.IsFaulted)
        {
            SetAndSelectError("File load/convert error");
            return;
        }

        string filename = Path.GetFileName(savePath);

        DownloadHandler response = await SendRequestForOrShowError($"{Prefs.ssblRepo}/api/saves/uploadBase64?filename={filename}&data={base64}", "PUT");
        if (response is null)
            return;

        SceneSaverSaveEntry entry = JsonConvert.DeserializeObject<SceneSaverSaveEntry>(response.text);
        SelectSaveMetadata(entry);
    }

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    private static async Task<DownloadHandler?> SendRequestForOrShowError(string url, string method = "GET")
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    {
        UnityWebRequest webReq = UnityWebRequest.Get(url);
        
        if (method != "GET")
            webReq.method = method;

        await AsyncUtilities.ToUniTask(webReq.SendWebRequest());

        if (webReq.WasCollected)
        {
            SetAndSelectError("Web req GC'd");
            return null;
        }

        if (webReq.downloadHandler.WasCollected)
        {
            SetAndSelectError("DL handler GC'd");
            return null;
        }

        switch (webReq.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                SetAndSelectError("Net err - " + webReq.error);
                return null;
            case UnityWebRequest.Result.ProtocolError:
                SetAndSelectError($"HTTP err ({webReq.responseCode}) - " + webReq.error);
                return null;
            case UnityWebRequest.Result.DataProcessingError:
                SetAndSelectError($"Data err (from HTTP{webReq.responseCode}) - " + webReq.error);
                return null;
        }

        return webReq.downloadHandler;
    }

    private static void SetAndSelectError(string text)
    {
        MenuElement element = genericErrorMenu.Elements.FirstOrDefault() ?? genericErrorMenu.CreateFunctionElement("", Color.white, SaveUtils.NothingAction);
        element.SetName(text);
        MenuManager.SelectCategory(genericErrorMenu);
    }

}
