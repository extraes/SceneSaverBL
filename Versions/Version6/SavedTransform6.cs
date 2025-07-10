using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedTransform6 : IContextfulSavedObject<SavedTransform6, Transform, TransformInitializationContext6>
{
    // 12 + 12 + 12 + 8 = 44 -> packed to 64 bytes
    private Vector3 localPos;
    private Vector3 scale;
    private Vector3 localRotation;
    private SavedHierarchyLocation6 hierarchyLoc;

    public readonly Vector3 LocalPosition => localPos;
    public readonly Quaternion Rotation => Quaternion.Euler(localRotation);

    static byte[] vector3Buffer = new byte[Const.SizeV3];
    static List<int> sharedIntList = new(10); // use one list so i can just toarray for each transform so im not overallocating locally

    // cannot be async - async methods cannot modify their original instances
    public void Read(Stream stream)
    {
        Vector3 readPos;
        Vector3 readScale;
        Vector3 readRot;
        byte[] buffer = vector3Buffer;

        stream.Read(buffer, 0, Const.SizeV3);
        readPos = Utilities.DebyteV3(buffer, 0);

        stream.Read(buffer, 0, Const.SizeV3);
        readScale = Utilities.DebyteV3(buffer, 0);

        stream.Read(buffer, 0, Const.SizeV3);
        readRot = Utilities.DebyteV3(buffer, 0);

        localPos = readPos;
        localRotation = readRot;
        scale = readScale;

        hierarchyLoc.Read(stream);

#if DEBUG
        SceneSaverBL.Log($"Read: " + ToString());
        SaveChecks.ThrowIfInvalid(localPos);
        SaveChecks.ThrowIfInvalid(localRotation);
        SaveChecks.ThrowIfInvalid(scale);
#endif
    }

    public async Task Write(Stream stream)
    {
        byte[] buffer = vector3Buffer;

        Utilities.SerializeInPlace(buffer, localPos);
        await stream.WriteAsync(buffer, 0, Const.SizeV3);
        
        Utilities.SerializeInPlace(buffer, scale);
        await stream.WriteAsync(buffer, 0, Const.SizeV3);
        
        Utilities.SerializeInPlace(buffer, localRotation);
        await stream.WriteAsync(buffer, 0, Const.SizeV3);

        await hierarchyLoc.Write(stream);

#if DEBUG
        SceneSaverBL.Log("Wrote " + ToString());
#endif
    }

    public async Task<Transform> Initialize(TransformInitializationContext6 context)
    {
        HierarchyInitializationContext6 hic = new(context.poolees);

        Transform targetTransform = await hierarchyLoc.Initialize(hic);

        // use localposition because it will likely have lower average values, meaning (insignificantly) more precise floats
        if (targetTransform.root == targetTransform)
            targetTransform.localPosition = LocalPosition + context.worldspaceRootOffset;
        else
            targetTransform.localPosition = LocalPosition;
        targetTransform.localRotation = Rotation;
        targetTransform.localScale = scale;

#if DEBUG
        SceneSaverBL.Log($"Transform '{targetTransform.name}' LocalPosition set to: {targetTransform.localPosition} (Deserialized pos was {SaveUtils.ToStr(localPos)})");
        float dist = Vector3.Distance(localPos, targetTransform.localPosition);
        if (targetTransform.root != targetTransform && context.worldspaceRootOffset != default && dist > 0.1f)
            SceneSaverBL.Warn($"!!! THIS IS {dist} METERS AWAY FROM SERIALIZED POSITION!!! SPOS: {SaveUtils.ToStr(localPos)}");
        SaveChecks.ThrowIfInvalid(targetTransform.localPosition);
        SaveChecks.ThrowIfInvalid(targetTransform.position);
#endif

        return targetTransform;
    }

    public bool Equals(SavedTransform6 other)
    {
        //return other.children == this.children 
        return other.localPos == this.localPos
            && other.localRotation == this.localRotation
            && other.scale == this.scale;
    }

    public override readonly string ToString()
    {
        // use SaveUtils.ToStr to avoid having to use Il2CppThreadScope because the default ToString from IL2CPP allocates from the IL2CPP domain, which throws a shitfit on off-main threads.
        return $"SSBL Transform V6 - LPos = {SaveUtils.ToStr(localPos)}; LRot (euler) = {SaveUtils.ToStr(localRotation)}; Scale = {SaveUtils.ToStr(scale)}";
    }

    public void Construct(Transform sourceTransform, TransformInitializationContext6 ctx)
    {
        localPos = sourceTransform.transform.localPosition;
        scale = sourceTransform.transform.localScale;
        localRotation = sourceTransform.transform.localRotation.eulerAngles;

        HierarchyInitializationContext6 hic = new(ctx.poolees);
        hierarchyLoc.Construct(sourceTransform, hic);

#if DEBUG
        SaveChecks.ThrowIfInvalid(localPos);
        SaveChecks.ThrowIfInvalid(localRotation);
        SaveChecks.ThrowIfInvalid(scale);
#endif
    }
}
