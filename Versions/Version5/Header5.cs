using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Jevil;

namespace SceneSaverBL.Versions.Version5;

public struct Header5
{
    public ushort barcodeLen;
    public byte usernameLen;
    public int previewLen;
    public int poolees;
    public int constraints;
    public ushort[] serializedTransformCounts;
    public bool hasSerializedTransforms;
    public Vector3 centerBottom;
    public Vector3 size;

    // NON SERIALIZED FIELDS
    public int dataStartStreamPos; // keep so we know where to start actually reading when we pick up later.
    public int DataReadPos => dataStartStreamPos + barcodeLen + usernameLen + previewLen;

    public async Task Write(Stream stream)
    {
#if DEBUG
        SaveChecks.ThrowIfDefault(barcodeLen);
        SaveChecks.ThrowIfDefault(previewLen);
        SaveChecks.ThrowIfDefault(poolees);
        SaveChecks.ThrowIfDefault(serializedTransformCounts);
        SaveChecks.ThrowIfDefault(centerBottom);
        SaveChecks.ThrowIfDefault(size);
#endif

        byte[] barcodeLenBytes = BitConverter.GetBytes(barcodeLen);
        byte[] usernameLenBytes = new byte[1] { usernameLen };
        byte[] previewLenBytes = BitConverter.GetBytes(previewLen);
        byte[] objectsBytes = BitConverter.GetBytes(poolees);
        byte[] constraintsBytes = BitConverter.GetBytes(constraints);
        byte[] hasSerializedTransformsBytes = BitConverter.GetBytes(hasSerializedTransforms);
        byte[] serializedTransformsBytesLength = BitConverter.GetBytes((ushort)serializedTransformCounts.Length);
        byte[] serializedTransformsBytes = Utilities.SerializePrimitives(serializedTransformCounts);
        byte[] centerBottomBytes = centerBottom.ToBytes();
        byte[] sizeBytes = size.ToBytes();

        await stream.WriteAsync(barcodeLenBytes, 0, barcodeLenBytes.Length);
        await stream.WriteAsync(usernameLenBytes, 0, usernameLenBytes.Length);
        await stream.WriteAsync(previewLenBytes, 0, previewLenBytes.Length);
        await stream.WriteAsync(objectsBytes, 0, objectsBytes.Length);
        await stream.WriteAsync(constraintsBytes, 0, constraintsBytes.Length);
        await stream.WriteAsync(hasSerializedTransformsBytes, 0, hasSerializedTransformsBytes.Length);
#if DEBUG
        SceneSaverBL.Log("Header5 Write: Fixed len pos = " + stream.Position);
#endif
        await stream.WriteAsync(serializedTransformsBytesLength, 0, serializedTransformsBytesLength.Length);
        await stream.WriteAsync(serializedTransformsBytes, 0, serializedTransformsBytes.Length);
#if DEBUG
        SceneSaverBL.Log("Header5 Write: Transform size pos = " + stream.Position);
#endif
        await stream.WriteAsync(centerBottomBytes, 0, centerBottomBytes.Length);
        await stream.WriteAsync(sizeBytes, 0, sizeBytes.Length);
    }

    public static async Task<Header5> ReadFrom(Stream stream)
    {
        Header5 header = default;
        byte[] buffer12 = new byte[Const.SizeV3];

        await stream.ReadAsync(buffer12, 0, sizeof(ushort));
        header.barcodeLen = BitConverter.ToUInt16(buffer12, 0);

        header.usernameLen = (byte)stream.ReadByte();

        await stream.ReadAsync(buffer12, 0, sizeof(int));
        header.previewLen = BitConverter.ToInt32(buffer12, 0);
        
        await stream.ReadAsync(buffer12, 0, sizeof(int));
        header.poolees = BitConverter.ToInt32(buffer12, 0);
        
        await stream.ReadAsync(buffer12, 0, sizeof(int));
        header.constraints = BitConverter.ToInt32(buffer12, 0);

        header.hasSerializedTransforms = stream.ReadByte() != 0;

#if DEBUG
        SceneSaverBL.Log("Header5 Read: Fixed len pos = " + stream.Position);
#endif

        await stream.ReadAsync(buffer12, 0, sizeof(ushort));
        header.serializedTransformCounts = new ushort[BitConverter.ToUInt16(buffer12, 0)];
        for (int i = 0; i < header.serializedTransformCounts.Length; i++)
        {
            await stream.ReadAsync(buffer12, 0, sizeof(ushort));
            header.serializedTransformCounts[i] = BitConverter.ToUInt16(buffer12, 0);
        }
#if DEBUG
        SceneSaverBL.Log("Header5 Read: Transform size pos = " + stream.Position);
#endif

        await stream.ReadAsync(buffer12, 0, Const.SizeV3);
        header.centerBottom = Utilities.DebyteV3(buffer12);
        
        await stream.ReadAsync(buffer12, 0, Const.SizeV3);
        header.size = Utilities.DebyteV3(buffer12);

        header.dataStartStreamPos = (int)stream.Position;
        return header;
    }

    #region Stuff the compiler whined at me to do

    public override bool Equals(object obj)
    {
        return obj is Header5 header &&
               header == this;
    }

    public override int GetHashCode()
    {
        // compiler did this shit. idk what the fuck its on about but sure man
        int hashCode = 2103640168;
        hashCode = hashCode * -1521134295 + barcodeLen.GetHashCode();
        hashCode = hashCode * -1521134295 + previewLen.GetHashCode();
        hashCode = hashCode * -1521134295 + poolees.GetHashCode();
        hashCode = hashCode * -1521134295 + constraints.GetHashCode();
        hashCode = hashCode * -1521134295 + EqualityComparer<ushort[]>.Default.GetHashCode(serializedTransformCounts);
        hashCode = hashCode * -1521134295 + hasSerializedTransforms.GetHashCode();
        hashCode = hashCode * -1521134295 + centerBottom.GetHashCode();
        hashCode = hashCode * -1521134295 + size.GetHashCode();
        hashCode = hashCode * -1521134295 + dataStartStreamPos.GetHashCode();
        return hashCode;
    }

    #endregion

    public static bool operator ==(Header5 lhs,  Header5 rhs)
    {
        // these should be enough to uniquely identify headers
        // miniscule changes should result in preview differences, even with the same selwire
        return lhs.centerBottom == rhs.centerBottom
            && lhs.previewLen == rhs.previewLen
            && lhs.size == rhs.size;
    }

    public static bool operator !=(Header5 lhs, Header5 rhs)
    {
        return !(lhs == rhs);
    }
}
