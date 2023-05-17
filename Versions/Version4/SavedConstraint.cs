using System.IO;
using Jevil;
using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using SLZ.SaveData;
using Cysharp.Threading.Tasks;
using System.Runtime.InteropServices;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version4;

internal struct SavedConstraint : ISavedConstraint<SavedConstraint>
{
    // struct size is 4+4+1+8+8 = 25 -> packed to 32 bytes
    //private readonly Vector3 conAnchor;
    public (int, int) DependentOn => (firstObjectIndex, secondObjectIndex);

    private int firstObjectIndex;
    private int secondObjectIndex;
    private byte constraintMode;
    public byte[] childIndicesToFirst;
    public byte[] childIndicesToSecond;

    public Constrainer.ConstraintMode ConstraintMode => (Constrainer.ConstraintMode)constraintMode;

    public void Read(Stream stream)
    {
        byte arrLen;
        byte[] buffer4 = new byte[sizeof(uint)];

        constraintMode = (byte)stream.ReadByte();
        stream.Read(buffer4, 0, sizeof(uint));
        firstObjectIndex = BitConverter.ToInt32(buffer4, 0);
        stream.Read(buffer4, 0, sizeof(uint));
        secondObjectIndex = BitConverter.ToInt32(buffer4, 0);

        arrLen = (byte)stream.ReadByte();
        childIndicesToFirst = new byte[arrLen];
        arrLen = (byte)stream.ReadByte();
        childIndicesToSecond = new byte[arrLen];

        stream.Read(childIndicesToFirst, 0, childIndicesToFirst.Length);
        stream.Read(childIndicesToSecond, 0, childIndicesToSecond.Length);
    }

