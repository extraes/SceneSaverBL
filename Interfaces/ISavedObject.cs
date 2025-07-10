namespace SceneSaverBL.Interfaces;

internal interface ISavedObject<TImplementor, TSavedObject> : IEquatable<TImplementor>, ISerializableStruct<TImplementor> where TImplementor : struct, ISavedObject<TImplementor, TSavedObject>
{
    // use blocking calls for Construct because it accesses unity shit
    public void Construct(TSavedObject prepareToSerialize);
    public Task<TSavedObject> Initialize();
}
