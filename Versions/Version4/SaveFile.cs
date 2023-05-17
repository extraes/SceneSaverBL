using Cysharp.Threading.Tasks;
using Jevil.Spawning;
using Jevil;
using RootMotion.FinalIK;
using SLZ.Marrow.Pool;
using SLZ.Marrow.SceneStreaming;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using BoneLib.BoneMenu.Elements;
using BoneLib.BoneMenu;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version4;

internal class SaveFile : ISaveFile
{

    string path;
    MenuCategory infoCategory;
    internal string levelBarcode;
    private Texture2D preview;
    private byte[] previewBytes;
    private int objectCount;
    private int constraintCount;
    internal SavedObject[] objects;
    private AssetPoolee[] initializedObjects; // need for constraints
    internal SavedConstraint[] constraints;
    ConstraintTracker[] initializedTrackers;
    Constrainer constrainer;
    Dictionary<Rigidbody, bool> preFreezeKinematics = new(UnityObjectComparer<Rigidbody>.Instance);

    public byte Version => 4;

    public async Task Read(Stream stream)
    {
        byte[] buffer4 = new byte[sizeof(uint)];

        await stream.ReadAsync(buffer4, 0, sizeof(int));
        int levelBarcodeLen = BitConverter.ToInt32(buffer4, 0);
        byte[] levelBarcodeBuff = new byte[levelBarcodeLen];
        await stream.ReadAsync(levelBarcodeBuff, 0, levelBarcodeLen);
        levelBarcode = Encoding.UTF8.GetString(levelBarcodeBuff, 0, levelBarcodeLen);
        SceneSaverBL.Log($"Read {levelBarcodeLen} bytes for level barcode: {levelBarcode}");

        await stream.ReadAsync(buffer4, 0, sizeof(int));
        int imageLen = BitConverter.ToInt32(buffer4, 0);
        previewBytes = new byte[imageLen];
        await stream.ReadAsync(previewBytes, 0, imageLen);
        //stream.Seek(imageLen, SeekOrigin.Current); // skip image - useless to actually read it after selection

        await stream.ReadAsync(buffer4, 0, sizeof(uint));

        objectCount = BitConverter.ToInt32(buffer4, 0);
        objects = new SavedObject[objectCount];
        SceneSaverBL.Log($"Reading {objectCount} objects");

        for (int i = 0; i < objectCount; i++)
            objects[i].Read(stream);

        await stream.ReadAsync(buffer4, 0, sizeof(uint));

        constraintCount = BitConverter.ToInt32(buffer4, 0);
        constraints = new SavedConstraint[constraintCount];
        SceneSaverBL.Log($"Reading {constraintCount} constraints");

        for (int i = 0; i < constraintCount; i++)
            constraints[i].Read(stream);

        SceneSaverBL.Log($"Done reading file data");

#if DEBUG
        if (stream.Length != stream.Position) SceneSaverBL.Warn($"Expected to be done with file, but there is still {stream.Length - stream.Position} bytes left!");
#endif
    }

    public async Task Initialize()
    {
        constrainer = await SaveUtils.GetDummyConstrainer();
        SceneSaverBL.Log("Retrieved dummy constrainer " + constrainer.name);

        initializedObjects = new AssetPoolee[objectCount];
        initializedTrackers = new ConstraintTracker[constraintCount];

        // timeslicing poolee may cause things to clip into/fall through things like tables before the table is intitialized... do i bother ordering poolees by pos y-value when saving?
        SceneSaverBL.Log($"Created variable arrays: {initializedObjects.Length} object(s), {initializedTrackers.Length} constraint(s). Now time-slicing poolee init");
        await AsyncUtilities.ForTimeSlice(0, (int)objectCount, InitializePoolee, Prefs.timeSliceMs);
        SceneSaverBL.Log("Poolee init successful. Now time-slicing constraint init");
        await AsyncUtilities.ForTimeSlice(0, (int)constraintCount, InitializeConstraint, Prefs.timeSliceMs);

        if (Prefs.freezeWhileLoading)
        {
            SceneSaverBL.Log("Constraint init successful. Now time-slicing kinematics reset.");
            await AsyncUtilities.ForEachTimeSlice(preFreezeKinematics, ResetKinematics, Prefs.timeSliceMs);
            preFreezeKinematics.Clear();
        }
    }

