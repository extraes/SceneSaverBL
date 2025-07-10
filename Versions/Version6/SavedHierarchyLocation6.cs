using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

//todo: see if saving names works better than just saving indices
internal struct SavedHierarchyLocation6 : IContextfulSavedObject<SavedHierarchyLocation6, Transform, HierarchyInitializationContext6>
{
    public const int NO_POOLEE = int.MaxValue;
    public readonly bool IsDummy => pooleeIdx == NO_POOLEE;

    public int pooleeIdx;
    byte[] siblingIndices;

    public void Construct(Transform save, HierarchyInitializationContext6 ctx)
    {
#if DEBUG
        //if (save.IsChildOf(ctx.targetTransform))
        //    throw new InvalidOperationException($"Transform {save.name} is not a child of {ctx.targetTransform.GetFullPath()}");
#endif
        if (save is null)
        {
            pooleeIdx = NO_POOLEE;
            return;
        }

        int? idx = GetPooleeIdx(ctx.poolees, save);

        if (idx.HasValue)
        {
            pooleeIdx = idx.Value;
            siblingIndices = GetSiblingIndices(save);
        }
        else
            pooleeIdx = NO_POOLEE;
    }

    public readonly bool Equals(SavedHierarchyLocation6 other)
    {
        return other.siblingIndices.Length == this.siblingIndices.Length
            && other.siblingIndices.Sum(x => x) == this.siblingIndices.Sum(x => x);
    }

    public Task<Transform> Initialize(HierarchyInitializationContext6 ctx)
    {
        if (IsDummy)
            return Task.FromResult(GetDummyTransform());

        if (ctx.poolees[pooleeIdx] is null)
        {
            SceneSaverBL.Warn($"Poolee (idx {pooleeIdx}) was null when trying to initialize SavedHierarchyLocation6");
            return Task.FromResult(GetDummyTransform());
        }

        Transform root = ctx.poolees[pooleeIdx].transform;
        Transform target = TraverseHierarchy(root, siblingIndices);
        return Task.FromResult(target);
    }

    public void Read(Stream stream)
    {
        using var apt = ByteArrayPool.RentVectorTemp();
        byte[] arr = apt.Rented;

#if DEBUG
        SceneSaverBL.Log("SavedHierarchyLocation6 Read: Starting @ pos " + stream.Position);
#endif

        stream.Read(arr, 0, sizeof(int));
        pooleeIdx = BitConverter.ToInt32(arr, 0);

        if (IsDummy) return;

        stream.Read(arr, 0, sizeof(ushort));
        siblingIndices = new byte[BitConverter.ToUInt16(arr, 0)];
        stream.Read(siblingIndices, 0, siblingIndices.Length);

#if DEBUG
        SceneSaverBL.Log("SavedHierarchyLocation6 Read: Ended @ pos " + stream.Position);
#endif
    }

    public async Task Write(Stream stream)
    {
        using var apt = ByteArrayPool.RentVectorTemp();
        byte[] arr = apt.Rented;

#if DEBUG
        SceneSaverBL.Log("SavedHierarchyLocation6 Write: Starting @ pos " + stream.Position);
#endif

        Utilities.SerializeInPlace(arr, pooleeIdx);
        await stream.WriteAsync(arr, 0, sizeof(int));

        if (IsDummy)
        {
            return;
        }

        Utilities.SerializeInPlace(arr, (ushort)(siblingIndices?.Length ?? 0));

        await stream.WriteAsync(arr, 0, sizeof(ushort));

        await stream.WriteAsync(siblingIndices);
#if DEBUG
        SceneSaverBL.Log("SavedHierarchyLocation6 Write: Ended @ pos " + stream.Position);
#endif
    }

    static byte[] GetSiblingIndices(Transform saveT)
    {
        // precalculate depth to avoid overallocating
        byte[] ret;

        int depth = 0;
        Transform depthT = saveT;
        while (depthT.parent != null)
        {
            depth++;
            depthT = depthT.parent;
        }

        ret = new byte[depth];

        // do in reverse so when being loaded can use a normal for loop
        for (int i = depth - 1; i >= 0; i--)
        {
            ret[i] = (byte)saveT.GetSiblingIndex();
            saveT = saveT.parent;
        }

        return ret;
    }

    static Transform TraverseHierarchy(Transform transform, byte[] childIdxs)
    {
        Transform ret = transform;
        foreach (byte idx in childIdxs)
        {
#if DEBUG
            SceneSaverBL.Log($"Getting child {idx} of transform {ret.name}");
#endif
            ret = ret.GetChild(idx);
        }

        return ret;
    }

    static int? GetPooleeIdx(Poolee[] poolees, Transform t)
    {
#if DEBUG
        string originalPath = t.GetFullPath();
#endif
        t = t.root; // Poolee component only appears on targetTransform gameobject

        for (int i = 0; i < poolees.LongLength; i++)
            if (poolees[i].transform == t) return i;

#if DEBUG
        SceneSaverBL.Warn("Transform was not found in list of saved Poolees! Path: " + originalPath);
#endif

        return null;
    }

    static Transform? dummyTransform;
    static Transform GetDummyTransform()
    {
        if (dummyTransform == null)
        {
            GameObject go = new("SSBL Dummy transform");
            dummyTransform = go.transform;
        }
        return dummyTransform;
    }
}
