using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

// Should be an easy serialize and deserialize, profiling will determine whether its quick enough to be synchronously serialized and dseserialized
public struct PreviewData6 : ISerializableStruct<PreviewData6>
{
    public Vector3 centerBottom;
    public Vector3 size;

    //todo: use previewmesh
    public Bounds[] pooleeBoundingBoxes;

    public async Task Write(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;

        Utilities.SerializeInPlace(vecArr, centerBottom);
        await stream.WriteAsync(vecArr);
        Utilities.SerializeInPlace(vecArr, size);
        await stream.WriteAsync(vecArr);


        Utilities.SerializeInPlace(vecArr, (ushort)pooleeBoundingBoxes.Length, 0);
        await stream.WriteAsync(vecArr, 0, sizeof(ushort));
        
#if DEBUG
        SceneSaverBL.Log("PreviewData6 Write: poolee bounding box count is " + (ushort)pooleeBoundingBoxes.Length + " @ stream pos " + stream.Position);
#endif

        foreach (Bounds boundBox in pooleeBoundingBoxes)
        {
            // avoid re-allocating data and keep writing to stream
            Utilities.SerializeInPlace(vecArr, boundBox.center);
            await stream.WriteAsync(vecArr);
            Utilities.SerializeInPlace(vecArr, boundBox.size);
            await stream.WriteAsync(vecArr);
        }
    }

    public void Read(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;

        stream.Read(vecArr, 0, vecArr.Length);
        centerBottom = Utilities.DebyteV3(vecArr);
        stream.Read(vecArr, 0, vecArr.Length);
        size = Utilities.DebyteV3(vecArr);

        stream.Read(vecArr, 0, sizeof(ushort));
        ushort usher = BitConverter.ToUInt16(vecArr, 0);
        pooleeBoundingBoxes = new Bounds[usher];

#if DEBUG
        SceneSaverBL.Log("PreviewData6 Read: poolee bounding box count is " + usher + " @ stream pos " + stream.Position);
#endif

        for (int i = 0; i < usher; i++)
        {
            stream.Read(vecArr, 0, vecArr.Length);

            Vector3 center = Utilities.DebyteV3(vecArr);
            stream.Read(vecArr, 0, vecArr.Length);
            Vector3 size = Utilities.DebyteV3(vecArr);
            
            pooleeBoundingBoxes[i] = new Bounds(center, size);
        }
    }

    public Bounds GetBoundsOfPoolees()
    {
        if (pooleeBoundingBoxes.Length == 0)
            return new Bounds(centerBottom, size);

        Bounds bounds = pooleeBoundingBoxes[0];
        for (int i = 1; i < pooleeBoundingBoxes.Length; i++)
        {
            bounds.Encapsulate(pooleeBoundingBoxes[i]);
        }

        return bounds;
    }
}
