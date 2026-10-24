using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public class WindSetupParams : BasicRectangleParams
{
	public double StillWaterDepth = 10.0;

	public double WindStress = 0.5;

	public double Rhow = 1000.0;

	public override List<string> Tags
	{
		get
		{
			int ordinal = 4;
			List<string> stringList = new List<string>(ordinal);
			CollectionsMarshal.SetCount(stringList, ordinal);
			Span<string> stringListSpan = CollectionsMarshal.AsSpan(stringList);
			int ordinal2 = 0;
			stringListSpan[ordinal2] = "Wind";
			ordinal2++;
			stringListSpan[ordinal2] = "Setup";
			ordinal2++;
			stringListSpan[ordinal2] = "SWE";
			ordinal2++;
			stringListSpan[ordinal2] = "Verification";
			return stringList;
		}
	}

	public static WindSetupParams Default()
	{
		return new WindSetupParams
		{
			CellSize = 10.0,
			CellsWide = 100,
			CellsTall = 1,
			NValue = 10f,
			Slope = 0.0,
			StillWaterDepth = 10.0,
			WindStress = 0.5,
			Rhow = 1000.0,
			SolveDt = 1.0,
			SolveDuration = 3600.0
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point pt) => (float)StillWaterDepth;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		double L = (double)CellsWide * CellSize;
		double slope = WindStress / (Rhow * SolverControl.GravSI * StillWaterDepth);
		return (Point pt, double t) => (float)(StillWaterDepth + slope * (pt.X - 0.5 * L));
	}

	public float FuncWindStressX(Point pt)
	{
		return (float)WindStress;
	}

	public float FuncWindStressY(Point pt)
	{
		return 0f;
	}
}
