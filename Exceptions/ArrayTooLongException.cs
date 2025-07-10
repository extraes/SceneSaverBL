using System.Runtime.CompilerServices;

namespace SceneSaverBL.Exceptions;

public class ArrayTooLongException : Exception
{
    public ArrayTooLongException(string message) : base(message) { }
    public ArrayTooLongException(int givenLength, int maxLength, [CallerMemberName] string callerName = "Unknown") : base($"Length {givenLength} is longer than the maximum of {maxLength} in {callerName}") { }
}
