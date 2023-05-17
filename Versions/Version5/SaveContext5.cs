using Cysharp.Threading.Tasks;
using SLZ.Marrow.Pool;
using SLZ.Props;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnhollowerBaseLib;
using UnityEngine;

namespace SceneSaverBL.Versions.Version5;

internal struct SaveContext5
{
    // used exclusively for serialization
    internal ConstraintTracker[] allTrackers;

    // used for both serialization and deserialization
    internal AssetPoolee[] poolees;
    internal List<Transform>[] transformsByPoolee;
    internal byte[] barcodeBytes;
    internal byte[] usernameBytes;

    // used exclusively for deserialization
    internal Constrainer constrainer;
    internal Dictionary<Rigidbody, bool> frozenDuringLoad;
    internal Task[] pooleeTasks;
}
