using Il2CppSLZ.Marrow.Interaction;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedConstraint6 : ISavedConstraint<SavedConstraint6>
{
    // struct size is 4+4+1+8+8 = 25 -> packed to 32 bytes
    //private readonly Vector3 conAnchor;
    public readonly (int, int) DependentOn => (firstTransform.IsDummy ? 0 : firstTransform.pooleeIdx, secondTransform.IsDummy ? 0 : secondTransform.pooleeIdx);

    private byte constraintMode;
    private readonly Constrainer.ConstraintMode ConstraintMode => (Constrainer.ConstraintMode)constraintMode;
    private Vector3 firstPoint; //todo: use these
    private Vector3 secondPoint;
    private SavedHierarchyLocation6 firstTransform;
    private SavedHierarchyLocation6 secondTransform;


    // this really should just be an async method but its "fine" cuz its only called in tasks
    public void Read(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecBuff = rent.Rented;

        constraintMode = (byte)stream.ReadByte();
        
        stream.Read(vecBuff, 0, Const.SizeV3);
        firstPoint = Utilities.DebyteV3(vecBuff);
        stream.Read(vecBuff, 0, Const.SizeV3);
        secondPoint = Utilities.DebyteV3(vecBuff);

        firstTransform.Read(stream);
        secondTransform.Read(stream);
    }

    public void Construct(Poolee[] poolees, ConstraintTracker tracker)
    {
        //conAnchor = tracker.joint.connectedAnchor;
        constraintMode = (byte)SpawnerStates.Constraint.GetModeWhenSpawned(tracker);

        Transform firstT = tracker.attachPoint;
        Transform secondT = tracker.otherTracker.attachPoint;

        // dont want jPt transforms. they wont be there when the object is loaded.
        // could have been done with a ternary operator, but i want to minimize creating managed wrapper objects
        if (firstT.name.StartsWith("jPt"))
            firstT = firstT.parent;
        if (secondT.name.StartsWith("jPt"))
            secondT = secondT.parent;

        SpawnerStates.State constrainerState = SpawnerStates.Constraint.GetStateWhenSpawned(tracker);
        firstPoint = constrainerState.pointStart;
        secondPoint = constrainerState.pointEnd;

        firstTransform.Construct(firstT, new(poolees));
        secondTransform.Construct(secondT, new(poolees));
    }

    public async Task Write(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;
        
        stream.WriteByte(constraintMode);
        
        Utilities.SerializeInPlace(vecArr, firstPoint);
        await stream.WriteAsync(vecArr, 0, vecArr.Length);
        Utilities.SerializeInPlace(vecArr, secondPoint);
        await stream.WriteAsync(vecArr, 0, vecArr.Length);

        await firstTransform.Write(stream);
        await secondTransform.Write(stream);
    }

    public void Initialize(Poolee[] initializedPoolees, Constrainer constrainer)
    {
        HierarchyInitializationContext6 hierCtx = new(initializedPoolees);
        
        Transform tForm1 = firstTransform.Initialize(hierCtx).Result;
        Transform tForm2 = secondTransform.Initialize(hierCtx).Result;

        bool isStaticWeld = firstTransform.IsDummy || secondTransform.IsDummy;

        if (ConstraintMode == Constrainer.ConstraintMode.Weld && isStaticWeld && !Prefs.loadStaticWelds)
        {
            // potentially a slow call because windows console io is slow as all hell
            SceneSaverBL.Log("Ignoring static weld - preferences say to ignore");
            return;
        }

        // now time to effectively paste whatever the fuck SLZ was doing bruh
        CreateTracker(tForm1, tForm2, constrainer);
    }

    private void CreateTracker(Transform host, Transform otherT, Constrainer constrainer)
    {
        MarrowBody? hostBody = Instances<MarrowBody>.Get(host.gameObject);
        MarrowBody? otherBody = Instances<MarrowBody>.Get(otherT.gameObject);
        if (hostBody == null)
        {
            // should ensure hostBody is never null
            // swap things the cool way instead of making temp variables
            (hostBody, otherBody) = (otherBody, hostBody);
            (host, otherT) = (otherT, host);
            // will i need to swap firstpoint and secondpoint?
            //todo: ^^^
        }
        
        constrainer.mode = ConstraintMode;
        constrainer._gO1 = host.gameObject;
        constrainer._gO2 = otherT.gameObject;
        constrainer._mb1 = hostBody; // CANNOT be null else will just bailout
        constrainer._mb2 = otherBody;
        constrainer._point1 = firstPoint;
        constrainer._point2 = secondPoint;
        //todo: make sure these save/load correctly. they may be mismatched (does that matter?)

        constrainer.PrimaryButtonUp();
    }


    public override bool Equals(object obj)
    {
        if (obj is not SavedConstraint6 sc) return false;

        return this == sc;
    }
    public override int GetHashCode() => base.GetHashCode();

    public bool Equals(SavedConstraint6 other)
    {
        return this == other;
    }

    public static bool operator ==(SavedConstraint6 sc1, SavedConstraint6 sc2)
    {
        bool sameVecsMatch = sc1.firstPoint == sc2.firstPoint
                          && sc1.secondPoint == sc2.secondPoint;

        bool diffVecsMatch = sc1.firstPoint == sc2.secondPoint
                          && sc1.secondPoint == sc2.firstPoint;

        return sameVecsMatch || diffVecsMatch;
    }

    public static bool operator !=(SavedConstraint6 sc1, SavedConstraint6 sc2) => !(sc1 == sc2);
}
