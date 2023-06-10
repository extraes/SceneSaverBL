using BoneLib.BoneMenu.Elements;
using Cysharp.Threading.Tasks;
using Jevil;
using Jevil.Spawning;
using SceneSaverBL.Interfaces;
using SLZ.Bonelab;
using SLZ.Marrow.Data;
using SLZ.Marrow.Pool;
using SLZ.Props;
using SLZ.UI;
using SLZ.VRMK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL;

internal static class SaveUtils
{
    public const int FORMAT_ID_LEN = 5; 
    public static Action NothingAction = () => { };

    private static Constrainer constrainer;
    static readonly byte[] FormatId = { (byte)'S', (byte)'S', (byte)'B', (byte)'L' };

    public static void CleanTrackers<T>(ref T[] constraints) where T : struct, ISavedConstraint<T>
    {
        T[] oldConstraints = constraints;
        int usedTrackers = constraints.Count(sc => !sc.Equals(default));
        T[] newConstraints = new T[usedTrackers];

        int nextCopyIdx = 0;
        for (int i = 0; i < oldConstraints.Length; i++)
        {
            if (oldConstraints[i].Equals(default)) continue;

            newConstraints[nextCopyIdx] = oldConstraints[i];
            nextCopyIdx++;
        }
        // linq is wasteful yes but this is significantly more readable than manually ordering, and saving is already an expensive operation, but this pales in comparison to the spike from screenshotting
        // its better to do this once when saving instead of doing it every time when loading. savedconstraints are not dependent on order in array
        constraints = newConstraints.OrderBy(sc => Math.Max(sc.DependentOn.Item1, sc.DependentOn.Item2)).ToArray();
#if DEBUG
        SceneSaverBL.Log($"Shrunk savedcontraints from {oldConstraints.Length} to {newConstraints.Length}");
#endif
    }

    public static async Task<byte> CheckFormatIdentifier(Stream stream)
    {
        byte readVersion = (byte)stream.ReadByte();

        byte[] fileId = new byte[FormatId.Length];
        await stream.ReadAsync(fileId, 0, fileId.Length);
        if (!fileId.SequenceEqual(FormatId))
            throw new InvalidDataException($"File's beginning sequence was not the expected '{Encoding.ASCII.GetString(FormatId)}'. Recieved '{Encoding.ASCII.GetString(fileId)}' instead.");

        return readVersion;
    }

    public static Task WriteIdentifier<TSaveFile>(Stream stream, TSaveFile save) where TSaveFile : ISaveFile
    {
        stream.WriteByte(save.Version);
        return stream.WriteAsync(FormatId, 0, FormatId.Length);
    }

    public static void SkipFormatIdentifier(Stream stream) => stream.Seek(5, SeekOrigin.Begin);

    public static void DefaultBoneMenuErrored(SubPanelElement spe, string reason)
    {
        Color pink = new(1, 0.5f, 0.5f);
        spe.SetColor(pink);
        spe.CreateFunctionElement("Failed to load!", Color.red, NothingAction);
        spe.CreateFunctionElement(reason, Color.red, NothingAction);
        spe.CreateFunctionElement("Delete", pink, NothingAction);
    }

    public static void DefaultBoneMenuErrored(MenuCategory mc, string reason)
    {
        Color pink = new(1, 0.5f, 0.5f);
        mc.SetColor(pink);
        mc.CreateFunctionElement("Failed to load!", Color.red, NothingAction);
        mc.CreateFunctionElement(reason, Color.red, NothingAction);
        mc.CreateFunctionElement("Delete", pink, NothingAction);
    }

    public static async Task<Constrainer> GetDummyConstrainer()
    {
        if (constrainer.INOC())
        {
            Spawnable sp = Barcodes.ToSpawnable(JevilBarcode.CONSTRAINER);
            AssetPoolee poolee = await sp.SpawnAsync(new Vector3(1000, 1000, 1000), Quaternion.identity);
            constrainer = poolee.GetComponent<Constrainer>();
        }

        return constrainer;
    }

