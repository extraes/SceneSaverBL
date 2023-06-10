using System.Threading.Tasks;

namespace SceneSaverBL.Interfaces;

// allows a saved object to accept context when initializing. the parameterless initializer may be empty.
internal interface IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext, TSaveFile> : ISavedObject<TImplementor, TSavedObject, TSaveFile>
    where TImplementor : struct, IContextfulSavedObject<TImplementor, TSavedObject, TInitializeContext, TSaveFile>
    where TInitializeContext : struct
    where TSaveFile : ISaveFile
{
    public Task<TSavedObject> Initialize(TInitializeContext ctx);
}
