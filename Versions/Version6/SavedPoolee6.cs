using Il2CppSLZ.Marrow.PuppetMasta;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

internal struct SavedPoolee6 : IContextfulSavedObject<SavedPoolee6, Poolee, PooleeInitializationContext6>
{
    private Vector3 pos;
    private Vector3 scale;
    private Vector3 rotEul;
    private int barcodeIdx;

    public readonly Vector3 Position => pos;
    public readonly Quaternion Rotation => Quaternion.Euler(rotEul);

    static readonly byte[] vector3Buffer = new byte[Const.SizeV3];

    public void Construct(Poolee poolee, PooleeInitializationContext6 ctx)
    {
        Transform posrotT = poolee.transform;
        AIBrain? brain = Instances<AIBrain>.Get(poolee.gameObject);
        if (brain != null)
        {
            // set to current location - AIBrain (on targetTransform Poolee transform) does not move, but LiteLoco/NavMeshAgent does
            BehaviourBaseNav? bbn = brain.puppetMaster.behaviours.FirstOrDefault()?.TryCast<BehaviourBaseNav>();
            if (bbn != null)
                posrotT = bbn._navAgent.transform;
        }

        scale = poolee.transform.localScale;
        pos = posrotT.position;
        rotEul = posrotT.rotation.eulerAngles;
        barcodeIdx = ctx.barcodes.GetBarcodeIdx(poolee.SpawnableCrate.Barcode);
    }

    public void Read(Stream stream)
    {
        Vector3 readPos;
        Vector3 readScale;
        Vector3 readRot;
        int readBarcodeIdx;
        byte[] buffer = vector3Buffer;

        stream.Read(buffer, 0, Const.SizeV3);
        readPos = Utilities.DebyteV3(buffer, 0);
        stream.Read(buffer, 0, Const.SizeV3);
        readScale = Utilities.DebyteV3(buffer, 0);
        stream.Read(buffer, 0, Const.SizeV3);
        readRot = Utilities.DebyteV3(buffer, 0);
        stream.Read(buffer, 0, sizeof(int));
        readBarcodeIdx = BitConverter.ToInt32(buffer, 0);
        
        pos = readPos;
        rotEul = readRot;
        barcodeIdx = readBarcodeIdx;
        scale = readScale;

#if DEBUG
        SceneSaverBL.Log("Read: " + ToString());
#endif
    }

    public async Task Write(Stream stream)
    {
        byte[] posBytes = pos.ToBytes();
        byte[] scaleBytes = scale.ToBytes();
        byte[] rotBytes = rotEul.ToBytes();
        byte[] barcodeBytes = BitConverter.GetBytes(barcodeIdx);
        await stream.WriteAsync(posBytes, 0, Const.SizeV3);
        await stream.WriteAsync(scaleBytes, 0, Const.SizeV3);
        await stream.WriteAsync(rotBytes, 0, Const.SizeV3);

        await stream.WriteAsync(barcodeBytes, 0, barcodeBytes.Length);

#if DEBUG
        SceneSaverBL.Log("Wrote: " + ToString());
#endif
    }

    public async Task<Poolee> Initialize(PooleeInitializationContext6 ctx)
    {
        string barcodeStr = ctx.barcodes.GetBarcodeStr(barcodeIdx);

        Spawnable mySpawnable = Barcodes.ToSpawnable(barcodeStr);
        Poolee poolee = await mySpawnable.SpawnAsyncS(pos + ctx.offset, Rotation);
        poolee.transform.localScale = scale;
        return poolee;
    }

    public bool Equals(SavedPoolee6 other)
    {
        return other.barcodeIdx == this.barcodeIdx 
            && other.pos == this.pos
            && other.rotEul == this.rotEul
            && other.scale == this.scale;
    }

    public override string ToString()
    {
        return $"SSBL Poolee V6 - Pos = {SaveUtils.ToStr(pos)}; Rot (euler) = {SaveUtils.ToStr(rotEul)}; Scale = {SaveUtils.ToStr(scale)}; Barcode Index = {barcodeIdx}";
    }

    public string GetBarcodeStr(StringCollection6 stringCollection)
    {
        return stringCollection.GetBarcodeStr(barcodeIdx);
    }
}