    public static void DeleteSave(string path)
    {
        File.Delete(path);
        Saves.ShowBoneMenu();
        //AsyncUtilities.WrapNoThrow(Saves.ShowBoneMenu).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    public static (Vector3, Quaternion) GetIdealMenuPolaroidLocation()
    {
        PreferencesPanelView menu = Instances.Player_RigManager.uiRig.popUpMenu.preferencesPanelView;
        Vector3 menuPos = menu.transform.position;
        Quaternion menuRot = menu.transform.rotation;
        Vector3 pos = menuPos - 0.5f * (Vector3.up + menu.transform.forward);
        Quaternion rot = Quaternion.Euler(menuRot.eulerAngles + new Vector3(-45, 0, 0));

        return (pos, rot);
    }

    public static void SetPolaroidTex(GameObject anyPolaroid, Texture2D newTex)
    {
        SaveIndication referenceSmuggler = Instances<SaveIndication>.Get(anyPolaroid) ?? throw new NullReferenceException("Reference smuggler not found - passing in the wrong polaroid prefab?");
        referenceSmuggler.material.SetTexture(Const.UrpLitMainTexID, newTex);
    }

    // walk hierarchy in a deterministic way. I'd use GetComponentsInChildren if i could, but id have to post-process its results to order them in a predictable way
    public static List<Transform> WalkHierarchy(Transform getChildrenOf, List<Transform> appendTo = null)
    {
#if DEBUG
        Stopwatch sw = Stopwatch.StartNew();
#endif
        List<Transform> children = appendTo ?? new();
        WalkHierarchyImpl(getChildrenOf, children);
#if DEBUG
        SceneSaverBL.Log($"walked hierarchy of {getChildrenOf.name} in {sw.ElapsedMilliseconds}ms");
#endif
        return children;
    }

    public static void WalkHierarchyImpl(Transform getChildrenOf, List<Transform> populate)
    {
        populate.Add(getChildrenOf);
        //if (getChildrenOf.childCount == 0) return;

        for (int i = 0; i < getChildrenOf.childCount; i++)
        {
            Transform child = getChildrenOf.GetChild(i);
            if (!SaveChecks.IsTransformIgnored(child))
                WalkHierarchyImpl(child, populate);
        }
    }

    public static async Task<List<Transform>> WalkHierarchyAsync(Transform getChildrenOf)
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<Transform> children = new();
        await WalkHierarchyAsyncImpl(getChildrenOf, children, sw);
        return children;
    }

    public static async Task WalkHierarchyAsyncImpl(Transform getChildrenOf, List<Transform> populate, Stopwatch trackWith)
    {
        populate.Add(getChildrenOf);
        //if (getChildrenOf.childCount == 0) return;

        for (int i = 0; i < getChildrenOf.childCount; i++)
        {
            Transform child = getChildrenOf.GetChild(i);

            if (trackWith.ElapsedMilliseconds > Prefs.timeSliceMs)
            {
                await UniTask.Yield();
                trackWith.Restart();
            }

            if (!SaveChecks.IsTransformIgnored(child))
                await WalkHierarchyAsyncImpl(child, populate, trackWith);
        }
    }

    public static async Task<T[]> ReadArrayAsync<T>(Stream readFrom, int sizeOfLengthType) where T : unmanaged
    {
        int len;
        switch (sizeOfLengthType)
        {
            case 1:
                len = readFrom.ReadByte();
                break;
            case 2:
                byte[] ushBuf = new byte[sizeOfLengthType];
                await readFrom.ReadAsync(ushBuf, 0, sizeOfLengthType);
                len = BitConverter.ToUInt16(ushBuf, 0);
                break;
            case 4:
                byte[] intBuf = new byte[sizeOfLengthType];
                await readFrom.ReadAsync(intBuf, 0, sizeOfLengthType);
                len = BitConverter.ToInt32(intBuf, 0);
                break;
            //case 8:
            //    byte[] lonBuf = new byte[sizeOfLengthType];
            //    await readFrom.ReadAsync(lonBuf, 0, sizeOfLengthType);
            //    len = BitConverter.ToInt64(lonBuf, 0);
            //    break;
            default:
                throw new ArgumentException($"Invalid length type. What type is {sizeOfLengthType} bytes long?", nameof(sizeOfLengthType));
        }

        byte[] bytes = new byte[len];
        T[] ret;
        await readFrom.ReadAsync(bytes, 0, len);
        ret = ConvertUsingSpans<byte, T>(bytes);

        return ret;
    }

    public static async Task WriteArrayAsync<T>(Stream writeTo, T[] arr, int sizeOfLengthType = 4) where T : unmanaged
    {
        switch (sizeOfLengthType)
        {
            case 1:
                writeTo.ReadByte();
                break;
            case 2:
                byte[] lenUsh = new byte[2];
                Utilities.SerializeInPlace(lenUsh, (ushort)arr.Length);
                await writeTo.WriteAsync(lenUsh);
                break;
            case 4:
                byte[] lenInt = new byte[4];
                Utilities.SerializeInPlace(lenInt, (int)arr.Length);
                await writeTo.WriteAsync(lenInt);
                break;
            default:
                throw new ArgumentException($"Invalid length type. What type is {sizeOfLengthType} bytes long?", nameof(sizeOfLengthType));
        }

        byte[] serializedData = ConvertUsingSpans<T, byte>(arr);
        await writeTo.WriteAsync(serializedData);
    }

    internal static TOut[] ConvertUsingSpans<TIn, TOut>(TIn[] input) 
        where TIn : unmanaged 
        where TOut : unmanaged
    {
        Span<TIn> inSpan = new(input);
        Span<TOut> outSpan = MemoryMarshal.Cast<TIn, TOut>(inSpan);
        return outSpan.ToArray();
    }

    // because the compilergenerated one kills itself when called off main thread or some shit i dont give enough a fuck
    public static string ToStr(Vector3 v3)
    {
        return $"({v3.x}, {v3.y}, {v3.z})";
    }
}