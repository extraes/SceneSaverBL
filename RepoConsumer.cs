using Newtonsoft.Json;
using SceneSaverBL.Interfaces;
using SceneSaverRepo.Data;
using System.Net;
using UnityEngine.Networking;

namespace SceneSaverBL;

internal static class RepoConsumer
{
    private enum InputMethod
    {
        BONEMENU,
        IMGUI,
        CLIPBOARD
    }

    const string HEXADEC = "0123456789ABCDEF";
    const string RECENTS_MENU_NAME_FORMAT = "Recents as of {0}m ago";
    static readonly string DownloadedSavePath = Path.Combine(SceneSaverBL.saveDir, "Downloaded");

    static Page downloadedPage;
    static Page repoPage;
    static SceneSaverRepoInfo repoInfo;

    static Page workingPage;
    static Page genericErrorPage;
    static Page recentsPage;
    static Page saveMetadataPage;
    static Page uploadPage;

    static readonly List<ISaveFile> saves = new();

    public static async Task Init(Page repoCategory)
    {
        Prefs.ssblRepo = Prefs.ssblRepo.TrimEnd('/');
        repoPage = repoCategory;
        workingPage = repoPage.CreatePage("Working...", Color.gray);
        genericErrorPage = repoPage.CreatePage("Error!", Color.Lerp(Color.white, Color.red, 0.5f));
        recentsPage = repoPage.CreatePage(RECENTS_MENU_NAME_FORMAT, Color.white);
        saveMetadataPage = repoPage.CreatePage("Save metadata placeholder name", Color.white);
        uploadPage = repoPage.CreatePage("Upload a save", Color.white);
        downloadedPage = repoPage.CreatePage("Downloaded", Color.white);

        Utilities.CreateDirectoryRecursive(DownloadedSavePath);

        repoPage.RemoveAll();

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
            repoInfo = System.Text.Json.JsonSerializer.Deserialize<SceneSaverRepoInfo>(repoInfoReq.downloadHandler.text)!;
        }
        catch (Exception e)
        {
            SceneSaverBL.Log("Raw response from webserver: " + repoInfoReq.downloadHandler.text);
            SceneSaverBL.Error("Exception while deserializing SSBL Repo info: " + e);
            CreateMainErroredMenu("Parse fail: " + e.Message);
            return;
        }

        PopulateMenu();

