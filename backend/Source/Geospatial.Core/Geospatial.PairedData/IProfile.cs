using System.Collections.Generic;

namespace Geospatial.PairedData;

public interface IProfile<T> where T : unmanaged
{
	int Count { get; }

	IReadOnlyList<double> Stations { get; }

	IReadOnlyList<T> Values { get; }

	T Sample(double station);
}
