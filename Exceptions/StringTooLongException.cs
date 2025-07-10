namespace SceneSaverBL.Exceptions;

public class StringTooLongException : Exception
{
    public StringTooLongException(string message) : base(message) { }
}
