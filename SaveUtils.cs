using Il2CppInterop.Runtime.Injection;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow.Warehouse;
using MelonLoader.Utils;
using SceneSaverBL.Interfaces;
using System.Diagnostics;

namespace SceneSaverBL;

file class BoardCrateFilter : Il2CppSystem.Object // , ICrateFilter<SpawnableCrate>
{
    public BoardCrateFilter(IntPtr pointer) : base(pointer) { }
    public BoardCrateFilter() : base(ClassInjector.DerivedConstructorPointer<BoardCrateFilter>())
    {
        ClassInjector.DerivedConstructorBody(this);
    }

#pragma warning disable CA1822 // Mark members as static
    public bool Filter(SpawnableCrate crate)
#pragma warning restore CA1822 // Mark members as static
    {
        return crate.Barcode.ID == SaveUtils.OLD_PLANK_BARCODE || SaveUtils.PlankBarcodesSet.Contains(crate.Barcode.ID);
    }
}

internal static class SaveUtils
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    static SaveUtils()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    {
        var options = new RegisterTypeOptions()
        {
            Interfaces = new Type[] { typeof(ICrateFilter<SpawnableCrate>) }
        };
        ClassInjector.RegisterTypeInIl2Cpp<BoardCrateFilter>(options);
    }

    public const int FORMAT_ID_LEN = 5;
    // todo: plank barcodes were changed to SLZ.BONELAB.Content.Spawnable.WoodPlank{0}, where {0} is replaced by letters A-H
    public const string OLD_PLANK_BARCODE = "c1534c5a-7b2a-41d7-bf2e-af9544657374";
    public static readonly string[] PlankBarcodes = Enumerable.Range('A', 'H' - 'A' + 1).Select(num => $"SLZ.BONELAB.Content.Spawnable.WoodPlank{(char)num}").ToArray();
    public static readonly HashSet<string> PlankBarcodesSet = new(PlankBarcodes);
    private static ICrateFilter<SpawnableCrate> filter;
    public static ICrateFilter<SpawnableCrate> PlankCrateFilter
    {
        get
        {
            if (filter is null || filter.WasCollected || filter == null)
                filter = new(new BoardCrateFilter().Pointer);
            return filter;
        }
    }
    public static HashSet<string> DontSaveTheseBarcodes = new()
    {
        "SLZ.BONELAB.Core.Spawnable.RigManagerBlank",
        "Lakatrazz.FusionContent.Spawnable.BitMart",
    };

    public const string FILE_EXTENSION = "ssbl";
    public static Action NothingAction = () => { };
    public static readonly string PreviewDir = Path.Combine(MelonEnvironment.UserDataDirectory, "SceneSaver", "Previews");

    private static Constrainer constrainer;
    private static BoardGenerator boardGun;
    static readonly byte[] FormatIdStart = { (byte)'S', (byte)'S', (byte)'B', (byte)'L' };

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

        byte[] fileId = new byte[FormatIdStart.Length];
        await stream.ReadAsync(fileId, 0, fileId.Length);
        if (!fileId.SequenceEqual(FormatIdStart))
            throw new InvalidDataException($"File's beginning sequence was not the expected '{Encoding.ASCII.GetString(FormatIdStart)}'. Recieved '{Encoding.ASCII.GetString(fileId)}' instead.");

        return readVersion;
    }

    public static Task WriteIdentifier<TSaveFile>(Stream stream, TSaveFile save) where TSaveFile : ISaveFile
    {
        stream.WriteByte(save.Version);
        return stream.WriteAsync(FormatIdStart, 0, FormatIdStart.Length);
    }

    public static void SkipFormatIdentifier(Stream stream) => stream.Seek(5, SeekOrigin.Begin);

    //public static void DefaultBoneMenuErrored(Page spe, string reason)
    //{
    //    Color pink = new(1, 0.5f, 0.5f);
    //    spe.Color = pink;
    //    spe.CreateFunction("Failed to load!", Color.red, NothingAction);
    //    spe.CreateFunction(reason, Color.red, NothingAction);
    //    spe.CreateFunction("Delete", pink, NothingAction);
    //}

    public static void DefaultBoneMenuErrored(Page mc, string reason)
    {
        Color pink = new(1, 0.5f, 0.5f);
        mc.Color = pink;
        mc.CreateFunction("Failed to load!", Color.red, NothingAction);
        mc.CreateFunction(reason, Color.red, NothingAction);
        mc.CreateFunction("Delete", pink, () => throw new NotImplementedException());
    }

    public static async Task<Constrainer> GetDummyConstrainer()
    {
        if (constrainer == null)
        {
            Spawnable sp = Barcodes.ToSpawnable(JevilBarcode.CONSTRAINER);
#if DEBUG
            SceneSaverBL.Log($"Found spawnable w/ ID {sp.crateRef.Barcode.ID}");
            if (sp is null || sp.WasCollected)
                throw new NullReferenceException("Constrainer spawnable was null or collected -- make sure the barcode is correct!");
#endif
            Pool pool = AssetSpawner._instance._poolList.ToArray().First(p => p._crate == sp.crateRef.Crate);
            var task = Spawnington.SpawnAsyncS(sp, new Vector3(1000, 1000, 1000), Quaternion.identity);
            Poolee poolee = await task;

            SceneSaverBL.Warn($"await unitask = {poolee}, result = {task.Result} explain this liberals");

            if (poolee is null || poolee.WasCollected)
            {
                SceneSaverBL.Warn("Resorting to backup method to find constrainer. Let's get it started (or something else) in here.");
                constrainer = GameObject.FindObjectOfType<Constrainer>();
                SceneSaverBL.Log($"Found constrainer @ {constrainer?.transform.GetFullPath() ?? "null"}");
                return constrainer;
            }

#if DEBUG
            if (poolee is null || poolee.WasCollected)
                throw new NullReferenceException("Constrainer spawned ended up being null or collected -- what the fuck!!!");
#endif
            constrainer = poolee.GetComponent<Constrainer>();
        }

        return constrainer;
    }

    public static async Task<BoardGenerator> GetDummyBoardGun()
    {
        //if (boardGun == null)
        //{
        // force recreate/reset boardgun every time.
        Spawnable sp = Barcodes.ToSpawnable(JevilBarcode.BOARDGUN);
        Poolee poolee = await Spawnington.SpawnAsyncS(sp, new Vector3(1000, 1000, 1000), Quaternion.identity);
        boardGun = poolee?.GetComponent<BoardGenerator>() ?? GameObject.FindObjectOfType<BoardGenerator>(); // HORRIBLE CODE SOMEONE KILL MYSELF
        //}

        return boardGun;
    }

    public static void DeleteSave(string path)
    {
        File.Delete(path);
        Saves.ShowBoneMenu();
        //AsyncUtilities.WrapNoThrow(Saves.ShowBoneMenu).RunOnFinish(SceneSaverBL.ErrIfNotNull);
    }

    public static (Vector3, Quaternion) GetIdealMenuPolaroidLocation()
    {
        PreferencesPanelView menu = Player.UIRig.popUpMenu.preferencesPanelView;
        Vector3 menuPos = menu.transform.position;
        Quaternion menuRot = menu.transform.rotation;
        Vector3 pos = menuPos - 0.5f * (Vector3.up + menu.transform.forward);
        Quaternion rot = Quaternion.Euler(menuRot.eulerAngles + new Vector3(-45, 0, 0));

        return (pos, rot);
    }

    public static void SetPolaroidTex(GameObject anyPolaroid, Texture2D newTex)
    {
        //todo: switch to GlassHandler
        SecurityCamera referenceSmuggler = Instances<SecurityCamera>.Get(anyPolaroid) ?? throw new NullReferenceException("Reference smuggler not found - passing in the wrong polaroid prefab?");
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

        for (int i = 0; i < getChildrenOf.childCount; i++)
        {
            Transform child = getChildrenOf.GetChild(i);

            if (trackWith.ElapsedMilliseconds > ConfigVars.timeSliceMs)
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
        int lenOfResArr; // length of RESULTING ARRAY, not length of READ BYTES
        switch (sizeOfLengthType)
        {
            case 1:
                lenOfResArr = readFrom.ReadByte();
                break;
            case 2:
                byte[] ushBuf = new byte[sizeOfLengthType];
                await readFrom.ReadAsync(ushBuf, 0, sizeOfLengthType);
                lenOfResArr = BitConverter.ToUInt16(ushBuf, 0);
                break;
            case 4:
                byte[] intBuf = new byte[sizeOfLengthType];
                await readFrom.ReadAsync(intBuf, 0, sizeOfLengthType);
                lenOfResArr = BitConverter.ToInt32(intBuf, 0);
                break;
            //case 8:
            //    byte[] lonBuf = new byte[sizeOfLengthType];
            //    await readFrom.ReadAsync(lonBuf, 0, sizeOfLengthType);
            //    lenOfResArr = BitConverter.ToInt64(lonBuf, 0);
            //    break;
            default:
                throw new ArgumentException($"Invalid length type. What type is {sizeOfLengthType} bytes long?", nameof(sizeOfLengthType));
        }

#if DEBUG
        SceneSaverBL.Log($"Reading a {typeof(T).FullName} array of length {lenOfResArr}. The index was stored as a {sizeOfLengthType}-byte long number.");
#endif

        int byteCount = lenOfResArr * SizeOf<T>();
        byte[] bytes = new byte[byteCount];
        T[] ret;
        await readFrom.ReadAsync(bytes, 0, bytes.Length);
        ret = QuickConvert<byte, T>(bytes);

        return ret;
    }

    /// <param name="sizeOfLengthType">The size of the type to use for the length.
    /// <br/>1 will use a <see langword="byte"/>, 2 a <see langword="ushort"/>, and 4 an <see langword="int"/>.
    /// <br/>These dictate the maximum lengths of the array that can be written.
    /// </param>
    public static async Task WriteArrayAsync<T>(Stream writeTo, T[] arr, int sizeOfLengthType = 4) where T : unmanaged
    {
        switch (sizeOfLengthType)
        {
            case 1:
#if DEBUG
                if (arr.Length > byte.MaxValue)
                    throw new ArgumentException($"Array length {arr.Length} is greater than the maximum length of a byte ({byte.MaxValue}) Use a longer length type");
#endif
                writeTo.WriteByte((byte)arr.Length);
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


        byte[] serializedData = QuickConvert<T, byte>(arr);
        await writeTo.WriteAsync(serializedData);
    }

    internal static unsafe TOut[] QuickConvert<TIn, TOut>(TIn[] input) 
        where TIn : unmanaged 
        where TOut : unmanaged
    {
        int totalBytes = input.Length * sizeof(TIn);
        TOut[] output = new TOut[totalBytes / sizeof(TOut)];

#if DEBUG
        if (output.Length * sizeof(TOut) != totalBytes)
        {
            SceneSaverBL.Warn($"I got $20 says this shit dies: output array size will be {totalBytes / sizeof(TOut)} elements long, but that's not right, it should be {totalBytes / (float)sizeof(TOut)} elements - a floating point value. If shit died after this, let them know it was because of that they/them pussy.");
        }
#endif

        fixed (void* srcPtr = input)
        fixed (void* dstPtr = output)
        {
            Buffer.MemoryCopy(srcPtr, dstPtr, totalBytes, totalBytes);
        }

#if DEBUG
        if (output.Length * sizeof(TOut) != totalBytes)
        {
            // it didnt die but i  still want them to know
            throw new ArgumentException("THAT THEY/THEM PUSSY  GOT ME ACTIN UNWISE MAKIN MY DICK JUMP N SHI 🥶🥶🥶🥶🥶🥶");
        }
#endif

        return output;
    }

    // because the compilergenerated one kills itself when called off main thread or some shit i dont give enough a fuck
    public static string ToStr(Vector3 v3)
    {
        return $"({v3.x}, {v3.y}, {v3.z})";
    }

    // peak frankly
    private static unsafe int SizeOf<T>() where T : unmanaged
    {
        return sizeof(T);
    }

    /// <returns><see langword="null"/> if not a plank.
    /// <br/><see langword="true"/> if it's a plank saved after patch 6(? likely to be patch 4 or 5 too), and <see langword="false"/> if it's a plank saved before then</returns>
    public static bool? IsNewPlank(string barcode)
    {
        if (barcode == OLD_PLANK_BARCODE)
            return false;
        else if (PlankBarcodesSet.Contains(barcode))
            return true;

        return null;
    }
}