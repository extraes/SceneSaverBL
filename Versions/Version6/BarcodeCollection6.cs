using Jevil;
using SceneSaverBL.Interfaces;
using SLZ.Marrow.Warehouse;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Versions.Version6;

internal class BarcodeCollection6 : ISerializableClass
{
    private List<string> barcodes = new();

    public async Task Read(Stream readFrom)
    {
        byte[] intBuffer = new byte[4];
        int len;
        await readFrom.ReadAsync(intBuffer, 0, sizeof(int));
        len = BitConverter.ToInt32(intBuffer, 0);
        barcodes = new(len);

        for (int i = 0; i < len; i++)
        {
            // removed because barcodes are unlikely to surpass 255 in length.
            //readFrom.Read(intBuffer, 0, sizeof(int));
            //len = BitConverter.ToInt32(intBuffer, 0);
            len = readFrom.ReadByte();

            byte[] barcodeBytes = new byte[len];
            await readFrom.ReadAsync(barcodeBytes, 0, len);
            string barcode = SaveFile6.StringEncoding.GetString(barcodeBytes, len);

            barcodes.Add(barcode);
        }
    }

    public async Task Write(Stream writeTo)
    {
#if DEBUG
        using ProfilingScope ps = new(SceneSaverBL.instance.LoggerInstance);
#endif

        byte[] lenBytes = BitConverter.GetBytes(barcodes.Count);
        await writeTo.WriteAsync(lenBytes, 0, lenBytes.Length);
        for (int i = 0; i < barcodes.Count; i++)
        {
            byte[] barcodeBytes = SaveFile6.StringEncoding.GetBytes(barcodes[i]);
#if DEBUG
            if (barcodeBytes.Length > byte.MaxValue) throw new InvalidDataException("str too long - shoulda been checked when added to list!");
#endif
            writeTo.WriteByte((byte)barcodeBytes.Length);
            writeTo.Write(barcodeBytes, 0, barcodeBytes.Length);
        }
    }

    public int GetBarcodeIdx(Barcode barcode)
    {
        string strCode = barcode.ID; // cache string so we dont alloc another by calling barcode.get_ID and crossing domain bounds again
        int ret = barcodes.IndexOf(strCode);
        
        if (ret == -1)
        {
            SaveChecks.ThrowIfLongerThanByte(strCode, SaveFile6.StringEncoding);
            ret = barcodes.Count; // new idx will be current length (cuz thatll be the idx of the last element when added)
            barcodes.Add(strCode);
#if DEBUG
            SceneSaverBL.Log($"Barcode will be serialized at index {ret}: {strCode}");
#endif
        }

        return ret;
    }
}
