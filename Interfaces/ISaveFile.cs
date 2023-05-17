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

internal interface ISaveFile : ISerializableClass
{
    public byte Version { get; }
    public Task SetFilePath(string filePath); // called before bonemenu init
    public bool ExistsOnDisk();
    public void PopulateBoneMenu(MenuCategory category);
    public Task Construct(AssetPoolee[] poolees, ConstraintTracker[] constraints);
    public Task Initialize();
}
