namespace SceneSaverBL.Versions.Version5;

internal struct SaveContext5
{
    // used exclusively for serialization
    internal ConstraintTracker[] allTrackers;

    // used for both serialization and deserialization
    internal Poolee[] poolees;
    internal List<Transform>[] transformsByPoolee;
    internal byte[] barcodeBytes;
    internal byte[] usernameBytes;

    // used exclusively for deserialization
    internal Constrainer constrainer;
    internal Dictionary<Rigidbody, bool> frozenDuringLoad;
    internal Task[] pooleeTasks;
}
