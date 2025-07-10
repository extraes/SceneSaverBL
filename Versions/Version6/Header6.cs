using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

public class Header6 : ISerializableClass
{
    public bool IsEmpty => previewLen == default; // this will always get set when writing and reading.

    public ushort mapBarcodeLen;
    public byte usernameLen;
    public int previewLen;
    public int poolees;
    public int constraints;
    public int planks;
    public ushort[] serializedTransformCounts;
    public bool hasSerializedTransforms;
    // moved centerBottom and size to PreviewData
    public PreviewData6 previewData;

    // NON SERIALIZED FIELDS
    public int dataStartStreamPos; // keep so we know where to start actually reading when we pick up later.
    public int DataReadPos => dataStartStreamPos + mapBarcodeLen + usernameLen + previewLen;

    public async Task Write(Stream stream)
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(mapBarcodeLen);
        SaveChecks.ThrowIfDefault(previewLen);
        SaveChecks.ThrowIfDefault(poolees + planks);
        SaveChecks.ThrowIfDefault(serializedTransformCounts); // even if no serialized transforms, this just gets defaulted to array.empty
#endif
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] vecArr = rent.Rented;

        Utilities.SerializeInPlace(vecArr, mapBarcodeLen); // 2
        vecArr[sizeof(ushort)] = usernameLen; // 3
        Utilities.SerializeInPlace(vecArr, previewLen, sizeof(ushort) + sizeof(byte)); // 5
        Utilities.SerializeInPlace(vecArr, poolees, sizeof(ushort) + sizeof(byte) + sizeof(int)); // 9
        await stream.WriteAsync(vecArr, 0, sizeof(ushort) + sizeof(byte) + sizeof(int) + sizeof(int));

        Utilities.SerializeInPlace(vecArr, planks, 0);
        Utilities.SerializeInPlace(vecArr, constraints, sizeof(int));
        Utilities.SerializeInPlace(vecArr, (byte)(hasSerializedTransforms ? 1 : 0), sizeof(int) + sizeof(int));
        await stream.WriteAsync(vecArr, 0, sizeof(int) + sizeof(int) + sizeof(bool));


#if DEBUG
        SceneSaverBL.Log("Header6 Write: Fixed len pos = " + stream.Position);
#endif
        await SaveUtils.WriteArrayAsync(stream, serializedTransformCounts, sizeof(ushort));

#if DEBUG
        SceneSaverBL.Log("Header6 Write: Transform size pos = " + stream.Position);
#endif

        await previewData.Write(stream);

#if DEBUG
        SceneSaverBL.Log("Header6 Write: Preview data pos = " + stream.Position);
#endif
    }

    public async Task Read(Stream stream)
    {
        using var rent = ByteArrayPool.RentVectorTemp();
        byte[] buffer12 = rent.Rented;

        stream.Read(buffer12, 0, sizeof(ushort));
        mapBarcodeLen = BitConverter.ToUInt16(buffer12, 0);

        usernameLen = (byte)stream.ReadByte();

        stream.Read(buffer12, 0, sizeof(int));
        previewLen = BitConverter.ToInt32(buffer12, 0);
        
        stream.Read(buffer12, 0, sizeof(int));
        poolees = BitConverter.ToInt32(buffer12, 0);
        
        stream.Read(buffer12, 0, sizeof(int));
        planks = BitConverter.ToInt32(buffer12, 0);

        stream.Read(buffer12, 0, sizeof(int));
        constraints = BitConverter.ToInt32(buffer12, 0);

        hasSerializedTransforms = stream.ReadByte() != 0;

#if DEBUG
        SceneSaverBL.Log("Header6 Read: Fixed len pos = " + stream.Position);
#endif

        serializedTransformCounts = await SaveUtils.ReadArrayAsync<ushort>(stream, 2);
        
#if DEBUG
        SceneSaverBL.Log("Header6 Read: Transform size pos = " + stream.Position);
#endif

        previewData.Read(stream);

#if DEBUG
        SceneSaverBL.Log("Header6 Read: Preview data pos = " + stream.Position);
#endif

        dataStartStreamPos = (int)stream.Position;
    }

    #region Stuff the compiler whined at me to do

    public override bool Equals(object obj)
    {
        return obj is Header6 header &&
               header == this;
    }

    public override int GetHashCode()
    {
        // compiler did this shit. idk what the fuck its on about but sure man
        int hashCode = 2103640168;
        hashCode = hashCode * -1521134295 + mapBarcodeLen.GetHashCode();
        hashCode = hashCode * -1521134295 + previewLen.GetHashCode();
        hashCode = hashCode * -1521134295 + poolees.GetHashCode();
        hashCode = hashCode * -1521134295 + constraints.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<ushort[]>.Default.GetHashCode(serializedTransformCounts);
        hashCode = hashCode * -1521134295 + hasSerializedTransforms.GetHashCode();
        //hashCode = hashCode * -1521134295 + centerBottom.GetHashCode();
        //hashCode = hashCode * -1521134295 + size.GetHashCode();
        hashCode = hashCode * -1521134295 + dataStartStreamPos.GetHashCode();
        return hashCode;
    }

    #endregion

    public static bool operator ==(Header6 lhs,  Header6 rhs)
    {
        // both are null
        if (lhs is null && rhs is null)
            return true;

        // only one is null
        if (lhs is null || rhs is null)
            return false;

        // these should be enough to uniquely identify headers
        // miniscule changes should result in preview differences, even with the same selwire
        return lhs.mapBarcodeLen == rhs.mapBarcodeLen
            && lhs.serializedTransformCounts.SequenceEqual(rhs.serializedTransformCounts);

    }

    public static bool operator !=(Header6 lhs, Header6 rhs)
    {
        return !(lhs == rhs);
    }
}
