using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Exceptions;

public class StringTooLongException : Exception
{
    public StringTooLongException(string message) : base(message) { }
}
