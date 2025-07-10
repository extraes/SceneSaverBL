namespace SceneSaverBL.Interfaces;

internal interface ISerializableStruct<TImplemmentor> where TImplemmentor : struct, ISerializableStruct<TImplemmentor>
{
    public Task Write(Stream stream);
    public void Read(Stream stream);
}
