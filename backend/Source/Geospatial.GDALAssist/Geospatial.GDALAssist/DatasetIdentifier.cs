using System;
using Utility.Extensions;
using Utility.Files;

namespace Geospatial.GDALAssist;

public class DatasetIdentifier
{
	public string Filename { get; set; }

	public string Driver { get; set; }

	public string Subdataset { get; set; }

	public string ConnectionString
	{
		get
		{
			if (HasDriver && HasSubdataset)
			{
				return Driver + ":" + Filename.InDoubleQuotes() + ":" + Subdataset;
			}
			return Filename;
		}
	}

	public string CLISafeConnectionString
	{
		get
		{
			if (HasDriver && HasSubdataset)
			{
				return ConnectionString;
			}
			return Filename.InDoubleQuotes();
		}
	}

	public bool HasDriver => !string.IsNullOrEmpty(Driver);

	public bool HasSubdataset => !string.IsNullOrEmpty(Subdataset);

	public DatasetIdentifier(string fn)
	{
		Filename = fn ?? throw new ArgumentNullException("fn");
	}

	public DatasetIdentifier(string fn, string driver, string subds)
	{
		Filename = fn ?? throw new ArgumentNullException("fn");
		Driver = driver ?? throw new ArgumentNullException("driver");
		Subdataset = subds ?? throw new ArgumentNullException("subds");
	}

	public string GetRelativeConnectionString(string pathRelativeBase)
	{
		string relativeOLD = FileHelpers.MakeRelativeOLD(Filename, pathRelativeBase);
		if (HasDriver && HasSubdataset)
		{
			return Driver + ":" + relativeOLD.InDoubleQuotes() + ":" + Subdataset;
		}
		return relativeOLD;
	}
}
