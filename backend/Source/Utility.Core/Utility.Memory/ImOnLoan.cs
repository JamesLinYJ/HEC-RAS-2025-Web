using System;

namespace Utility.Memory;

public interface ImOnLoan<T> : IDisposable
{
	T Item { get; }
}
