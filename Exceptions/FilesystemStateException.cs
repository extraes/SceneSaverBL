using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Exceptions;

internal class FilesystemStateException : Exception
{
    public FilesystemStateException()
    {
    }

    public FilesystemStateException(string message) : base(message)
    {
    }

    public FilesystemStateException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected FilesystemStateException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
