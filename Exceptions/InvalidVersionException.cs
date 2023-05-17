using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Exceptions;

public class InvalidVersionException : Exception
{
    public readonly int version;
    public InvalidVersionException(int version) : base("Unrecognized file version " + version) 
    {
        this.version = version;
    }
    public InvalidVersionException(string message) : base(message) { }
}
