namespace SceneSaverBL.Versions.Version6;

internal struct TransformInitializationContext6
{
    // not yet used - may be used later down the line to pass into HIC6
    //public StringCollection6 strings;

    // used both for serialization and deserialization (by hierarchylocation6)
    public readonly Poolee[] poolees;
    public readonly Vector3 worldspaceRootOffset;

    public TransformInitializationContext6(Poolee[] poolees, Vector3 wsRootOffset)
    {
        this.poolees = poolees;
        this.worldspaceRootOffset = wsRootOffset;
    }
}
