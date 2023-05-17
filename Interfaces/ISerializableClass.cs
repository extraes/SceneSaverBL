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

internal interface ISerializableClass
{
    public Task Write(Stream stream);
    public Task Read(Stream stream);
}