    // Making this async void is a poor idea. Oh well? It shouldn't be a problem, its body is try-caught.
    async void InitializePoolee(int idx)
    {
        try
        {
            initializedObjects[idx] = await objects[idx].Initialize();

            if (!Prefs.freezeWhileLoading || initializedObjects[idx] is null) // will return null if the initializing object is SLZ.BONELAB.Core.DefaultPlayerRig (pooled rig manager for modded maps)
                return;

            foreach (Rigidbody rb in initializedObjects[idx].GetComponentsInChildren<Rigidbody>())
            {
                preFreezeKinematics[rb] = rb.isKinematic;
                rb.isKinematic = true;
            }
        }
        catch (Exception e)
        {
            SceneSaverBL.Error(e);
        }
    }

    void InitializeConstraint(int idx)
    {
        constraints[idx].Initialize(initializedObjects, constrainer);
    }

    void ResetKinematics(KeyValuePair<Rigidbody, bool> kvp)
    {
        if (kvp.Key.INOC()) return;
        kvp.Key.isKinematic = kvp.Value;
    }

    public async Task SetFilePath(string path)
    {
        this.path = path;
        using FileStream fs = File.OpenRead(path);
        fs.Seek(5, SeekOrigin.Begin);
        await Read(fs);
    }

    public void PopulateBoneMenu(MenuCategory category)
    {
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Path must be set before populating BoneMenu");
        
        infoCategory = category.CreateCategory("Info: " + Path.GetFileName(path), Color.white);
        category.Elements.Remove(infoCategory);
        SubPanelElement panel = category.CreateSubPanel(Path.GetFileNameWithoutExtension(path), Color.yellow);
        panel.CreateFunctionElement($"Old file (Ver {Version})", Color.yellow, SaveUtils.NothingAction);
        panel.CreateFunctionElement("Open File Menu", Color.white, OpenMenu);
    }

    void OpenMenu()
    {
        if (infoCategory.Elements.Count != 0)
        {
            MenuManager.SelectCategory(infoCategory);
            return;
        }

        try
        {
            if (levelBarcode != SceneStreamer.Session.Level.Barcode.ID)
                infoCategory.CreateFunctionElement("Incorrect level", Color.yellow, SaveUtils.NothingAction);

            infoCategory.CreateFunctionElement(objectCount + " obj(s)", Color.white, SaveUtils.NothingAction);
            infoCategory.CreateFunctionElement("Load", Color.white, MenuLoad);
            infoCategory.CreateFunctionElement("Preview", Color.white, MenuPreview);
            infoCategory.CreateFunctionElement("Delete", Color.red, () => SaveUtils.DeleteSave(path));
            MenuManager.SelectCategory(infoCategory);
        }
        catch (Exception ex)
        {
            SceneSaverBL.Error("Failed to read or open menu of SSBL file " + path + " - " + ex);
        }
    }

    void MenuLoad()
    {
        SceneSaverBL.Log("Requesting save file be initialized...");
        SceneSaverBL.needInitialize.Enqueue(this);
    }

    void MenuPreview()
    {
        AsyncUtilities.WrapNoThrow(MenuPreviewImpl).RunOnFinish(ex => { if (ex != null) SceneSaverBL.Error("Exception while previewing save: " + ex); });
    }

    async Task MenuPreviewImpl()
    {
        if (preview.INOC())
        {
            preview = new Texture2D(2, 2);
            ImageConversion.LoadImage(preview, previewBytes);
        }

        GameObject polaroidBase = await Assets.Prefabs.Polaroid.GetAsync();
        GameObject polaroid = GameObject.Instantiate<GameObject>(polaroidBase);
        (Vector3 pos, Quaternion rot) = SaveUtils.GetIdealMenuPolaroidLocation();
        
        polaroid.transform.SetPositionAndRotation(pos, rot);
        SaveUtils.SetPolaroidTex(polaroid, preview);
        GameObject.Destroy(polaroid, 25); // destroy in 25 sec because ive found that 20 sec doesnt feel like enough time
    }

    public bool ExistsOnDisk()
    {
        return File.Exists(path);
    }

    public Task Construct(AssetPoolee[] poolees, ConstraintTracker[] constraints)
    {
        throw new NotSupportedException("Save files of version " + Version + " cannot be created. Create files of a newer version.");
    }

    public Task Write(Stream stream)
    {
        throw new NotSupportedException("Save files of version " + Version + " cannot be created. Create files of a newer version.");
    }
}