        foreach (string savePath in Directory.EnumerateFiles(DownloadedSavePath, "*.ssbl"))
        {
            ISaveFile save = await Saves.CreateSave(savePath);
            saves.Add(save);
        }
    }

    private static void PopulateMenu()
    {
        string repoShortName;
        int idx = Prefs.ssblRepo.IndexOf("://");
        if (idx != -1 && idx < Prefs.ssblRepo.Length - 3)
            repoShortName = Prefs.ssblRepo.Split(new string[] { "://" }, StringSplitOptions.None)[1];
        else
            repoShortName = Prefs.ssblRepo;

        Page repoInfoPage = repoPage.CreatePage(repoShortName, Color.white);
        repoInfoPage.CreateFunction("Running V" + repoInfo.version, Color.white, SaveUtils.NothingAction);
        repoInfoPage.CreateFunction("Timezone " + repoInfo.timeZone, Color.white, SaveUtils.NothingAction);

        //repoStats.CreateFunction($"Refreshed {Math.Round(repoInfo.timeSinceLastUpdate.TotalMinutes)}m ago", Color.white, SaveUtils.NothingAction);
        repoInfoPage.CreateFunction(repoInfo.totalDownloads + " total DLs", Color.white, SaveUtils.NothingAction);
        repoInfoPage.CreateFunction(repoInfo.totalDownloadsWeek + " DLs this week", Color.white, SaveUtils.NothingAction);

        repoPage.CreateFunction("Recents", Color.white, () => AsyncUtilities.WrapNoThrow(ShowRecents, 0).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        repoPage.CreateFunction("Upload", Color.white, () => AsyncUtilities.WrapNoThrow(ShowUploadMenu).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        Page dlPage = repoPage.CreatePage("DL from tag", Color.Lerp(Color.green, Color.white, 0.5f));
        dlPage.CreateFunction("Input from BoneMenu", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, InputMethod.BONEMENU).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        dlPage.CreateFunction("Input from Clipboard", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, InputMethod.CLIPBOARD).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        if (!Utilities.IsPlatformQuest())
            dlPage.CreateFunction("Input from Spectator Screen", Color.white, () => AsyncUtilities.WrapNoThrow(DownloadFromTag, InputMethod.IMGUI).RunOnFinish(SceneSaverBL.ErrIfNotNull));

        repoPage.CreateFunction("Downloaded", Color.white, ShowDownloaded);
    }

    private static void ShowDownloaded()
    {
        downloadedPage.RemoveAll();

        foreach (ISaveFile save in saves)
        {


            save.PopulateBoneMenu(downloadedPage);
        }

        Menu.OpenPage(downloadedPage);
    }

    private static async Task ShowRecents(int pageIdx)
    {
#if DEBUG
        using DebugLineCounter dlc = new(SceneSaverBL.instance.LoggerInstance, DebugLineCounter.Kind.LINE_NUMBER, "ShowRecents");
#endif
        Menu.OpenPage(workingPage);

        DownloadHandler? downloader = await SendRequestForOrShowError(Prefs.ssblRepo + $"/api/repo/recent?skip={pageIdx * 10}&take={10}");
        if (downloader is null)
            return;

#if DEBUG
        dlc.UpdateProgress();
#endif
        SceneSaverEntryCollection entries;
        try
        {
            // system json can suck me
            entries = Newtonsoft.Json.JsonConvert.DeserializeObject<SceneSaverEntryCollection>(downloader.text)!;
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

        recentsPage.Name = string.Format(RECENTS_MENU_NAME_FORMAT, Math.Round(entries.TimeSinceLastUpdate.TotalMinutes));
        recentsPage.RemoveAll();

#if DEBUG
        dlc.UpdateProgress();
        Utilities.InspectInUnityExplorer(entries);
#endif

        recentsPage.CreateFunction($"Page {pageIdx + 1}", Color.white, SaveUtils.NothingAction);
        foreach (SceneSaverSaveEntry saveMeta in entries.Saves)
        {
#if DEBUG
            SceneSaverBL.Log($"Adding {(saveMeta?.name) ?? "NULL!!!"} to page {(pageIdx + 1)}");
#endif
            recentsPage.CreateFunction(saveMeta.name, Color.white, () => SelectSaveMetadata(saveMeta));
        }


#if DEBUG
        dlc.UpdateProgress();
#endif

        if (pageIdx > 0)
            recentsPage.CreateFunction("< Previous page", Color.white, () => AsyncUtilities.WrapNoThrow(ShowRecents, pageIdx - 1).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        if (entries.Saves.Count() == 10)
            recentsPage.CreateFunction("Next page >", Color.white, () => AsyncUtilities.WrapNoThrow(ShowRecents, pageIdx + 1).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        else if (entries.Saves.Count() == 0)
            recentsPage.CreateFunction("No more pages", Color.white, SaveUtils.NothingAction);


#if DEBUG
        dlc.UpdateProgress();
#endif

        Menu.OpenPage(recentsPage);

#if DEBUG
        dlc.Success();
#endif
    }

    private static void SelectSaveMetadata(SceneSaverSaveEntry saveMeta)
    {
        if (saveMeta.name == "error" && string.IsNullOrEmpty(saveMeta.owner))
        {
            SetAndSelectError("No such save found");
            return;
        }

        TimeSpan? expiry = saveMeta.TimeUntilExpired();
        saveMetadataPage.RemoveAll();
        saveMetadataPage.Name = saveMeta.name;

        if (!Utilities.IsPlatformQuest())
        {
            saveMetadataPage.CreateFunction("Copy tag to clipboard", Color.white, () => GUIUtility.systemCopyBuffer = saveMeta.tag);
        }

        saveMetadataPage.CreateFunction("Tag: " + saveMeta.tag, Color.white, SaveUtils.NothingAction);
        //saveMetadataMenu.CreateFunction("Share the tag ^^^", Color.white, SaveUtils.NothingAction);
        saveMetadataPage.CreateFunction("Hash: " + saveMeta.hash, Color.gray, SaveUtils.NothingAction);

        saveMetadataPage.CreateFunction("Save file version: " + saveMeta.version, Color.green, SaveUtils.NothingAction);
        saveMetadataPage.CreateFunction("Weekly downloads: " + saveMeta.downloadCountWeek, Color.gray, SaveUtils.NothingAction);
        saveMetadataPage.CreateFunction("Lifetime downloads: " + saveMeta.downloadCount, Color.gray, SaveUtils.NothingAction);
        saveMetadataPage.CreateFunction("Uploaded by ID: " + saveMeta.owner, Color.gray, SaveUtils.NothingAction);

        if (expiry.HasValue)
            saveMetadataPage.CreateFunction($"Expires in {(int)expiry.Value.TotalDays}d {expiry.Value.Hours}h {expiry.Value.Minutes}m", Color.green, SaveUtils.NothingAction);
        else
            saveMetadataPage.CreateFunction("Doesn't expire", Color.green, SaveUtils.NothingAction);

        if (SceneSaverBL.supportedSaveVers.Contains(saveMeta.version))
            saveMetadataPage.CreateFunction("Download", Color.green, () => AsyncUtilities.WrapNoThrow(DownloadSave, saveMeta).RunOnFinish(SceneSaverBL.ErrIfNotNull));
        else
            saveMetadataPage.CreateFunction($"Can't read V{saveMeta.version} saves", Color.red, SaveUtils.NothingAction);

        Menu.OpenPage(saveMetadataPage);
    }

    static async Task DownloadSave(SceneSaverSaveEntry metadata)
    {
        Menu.OpenPage(workingPage);

        DownloadHandler? downloader = await SendRequestForOrShowError(Prefs.ssblRepo + "/api/saves/download?tag=" + metadata.tag);
        if (downloader is null)
            return;
        
        string path = Path.Combine(DownloadedSavePath, "DL - " + metadata.name);
        File.WriteAllBytes(path, downloader.data);

        ShowDownloaded();
    }

    private static void CreateMainErroredMenu(string text, string otherText = "")
    {
        repoPage.CreateFunction("Repo client error", Color.red, SaveUtils.NothingAction);
        repoPage.CreateFunction(text, Color.Lerp(Color.white, Color.red, 0.5f), SaveUtils.NothingAction);
        if (!string.IsNullOrEmpty(otherText))
            repoPage.CreateFunction(otherText, Color.Lerp(Color.white, Color.red, 0.25f), SaveUtils.NothingAction);

        repoPage.CreateFunction("Retry", Color.Lerp(Color.white, Color.green, 0.25f), () => AsyncUtilities.WrapNoThrow(Init, repoPage).RunOnFinish(SceneSaverBL.ErrIfNotNull));
    }

    private static async Task DownloadFromTag(InputMethod inputMethod)
    {
        string inputTag;

        switch (inputMethod)
        {
            case InputMethod.BONEMENU:
                inputTag = await BonemenuStringInput.GetStringInput("ABCDEF", BonemenuStringInput.ALL_NUMBERS_ALLOWED, string.Empty);
                break;
            case InputMethod.IMGUI:
                Menu.OpenPage(workingPage);
                inputTag = await IMGUIInputField.GetStringAsync(60, "", HEXADEC);
                break;
            case InputMethod.CLIPBOARD:
                inputTag = GUIUtility.systemCopyBuffer;
                break;
            default:
                return;
        }

        foreach (char character in inputTag)
        {
            if (!HEXADEC.Contains(char.ToUpper(character)))
            {
                SetAndSelectError($"Tags don't contain: " + character);
                return;
            }
        }

        Menu.OpenPage(workingPage);

        DownloadHandler? infoDownloader = await SendRequestForOrShowError(Prefs.ssblRepo + "/api/saves/info?tag=" + inputTag);
        if (infoDownloader is null)
            return;

        SceneSaverSaveEntry saveMeta;
        try
        {
            saveMeta = System.Text.Json.JsonSerializer.Deserialize<SceneSaverSaveEntry>(infoDownloader.text)!;
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
        Menu.OpenPage(workingPage);

        uploadPage.RemoveAll();

        await AsyncUtilities.ForEachTimeSlice(Directory.EnumerateFiles(SceneSaverBL.saveDir, "*.ssbl"), savePath =>
        {
            if (savePath.StartsWith(DownloadedSavePath))
                return;


            Page filePage = uploadPage.CreatePage(Path.GetFileNameWithoutExtension(savePath), Color.white);

            filePage.CreateFunction("Delete", Color.red, () => File.Delete(savePath));
            Action performUpload = () => AsyncUtilities.WrapNoThrow(UploadSave, savePath).RunOnFinish(SceneSaverBL.ErrIfNotNull);
            filePage.CreateFunction("Upload", Color.white, () => Menu.DisplayDialog("Confirmation", $"Are you sure you want to upload \"{Path.GetFileName(savePath)}\"?", confirmAction: performUpload));
        }, 1);

        Menu.OpenPage(uploadPage);
    }

    // public: to be used in V6 save menus
    public static async Task UploadSave(string savePath)
    {
#if DEBUG
        if (!File.Exists(savePath))
            throw new FileNotFoundException("Save file not found, cannot upload! File: " + savePath);
#endif

        //todo: Quest should have an uploadhandler now? try using that!
        if (Utilities.IsPlatformQuest())
        {
            SetAndSelectError("Uploading not possible on Quest. Blame SLZ for IL2CPP!");
            return;
        }

        Menu.OpenPage(workingPage);

        string filename = Path.GetFileName(savePath);
        byte[] fileBytes = File.ReadAllBytes(savePath);
        string responseTxt = "";

        Exception exception = null;

        try
        {
            HttpClient clint = new();
            HttpRequestMessage req = new(HttpMethod.Put, $"{Prefs.ssblRepo}/api/saves/upload?filename={filename}");
            req.Content = new ByteArrayContent(fileBytes);
            Task<HttpResponseMessage> uploadTask = clint.SendAsync(req);

#if DEBUG
            SceneSaverBL.Log($"Uploading {filename}");
#endif

            while (!uploadTask.IsCompleted)
                await UniTask.Yield();

#if DEBUG
            SceneSaverBL.Log($"Finished uploading {filename}");
#endif
            uploadTask.Result.EnsureSuccessStatusCode();
            Task<string> stringTask = uploadTask.Result.Content.ReadAsStringAsync();
            
            while (!stringTask.IsCompleted)
                await UniTask.Yield();

            responseTxt = stringTask.Result;
            SceneSaverSaveEntry entry = System.Text.Json.JsonSerializer.Deserialize<SceneSaverSaveEntry>(responseTxt)!;


#if DEBUG
            SceneSaverBL.Log($"Loaded JSON for uploaded file. Its tag for this repo is {entry.tag}");
#endif

            GUIUtility.systemCopyBuffer = entry.tag;
            SelectSaveMetadata(entry);
            return;
        }
        catch(AggregateException ae)
        {
            if (ae.InnerExceptions.Count == 1)
                exception = ae.InnerExceptions[0];
            else
                exception = ae;
        }
        catch(Exception e)
        {
            exception = e;
        }

        if (exception is WebException we)
        {
            SceneSaverBL.Error("Exception uploading file to repo: " + we);
            SetAndSelectError("Upload fail: " + we.Message);
        }
        else if (exception is DecoderFallbackException dfe)
        {
            SceneSaverBL.Error("Exception while decoding binary stream into text: " + dfe);
            SetAndSelectError("Decode fail: " + dfe.Message);
        }
        else if (exception is JsonReaderException jre) // JOE ROGAN EXPERIENCE
        {
            SceneSaverBL.Log("Raw response from webserver: " + responseTxt);
            SceneSaverBL.Error("Exception while deserializing uploaded save metadata: " + jre);
            SetAndSelectError("Parse fail: " + jre.Message);
        }
        else
        {
            SceneSaverBL.Error("Exception while uploading save file: " + exception);
            SetAndSelectError("Generic fail: " + exception.Message);
        }
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
        Element element = genericErrorPage.Elements.FirstOrDefault() ?? genericErrorPage.CreateFunction("", Color.white, SaveUtils.NothingAction);
        element.ElementName = text;
        Menu.OpenPage(genericErrorPage);
    }
}
