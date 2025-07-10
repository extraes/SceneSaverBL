using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow.Interaction;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedPlank6 : ISerializableStruct<SavedPlank6>, IContextfulSavedObject<SavedPlank6, ObjectDestructible, PlankInitializationContext6>
{
    static readonly Il2CppSystem.Collections.Generic.List<ConfigurableJoint> JointList = new(2);
    public readonly (int, int) DependsOnPoolees => (startT.IsDummy ? 0 : startT.pooleeIdx, endT.IsDummy ? 0 : endT.pooleeIdx);

    Vector3 startPos;
    Vector3 endPos;
    Vector3 upDir;
    SavedHierarchyLocation6 startT;
    SavedHierarchyLocation6 endT;

    public void Construct(ObjectDestructible save, PlankInitializationContext6 ctx)
    {
        var state = SpawnerStates.Board.GetStateWhenSpawned(save);
        startPos = state.pointStart;
        endPos = state.pointEnd;

        HierarchyInitializationContext6 hierCtx = new(ctx.poolees);

        // use IL2CPP list to avoid constantly creating and copying arrays.
        JointList.Clear();
        save.GetComponents(JointList);

        ConfigurableJoint joint1 = JointList[0];
        ConfigurableJoint joint2 = JointList.Count == 2 ? JointList[1] : null;

        startT.Construct(joint1.connectedBody?.transform, hierCtx);
        endT.Construct(joint2?.connectedBody?.transform, hierCtx);
    }

    public bool Equals(SavedPlank6 other)
    {
        // take the classic shortcut: only compare vectors
        bool sameVecMatch = startPos == other.startPos
                         && endPos == other.endPos;

        bool diffVecMatch = startPos == other.endPos
                         && endPos == other.startPos;

        return sameVecMatch || diffVecMatch;
    }

    public async Task<ObjectDestructible> Initialize(PlankInitializationContext6 ctx)
    {
        Transform dummy = new GameObject($"{nameof(SavedPlank6)} dummy transform").transform;

        //bool bothNotDummy = startT.IsDummy && endT.IsDummy;
        
        //if (bothNotDummy)
        //{
        //    GameObject go = new($"{nameof(SavedPlank6)} dummy for ");
        //    CreateBoard(tForm1, tForm2, ctx.boardGun, ctx.worldspaceOffset);
        //}

        // now time to effectively paste whatever the fuck SLZ was doing bruh
        CreateBoard(dummy, dummy, ctx.boardGun, ctx.worldspaceOffset);
        GameObject board = await SpawnerStates.Board.WaitForAnyBoard();
        return Instances<ObjectDestructible>.Get(board);
    }

    public async Task PostInitialize(ObjectDestructible plank, PlankInitializationContext6 ctx)
    {
        if (startT.IsDummy && endT.IsDummy)
            return;

        HierarchyInitializationContext6 hierCtx = new(ctx.poolees);

        Transform tForm1 = startT.IsDummy ? null : await startT.Initialize(hierCtx);
        Transform tForm2 = endT.IsDummy ? null : await endT.Initialize(hierCtx);

        Rigidbody rb1 = startT.IsDummy ? null : Instances<Rigidbody>.Get(tForm1.gameObject);
        Rigidbody rb2 = endT.IsDummy ? null : Instances<Rigidbody>.Get(tForm2.gameObject);

        if (tForm1 == null)
        {
            (tForm1, tForm2) = (tForm2, tForm1);
            (rb1, rb2) = (rb2, rb1);
        }

        for(int i = 0; i < 5; i++) // wait a max of 5 frames
        {
            JointList.Clear();
            plank.GetComponents(JointList);
            if (JointList.Count > 0) // joints are set, can leave loop
                break;

            await UniTask.Yield();
        }

        if (JointList.Count == 0)
        {
            SceneSaverBL.Log($"No joints found after 5 frames for plank {plank.transform.GetFullPath()}");
            return;
        }

        ConfigurableJoint joint1 = JointList[0];
        ConfigurableJoint? joint2 = JointList.Count == 2 ? JointList[1] : null;

#if DEBUG
        SceneSaverBL.Log($"Setting rigidbodies on joint{(joint2 ? "s" : "")} for plank {plank.transform.GetFullPath()}");
        if (rb1 != null)
            SceneSaverBL.Log($" - rb1 = {rb1.transform.GetFullPath()}");
        if (rb2 != null)
            SceneSaverBL.Log($" - rb2 = {rb2.transform.GetFullPath()}");
#endif

        if (rb1 != null)
            joint1.connectedBody = rb1;
        if (joint2 != null && rb2 != null)
            joint2.connectedBody = rb2;
    }

    public void Read(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;

#if DEBUG
        SceneSaverBL.Log("SavedPlank6 Read: Starting @ pos " + stream.Position);
#endif

        stream.Read(vecArr, 0, vecArr.Length);
        startPos = Utilities.DebyteV3(vecArr);
        stream.Read(vecArr, 0, vecArr.Length);
        endPos = Utilities.DebyteV3(vecArr);
        stream.Read(vecArr, 0, vecArr.Length);
        upDir = Utilities.DebyteV3(vecArr);

        startT.Read(stream);
        endT.Read(stream);

#if DEBUG
        SceneSaverBL.Log("SavedPlank6 Read: Ended @ pos " + stream.Position);
#endif
    }

    public async Task Write(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;

#if DEBUG
        SceneSaverBL.Log("SavedPlank6 Write: Starting @ pos " + stream.Position);
#endif

        Utilities.SerializeInPlace(vecArr, startPos);
        await stream.WriteAsync(vecArr, 0, vecArr.Length);
        Utilities.SerializeInPlace(vecArr, endPos);
        await stream.WriteAsync(vecArr, 0, vecArr.Length);
        Utilities.SerializeInPlace(vecArr, upDir);
        await stream.WriteAsync(vecArr, 0, vecArr.Length);

        await startT.Write(stream);
        await endT.Write(stream);

#if DEBUG
        SceneSaverBL.Log("SavedPlank6 Write: Ended @ pos " + stream.Position);
#endif
    }

    private void CreateBoard(Transform t1, Transform t2, BoardGenerator boardGun, Vector3 wsOffset)
    {
#if DEBUG
        SceneSaverBL.Log($"Creating board @ host {t1.GetFullPath()} to otherBody @ {t2.GetFullPath()} using boardgun {boardGun?.name ?? "NULL"} with dupe offset of {wsOffset}");
#endif
        Rigidbody? hostBody = Instances<Rigidbody>.Get(t1.gameObject);
        Rigidbody? otherBody = Instances<Rigidbody>.Get(t2.gameObject);
        if (hostBody == null)
        {
            // should ensure that theres never a case of hostbody being null while otherbody isnt
            // swap things the cool way instead of making temp variables
            (hostBody, otherBody) = (otherBody!, hostBody);
            (t1, t2) = (t2, t1);
        }

        bool wasUsingAmmo = boardGun.isUsingAmmo;
        boardGun.isUsingAmmo = false;
        
        boardGun.FirstRb = hostBody == null ? null : MarrowBody.Cache.Get(hostBody.gameObject);
        boardGun.EndRb = otherBody == null ? null : MarrowBody.Cache.Get(otherBody.gameObject);
        boardGun.firstPoint = startPos + wsOffset;
        boardGun.EndPoint = endPos + wsOffset;
        boardGun.upDir = upDir;
        
        // boardspawner's parameters are now gone... did SLZ hear me through my IDE?
        boardGun.BoardSpawnerAsync();

        boardGun.isUsingAmmo = wasUsingAmmo;
    }
}
