using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SceneSaverBL.Exceptions;


[Serializable]
public class SpawnedPooleeNotFoundException : Exception
{
	public SpawnedPooleeNotFoundException() { }
	public SpawnedPooleeNotFoundException(string message) : base(message) { }
	public SpawnedPooleeNotFoundException(string message, Exception inner) : base(message, inner) { }
	protected SpawnedPooleeNotFoundException(
	  System.Runtime.Serialization.SerializationInfo info,
	  System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
