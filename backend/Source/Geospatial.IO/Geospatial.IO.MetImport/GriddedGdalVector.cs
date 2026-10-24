using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;

namespace Geospatial.IO.MetImport;

public class GriddedGdalVector : IGriddedDataSource
{
	private enum VectorComponent
	{
		X,
		Y,
		Magnitude,
		Direction,
		Invalid
	}

	public bool IsMagnitudeDirection { get; private set; }

	public GdalRasterSummary A { get; private set; }

	public GdalRasterSummary B { get; private set; }

	public GdalRasterSummary X
	{
		get
		{
			if (!IsMagnitudeDirection)
			{
				return A;
			}
			return null;
		}
	}

	public GdalRasterSummary Y
	{
		get
		{
			if (!IsMagnitudeDirection)
			{
				return B;
			}
			return null;
		}
	}

	public GdalRasterSummary Magnitude
	{
		get
		{
			if (!IsMagnitudeDirection)
			{
				return null;
			}
			return A;
		}
	}

	public GdalRasterSummary Direction
	{
		get
		{
			if (!IsMagnitudeDirection)
			{
				return null;
			}
			return B;
		}
	}

	public IReadOnlyList<DateTime> BandTimesteps => A.BandTimestamps;

	public IReadOnlyList<DateTime> BandIssueTimes => A.ForecastIssueTimes;

	public string Name => Path.GetFileName(A.Path) + " - " + Path.GetFileName(B.Path);

	public string Filepath => A.Path;

	public GriddedGdalVector(GdalRasterSummary a, GdalRasterSummary b)
	{
		VectorComponent component = GetComponent(a);
		VectorComponent component2 = GetComponent(b);
		if (component == VectorComponent.X && component2 == VectorComponent.Y)
		{
			AssignXY(a, b);
		}
		else if (component2 == VectorComponent.X && component == VectorComponent.Y)
		{
			AssignXY(b, a);
		}
		else if (component == VectorComponent.Magnitude && component2 == VectorComponent.Direction)
		{
			AssignMagDir(a, b);
		}
		else if (component2 == VectorComponent.Magnitude && component == VectorComponent.Direction)
		{
			AssignMagDir(b, a);
		}
	}

	private void AssignXY(GdalRasterSummary x, GdalRasterSummary y)
	{
		A = x;
		B = y;
		IsMagnitudeDirection = false;
	}

	private void AssignMagDir(GdalRasterSummary mag, GdalRasterSummary dir)
	{
		A = mag;
		B = dir;
		IsMagnitudeDirection = true;
	}

	private static VectorComponent GetComponent(GdalRasterSummary summary)
	{
		VectorComponent component = GetComponent(summary.DataType);
		if (component != VectorComponent.Invalid)
		{
			return component;
		}
		return GetComponent(Path.GetFileNameWithoutExtension(summary.Path).Split('_').Last());
	}

	private static VectorComponent GetComponent(string typename)
	{
		if (GdalVectorComponentNames.Xs.Contains(typename))
		{
			return VectorComponent.X;
		}
		if (GdalVectorComponentNames.Ys.Contains(typename))
		{
			return VectorComponent.Y;
		}
		if (GdalVectorComponentNames.Magnitudes.Contains(typename))
		{
			return VectorComponent.Magnitude;
		}
		if (GdalVectorComponentNames.Directions.Contains(typename))
		{
			return VectorComponent.Direction;
		}
		return VectorComponent.Invalid;
	}
}
