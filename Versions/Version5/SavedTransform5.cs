using Jevil;
using Jevil.Spawning;
using SceneSaverBL.Interfaces;
using SLZ.Marrow.Data;
using SLZ.Marrow.Pool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static RootMotion.FinalIK.AimPoser;

namespace SceneSaverBL.Versions.Version5;

internal struct SavedTransform5 : IContextfulSavedObject<SavedTransform5, Transform, TransformInitializationContext5>
{
    // 12 + 12 + 8 = 32
    private Vector3 localPos;
    private Vector3 scale;
    private Vector3 localRotation;

    public Vector3 LocalPosition => localPos;
    public Quaternion Rotation => Quaternion.Euler(localRotation);

    static byte[] vector3Buffer = new byte[Const.SizeV3];


    public void Construct(Transform sourceTransform)
    {
        localPos = sourceTransform.transform.localPosition;
        scale = sourceTransform.transform.localScale;
        localRotation = sourceTransform.transform.localRotation.eulerAngles;

#if DEBUG
        SaveChecks.ThrowIfInvalid(localPos);
        SaveChecks.ThrowIfInvalid(localRotation);
        SaveChecks.ThrowIfInvalid(scale);
#endif
    }

    // cannot be async - async methods cannot modify their original instances
    public void Read(Stream stream)
    {
        Vector3 readPos;
        Vector3 readScale;
        Vector3 readRot;
        //ushort readChildren;
        byte[] buffer = vector3Buffer;

        stream.Read(buffer, 0, Const.SizeV3);
        readPos = Utilities.DebyteV3(buffer, 0);
        stream.Read(buffer, 0, Const.SizeV3);
        readScale = Utilities.DebyteV3(buffer, 0);
        stream.Read(buffer, 0, Const.SizeV3);
        readRot = Utilities.DebyteV3(buffer, 0);
        //stream.Read(buffer, 0, sizeof(ushort));
        //readChildren = BitConverter.ToUInt16(buffer, 0);

        localPos = readPos;
        localRotation = readRot;
        scale = readScale;

#if DEBUG
        SceneSaverBL.Log($"Read: " + ToString());
        SaveChecks.ThrowIfInvalid(localPos);
        SaveChecks.ThrowIfInvalid(localRotation);
        SaveChecks.ThrowIfInvalid(scale);
#endif
    }

    public async Task Write(Stream stream)
    {
        byte[] posBytes = localPos.ToBytes();
        byte[] scaleBytes = scale.ToBytes();
        byte[] rotBytes = localRotation.ToBytes();
        //byte[] childrenBytes = BitConverter.GetBytes(children);
        await stream.WriteAsync(posBytes, 0, Const.SizeV3);
        await stream.WriteAsync(scaleBytes, 0, Const.SizeV3);
        await stream.WriteAsync(rotBytes, 0, Const.SizeV3);
        //await stream.WriteAsync(childrenBytes, 0, Const.SizeV3); ;

#if DEBUG
        SceneSaverBL.Log("Wrote " + ToString());
#endif
    }

    public Task<Transform> Initialize() => throw new NotSupportedException("SavedTransform is a contextually saved object - you must initialize it with a context");

    public Task<Transform> Initialize(TransformInitializationContext5 context)
    {
        Transform t = context.transform;

        // use localposition because it will likely have lower average values, meaning (insignificantly) more precise floats
        t.localPosition = LocalPosition;
        t.localRotation = Rotation;
        t.localScale = scale;

#if DEBUG
        SceneSaverBL.Log($"Transform '{t.name}' LocalPosition set to: {t.localPosition} (Deserialized pos was {SaveUtils.ToStr(localPos)})");
        float dist = Vector3.Distance(localPos, t.localPosition);
        if (dist > 0.1f) SceneSaverBL.Warn($"!!! THIS IS {dist} METERS AWAY FROM SERIALIZED POSITION!!! SPOS: {SaveUtils.ToStr(localPos)}");
        SaveChecks.ThrowIfInvalid(t.localPosition);
        SaveChecks.ThrowIfInvalid(t.position);
#endif

        return Task.FromResult(t);
    }

    public bool Equals(SavedTransform5 other)
    {
        //return other.children == this.children 
        return other.localPos == this.localPos
            && other.localRotation == this.localRotation
            && other.scale == this.scale;
    }

    public override string ToString()
    {
        return $"SSBL Transform V5 - LPos = {SaveUtils.ToStr(localPos)}; LRot (euler) = {SaveUtils.ToStr(localRotation)}; Scale = {SaveUtils.ToStr(scale)}";
    }
}
