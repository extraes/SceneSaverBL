namespace SceneSaverBL.Interfaces;

// allows a saved object to accept context when initializing. the parameterless initializer may be empty.
internal interface IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext> : ISerializableStruct<TImplementor>
    where TImplementor : struct, IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext>
    where TInitializeContext : struct
{
    public void Construct(TSavedObject save, TInitializeContext ctx);
    public Task<TSavedObject> Initialize(TInitializeContext ctx);
}
