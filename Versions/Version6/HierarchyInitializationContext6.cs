namespace SceneSaverBL.Versions.Version6;

internal readonly struct HierarchyInitializationContext6
{
    // IN SERIALIZATION: useless
    // IN DESERIALIZATION: the targetTransform object to walk down
    //public readonly Transform targetTransform;

    public readonly Poolee[] poolees;

    public HierarchyInitializationContext6(Poolee[] p)
    { 
        this.poolees = p;
    }
}
