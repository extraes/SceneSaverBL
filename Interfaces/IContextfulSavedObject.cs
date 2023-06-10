using System.Threading.Tasks;

namespace SceneSaverBL.Interfaces;

// allows a saved object to accept context when initializing. the parameterless initializer may be empty.
internal interface IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext> : ISavedObject<TImplementor, TSavedObject>
    where TImplementor : struct, IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext>
    where TInitializeContext : struct
{
    public Task<TSavedObject> Initialize(TInitializeContext ctx);
}
