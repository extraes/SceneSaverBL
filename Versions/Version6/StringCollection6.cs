using Il2CppSLZ.Marrow.Warehouse;
using SceneSaverBL.Interfaces;

namespace SceneSaverBL.Versions.Version6;

internal class StringCollection6 : ISerializableClass
{
    private List<string> strings = new();

    public async Task Read(Stream readFrom)
    {
        using var rent = ByteArrayPool.RentTemp(sizeof(int), true);
        int len;
        await readFrom.ReadAsync(rent.Rented, 0, sizeof(int));
        len = BitConverter.ToInt32(rent.Rented, 0);
        strings = new(len);

#if DEBUG
        SceneSaverBL.Log($"StringCollection6 Read: this save has {len} saved strings. Going to read from stream pos " + readFrom.Position);
#endif

        for (int i = 0; i < len; i++)
        {
            // removed because barcodes are unlikely to surpass 255 in length.
            //readFrom.Read(intBuffer, 0, sizeof(int));
            //len = BitConverter.ToInt32(intBuffer, 0);
            byte strLen = (byte)readFrom.ReadByte();

            byte[] barcodeBytes = new byte[strLen];
            await readFrom.ReadAsync(barcodeBytes, 0, strLen);
            string barcode = SaveFile6.StringEncoding.GetString(barcodeBytes, 0);

#if DEBUG
            SceneSaverBL.Log($"Read {strLen} byte string '{barcode}', ending at stream pos {readFrom.Position}");
#endif

            if (SaveUtils.DontSaveTheseBarcodes.Contains(barcode))
            {
#if DEBUG
                SceneSaverBL.Warn($"That barcode isn't supposed to be loaded though, so instead it becomes apollo!");
#endif
                barcode = Barcodes.ToBarcodeString(JevilBarcode.APOLLO);
            }

            strings.Add(barcode);
        }

#if DEBUG
        SceneSaverBL.Log($"StringCollection6 Read: Finished reading at stream pos " + readFrom.Position);
#endif
    }

    public async Task Write(Stream writeTo)
    {
#if DEBUG
        using ProfilingScope ps = new(SceneSaverBL.instance.LoggerInstance, ProfilingScope.ProfilingType.ALL, "V6 Strings");
#endif

        byte[] lenBytes = BitConverter.GetBytes(strings.Count);
        await writeTo.WriteAsync(lenBytes, 0, lenBytes.Length);

#if DEBUG
        SceneSaverBL.Log($"StringCollection6 Write: Wrote length ({strings.Count}). Going to continue writing from stream pos {writeTo.Position}");
#endif

        for (int i = 0; i < strings.Count; i++)
        {
            byte[] barcodeBytes = SaveFile6.StringEncoding.GetBytes(strings[i]);
#if DEBUG
            if (barcodeBytes.Length > byte.MaxValue)
                throw new InvalidDataException("str too long - shoulda been checked when added to list!");
#endif
            writeTo.WriteByte((byte)barcodeBytes.Length);
            await writeTo.WriteAsync(barcodeBytes, 0, barcodeBytes.Length);

#if DEBUG
            SceneSaverBL.Log($"Wrote {barcodeBytes.Length} byte string '{strings[i]}', ending at {writeTo.Position}");
#endif
        }

#if DEBUG
        SceneSaverBL.Log($"StringCollection6 Write: Finished writing at stream pos {writeTo.Position}");
#endif
    }

    public int GetBarcodeIdx(Barcode barcode)
    {
        string strCode = barcode.ID;
        return GetStringIdx(strCode);
    }

    public int GetStringIdx(string str)
    {
        int ret = strings.IndexOf(str);

        if (ret == -1)
        {
            SaveChecks.ThrowIfLongerThanByte(str, SaveFile6.StringEncoding);
            ret = strings.Count; // new idx will be current length (cuz thatll be the idx of the last element when added)
            strings.Add(str);
#if DEBUG
            SceneSaverBL.Log($"String will be serialized at index {ret}: {str}");
#endif
        }

        return ret;
    }

    public string GetBarcodeStr(int idx)
    {
#if DEBUG
        if (idx >= strings.Count)
            throw new ArgumentOutOfRangeException(nameof(idx), "Poolee tried to recieve barcode at idx " + idx + " but the barcode collection only contains " + strings.Count + " items!");
#endif
        return strings[idx];
    }
}
