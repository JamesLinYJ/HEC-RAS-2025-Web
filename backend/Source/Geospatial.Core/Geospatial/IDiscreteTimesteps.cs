using System;
using System.Collections.Generic;

namespace Geospatial;

public interface IDiscreteTimesteps
{
	IReadOnlyList<DateTime> Times { get; }
}
