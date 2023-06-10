using Jevil;
using Jevil.Spawning;
using PuppetMasta;
using SceneSaverBL.Interfaces;
using SLZ.AI;
using SLZ.Marrow.Data;
using SLZ.Marrow.Pool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedPoolee6 : IContextfulSavedObject<SavedPoolee6, AssetPoolee, PooleeInitializationContext6>
{
    // 12 + 12 + 8 = 32
    private Vector3 pos;
    private Vector3 scale;
    private Vector3 rot;
    private int barcodeIdx;

    public Vector3 Position => pos;
    public Quaternion Rotation => Quaternion.Euler(rot);

    static byte[] vector3Buffer = new byte[Const.SizeV3];


    public void Construct(AssetPoolee poolee)
    {
        Transform posrotT = poolee.transform;
        AIBrain brain = AIBrain.Cache.Get(poolee.gameObject);
        if (brain != null)
        {
            // set to current location - AIBrain (on root AssetPoolee transform) does not move, but LiteLoco/NavMeshAgent does
            BehaviourBaseNav bbn = brain.puppetMaster.behaviours.FirstOrDefault()?.TryCast<BehaviourBaseNav>();
            if (bbn != null)
                posrotT = bbn._navAgent.transform;
        }

        scale = poolee.transform.localScale;
        pos = posrotT.position;
        rot = posrotT.rotation.eulerAngles;
        barcode = poolee.spawnableCrate.Barcode.ID;
    }

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
        stream.Read(buffer, 0, sizeof(ushort));
        barcodeLength = BitConverter.ToUInt16(buffer, 0);
        barcodeBuffer = new byte[barcodeLength];
        stream.Read(barcodeBuffer, 0, barcodeLength);
        readBarcode = Encoding.UTF8.GetString(barcodeBuffer);

        pos = readPos;
        rot = readRot;
        barcode = readBarcode;
        scale = readScale;

#if DEBUG
        SceneSaverBL.Log("Read: " + ToString());
#endif
    }

    public async Task Write(Stream stream)
    {
        byte[] posBytes = pos.ToBytes();
        byte[] scaleBytes = scale.ToBytes();
        byte[] rotBytes = rot.ToBytes();
        byte[] barcodeBytes = Encoding.UTF8.GetBytes(barcode);
        byte[] barcodeLength = BitConverter.GetBytes((ushort)barcodeBytes.Length);
        await stream.WriteAsync(posBytes, 0, Const.SizeV3);
        await stream.WriteAsync(scaleBytes, 0, Const.SizeV3);
        await stream.WriteAsync(rotBytes, 0, Const.SizeV3);

        await stream.WriteAsync(barcodeLength, 0, sizeof(ushort));
        await stream.WriteAsync(barcodeBytes, 0, barcodeBytes.Length);


#if DEBUG
        SceneSaverBL.Log("Wrote: " + ToString());
#endif
    }

    public async Task<AssetPoolee> Initialize()
    {
        if (barcode == "SLZ.BONELAB.Core.DefaultPlayerRig") return null;

        Spawnable mySpawnable = Barcodes.ToSpawnable(barcode);
        AssetPoolee poolee = await mySpawnable.SpawnAsync(pos, Rotation);
        poolee.transform.localScale = scale;
        return poolee;
    }

    public bool Equals(SavedPoolee6 other)
    {
        return other.barcode == this.barcode 
            && other.pos == this.pos
            && other.rot == this.rot
            && other.scale == this.scale;
    }

    public override string ToString()
    {
        return $"SSBL Poolee V6 - Pos = {SaveUtils.ToStr(pos)}; Rot (euler) = {SaveUtils.ToStr(rot)}; Scale = {SaveUtils.ToStr(scale)}; Barcode = {barcode}";
    }

    public Task<AssetPoolee> Initialize(PooleeInitializationContext6 ctx)
    {
        throw new NotImplementedException();
    }
}
