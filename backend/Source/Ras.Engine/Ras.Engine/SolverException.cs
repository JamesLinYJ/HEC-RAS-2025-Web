using System;

namespace Ras.Engine;

public class SolverException : Exception
{
	public SolverException(string message)
		: base(message)
	{
	}
}
