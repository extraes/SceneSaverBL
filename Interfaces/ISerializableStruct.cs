using BoneLib.BoneMenu.Elements;
using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SceneSaverBL.Interfaces;

internal interface ISerializableStruct<TImplemmentor> where TImplemmentor : struct, ISerializableStruct<TImplemmentor>
{
    public Task Write(Stream stream);
    public void Read(Stream stream);
}