    public void Construct(AssetPoolee[] poolees, ConstraintTracker tracker)
    {
        //conAnchor = tracker.joint.connectedAnchor;
        constraintMode = (byte)tracker.mode;

        int? idx1 = GetPooleeIdx(poolees, tracker.attachPoint);
        int? idx2 = GetPooleeIdx(poolees, tracker.otherTracker.attachPoint);

#if DEBUG
        SceneSaverBL.Log($"Saving constraint for poolees with indices {idx1?.ToString() ?? "NULL"} & {idx2?.ToString() ?? "NULL"}");
#endif
        if (tracker.mode != Constrainer.ConstraintMode.Weld && (!idx1.HasValue || !idx2.HasValue))
        {
            throw new ArgumentException($"Transform(s) PATH1={tracker.attachPoint.GetFullPath()} PATH2={tracker.otherTracker.attachPoint.GetFullPath()} are not contained in the given list of poolees.");
        }

        firstObjectIndex = idx1 ?? int.MaxValue;
        secondObjectIndex = idx2 ?? int.MaxValue;

        childIndicesToFirst = idx1.HasValue ? GetIndicesFromParent(tracker.attachPoint) : Array.Empty<byte>();
        childIndicesToSecond = idx2.HasValue ? GetIndicesFromParent(tracker.otherTracker.attachPoint) : Array.Empty<byte>();

        if (childIndicesToFirst.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException($"There are more child indices than can be serialized. Tell the developer to switch to using 'ushort' for tracking. Full path below.\n{tracker.attachPoint.transform.GetFullPath()}");
        if (childIndicesToSecond.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException($"There are more child indices than can be serialized. Tell the developer to switch to using 'ushort' for tracking. Full path below.\n{tracker.otherTracker.attachPoint.transform.GetFullPath()}");
    }

    public async Task Write(Stream stream)
    {
        stream.WriteByte(constraintMode);
        await stream.WriteAsync(BitConverter.GetBytes(firstObjectIndex), 0, sizeof(uint));
        await stream.WriteAsync(BitConverter.GetBytes(secondObjectIndex), 0, sizeof(uint));
        stream.WriteByte((byte)childIndicesToFirst.Length);
        stream.WriteByte((byte)childIndicesToSecond.Length);
        await stream.WriteAsync(childIndicesToFirst, 0, childIndicesToFirst.Length);
        await stream.WriteAsync(childIndicesToSecond, 0, childIndicesToSecond.Length);
    }

    public void Initialize(AssetPoolee[] initializedPoolees, Constrainer constrainer)
    {
        AssetPoolee firstPoolee = firstObjectIndex != int.MaxValue ? initializedPoolees[firstObjectIndex] : null;
        AssetPoolee secondPoolee = secondObjectIndex != int.MaxValue ? initializedPoolees[secondObjectIndex] : null;

        Transform tForm1 = firstPoolee == null ? CreateDummyTransform() : TraverseHierarchy(firstPoolee.transform, childIndicesToFirst);
        Transform tForm2 = secondPoolee == null ? CreateDummyTransform() : TraverseHierarchy(secondPoolee.transform, childIndicesToSecond);

        // now time to effectively paste whatever the fuck SLZ was doing bruh
        CreateTracker(tForm1, tForm2, ConstraintMode, constrainer);
    }

    private static void CreateTracker(Transform host, Transform otherT, Constrainer.ConstraintMode constraintMode, Constrainer constrainer)
    {
        Rigidbody hostBody = host.GetComponent<Rigidbody>();
        Rigidbody otherBody = otherT.GetComponent<Rigidbody>();

        constrainer.mode = constraintMode;
        constrainer._gO1 = host.gameObject;
        constrainer._gO2 = otherT.gameObject;
        constrainer._rb1 = hostBody; // CANNOT be null else will just bailout
        constrainer._rb2 = otherBody;
        constrainer._point1 = hostBody?.centerOfMass ?? Vector3.zero;
        constrainer._point2 = otherBody?.centerOfMass ?? Vector3.zero;

        constrainer.PrimaryButtonUp();
    }

    private static Transform TraverseHierarchy(Transform transform, byte[] childIdxs)
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

    private static Transform CreateDummyTransform()
    {
        GameObject go = new("WeldPoint!");
        Transform t = go.transform;
        return t;
    }

    private static byte[] GetIndicesFromParent(Transform attachedT)
    {
        if (attachedT.name.StartsWith("jPt"))
        {
#if DEBUG
            SceneSaverBL.Log($"Name starts with jPt: {attachedT.GetFullPath()}");
#endif
            attachedT = attachedT.parent;
        }
        // precalculate depth to avoid overallocating
        byte[] ret;

        int depth = 0;
        Transform depthT = attachedT;
        while(depthT.parent != null)
        {
            depth++;
            depthT = depthT.parent;
        }

        ret = new byte[depth];

        // do in reverse so when being loaded can use a normal for loop
        for (int i = depth - 1; i >= 0; i--)
        {
            ret[i] = (byte)attachedT.GetSiblingIndex();
            attachedT = attachedT.parent;
        }

        return ret;
    }

    static int? GetPooleeIdx(AssetPoolee[] poolees, Transform t)
    {
        string originalPath = t.GetFullPath();
        while (t.parent != null) t = t.parent;

        for (int i = 0; i < poolees.LongLength; i++)
            if (poolees[i].transform == t) return i;

        return null;
    }

    public override bool Equals(object obj)
    {
        if (obj is not SavedConstraint sc) return false;

        return this == sc;
    }
    public override int GetHashCode() => base.GetHashCode();

    public bool Equals(SavedConstraint other)
    {
        return this == other;
    }

    public static bool operator ==(SavedConstraint sc1, SavedConstraint sc2)
    {
#if DEBUG
        // define a bunch of locals so debugger can access these values in the locals view EEEYUP
        int idx1 = sc1.firstObjectIndex;
        int idx2 = sc1.secondObjectIndex;
        int idx3 = sc2.firstObjectIndex;
        int idx4 = sc2.secondObjectIndex;
#endif

        bool sameIdxsMatch = sc1.firstObjectIndex == sc2.firstObjectIndex
                          && sc1.secondObjectIndex == sc2.secondObjectIndex;

        bool diffIdxsMatch = sc1.firstObjectIndex == sc2.secondObjectIndex
                          && sc1.secondObjectIndex == sc2.firstObjectIndex;

        return sameIdxsMatch || diffIdxsMatch;
    }

    public static bool operator !=(SavedConstraint sc1, SavedConstraint sc2) => !(sc1 == sc2);
}
