﻿using System.IO;
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
using SLZ.Bonelab;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedConstraint6 : ISavedConstraint<SavedConstraint6, SaveFile6>
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

    // this really should just be an async method but its "fine" cuz its only called in tasks
    public void Read(Stream stream)
    {
        byte arrLen;
        byte[] buffer4 = new byte[sizeof(int)];

        constraintMode = (byte)stream.ReadByte();
        stream.Read(buffer4, 0, sizeof(int));
        firstObjectIndex = BitConverter.ToInt32(buffer4, 0);
        stream.Read(buffer4, 0, sizeof(int));
        secondObjectIndex = BitConverter.ToInt32(buffer4, 0);

        arrLen = (byte)stream.ReadByte();
        childIndicesToFirst = new byte[arrLen];
        arrLen = (byte)stream.ReadByte();
        childIndicesToSecond = new byte[arrLen];

        stream.Read(childIndicesToFirst, 0, childIndicesToFirst.Length);
        stream.Read(childIndicesToSecond, 0, childIndicesToSecond.Length);
    }

    public void Construct(SaveFile6 originSave, AssetPoolee[] poolees, ConstraintTracker tracker)
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
        await stream.WriteAsync(BitConverter.GetBytes(firstObjectIndex), 0, sizeof(int));
        await stream.WriteAsync(BitConverter.GetBytes(secondObjectIndex), 0, sizeof(int));
        await SaveUtils.WriteArrayAsync(stream, childIndicesToFirst, 1);
        await SaveUtils.WriteArrayAsync(stream, childIndicesToSecond, 1);
    }

    public void Initialize(AssetPoolee[] initializedPoolees, Constrainer constrainer)
    {
        AssetPoolee firstPoolee = firstObjectIndex != int.MaxValue ? initializedPoolees[firstObjectIndex] : null;
        AssetPoolee secondPoolee = secondObjectIndex != int.MaxValue ? initializedPoolees[secondObjectIndex] : null;

        Transform tForm1 = firstPoolee == null ? CreateDummyTransform() : TraverseHierarchy(firstPoolee.transform, childIndicesToFirst);
        Transform tForm2 = secondPoolee == null ? CreateDummyTransform() : TraverseHierarchy(secondPoolee.transform, childIndicesToSecond);

        bool isStaticWeld = firstObjectIndex == int.MaxValue || secondObjectIndex == int.MaxValue;

        if (ConstraintMode == Constrainer.ConstraintMode.Weld && isStaticWeld && !Prefs.loadStaticWelds)
        {
            SceneSaverBL.Log("Ignoring static weld - preferences say to ignore");
            return;
        }

        // now time to effectively paste whatever the fuck SLZ was doing bruh
        CreateTracker(tForm1, tForm2, ConstraintMode, constrainer);
    }

    private static void CreateTracker(Transform host, Transform otherT, Constrainer.ConstraintMode constraintMode, Constrainer constrainer)
    {
        Vector3 hostPt;
        Rigidbody hostBody = Instances<Rigidbody>.Get(host);
        Rigidbody otherBody = Instances<Rigidbody>.Get(otherT);
        if (hostBody.INOC())
        {
            // should ensire hostBody is never null
            Rigidbody rb = hostBody;
            Transform t = host;
            hostBody = otherBody;
            otherBody = rb;
            host = otherT;
            otherT = t;
        }
        hostPt = host.transform.position;
        
        constrainer.mode = constraintMode;
        constrainer._gO1 = host.gameObject;
        constrainer._gO2 = otherT.gameObject;
        constrainer._rb1 = hostBody; // CANNOT be null else will just bailout
        constrainer._rb2 = otherBody;
        constrainer._point1 = hostBody?.centerOfMass ?? hostPt + new Vector3(0, -100, 0);
        constrainer._point2 = otherBody?.centerOfMass ?? hostPt + new Vector3(0, -100, 0);
        
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

    public static bool operator !=(SavedConstraint6 sc1, SavedConstraint6 sc2) => !(sc1 == sc2);
}