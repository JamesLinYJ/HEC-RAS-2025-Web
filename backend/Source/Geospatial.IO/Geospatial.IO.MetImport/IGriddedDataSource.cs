using System;
using System.Collections.Generic;

namespace Geospatial.IO.MetImport;

public interface IGriddedDataSource
{
	IReadOnlyList<DateTime> BandTimesteps { get; }

	IReadOnlyList<DateTime> BandIssueTimes { get; }

	string Name { get; }

	string Filepath { get; }

	GriddedSourceUnits Units => default;
}
