namespace SceneSaverBL.Exceptions;

public class UninitializedSerializedDataException : Exception
{
    public UninitializedSerializedDataException(string message) : base(message) { }
}
