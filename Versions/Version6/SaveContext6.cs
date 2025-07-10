using Il2CppSLZ.Bonelab;

namespace SceneSaverBL.Versions.Version6;

internal struct SaveContext6
{
    // used exclusively for serialization
    internal ConstraintTracker[] allTrackers;
    internal ObjectDestructible[] planks;

    // used for both serialization and deserialization
    internal Poolee[] poolees;
    internal List<Transform>[] transformsByPoolee;
    internal byte[] mapBarcodeBytes;
    internal byte[] usernameBytes;
    internal StringCollection6 strings;

    // used exclusively for deserialization
    internal Constrainer constrainer;
    internal BoardGenerator boardGun;
    internal Dictionary<Rigidbody, bool> frozenDuringLoad;
    internal Task[] pooleeTasks;
    internal Vector3 worldspaceOffset;
    internal int nextPlankIdx;
    internal bool ignorePlanks;
}
