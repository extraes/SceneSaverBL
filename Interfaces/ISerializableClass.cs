namespace SceneSaverBL.Interfaces;

internal interface ISerializableClass
{
    public Task Write(Stream stream);
    public Task Read(Stream stream);
}
