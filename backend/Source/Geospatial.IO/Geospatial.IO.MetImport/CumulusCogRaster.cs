using System;
using System.Collections.Generic;
using Utility.Units;

namespace Geospatial.IO.MetImport;

public class CumulusCogRaster : IGriddedDataSource
{
	private readonly string _name;

	private readonly DateTime[] _bandTimesteps;

	private readonly DateTime[] _bandIssueTimes;

	public string SourcePath { get; }

	public DateTime Timestep { get; }

	public DateTime IssueTime { get; }

	public IReadOnlyList<DateTime> BandTimesteps => _bandTimesteps;

	public IReadOnlyList<DateTime> BandIssueTimes => _bandIssueTimes;

	public string Name => _name;

	public string Filepath => SourcePath;

	public GriddedSourceUnits Units => new GriddedSourceUnits
	{
		DefaultPrecipDataType = "per-cum",
		DefaultLength = Length.Millimeters.Name
	};

	public CumulusCogRaster(string sourcePath, DateTime timestep, DateTime issueTime, string name)
	{
		SourcePath = sourcePath;
		Timestep = timestep;
		IssueTime = issueTime;
		_name = name;
		_bandTimesteps = new DateTime[1] { timestep };
		_bandIssueTimes = new DateTime[1] { issueTime };
	}
}
