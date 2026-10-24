using System;
using Utility.Units;

namespace Geospatial.Hydraulics;

public sealed class SCMResult
{
	public string Name { get; }

	public UnitSystem Units { get; }

	public FaceGeometry Geometry { get; }

	public int RequestedWseCount => Wse.Length;

	public int SolvedWseCount { get; internal set; }

	public bool StoppedEarly => SolvedWseCount < RequestedWseCount;

	public float FinalConveyanceError { get; internal set; } = float.NaN;

	public float[] Wse { get; }

	public float[] Area { get; }

	public float[] WettedPerimeter { get; }

	public float[] WettedWidth { get; }

	public float[] Conveyance { get; }

	internal SCMResult(FaceGeometry geometry, SCMControl control, float[] wseValues)
		: this(geometry, control.Units, wseValues)
	{
	}

	private SCMResult(FaceGeometry geometry, UnitSystem units, float[] wseValues)
	{
		Name = geometry.Name;
		Units = units;
		Geometry = geometry;
		Wse = (float[])wseValues.Clone();
		SolvedWseCount = Wse.Length;
		Area = new float[Wse.Length];
		WettedPerimeter = new float[Wse.Length];
		WettedWidth = new float[Wse.Length];
		Conveyance = new float[Wse.Length];
	}

	internal void SetDry(int wseIndex)
	{
		Area[wseIndex] = 0f;
		WettedPerimeter[wseIndex] = 0f;
		WettedWidth[wseIndex] = 0f;
		Conveyance[wseIndex] = 0f;
	}

	public SCMResult Compact(float curvatureTolerance, float interpolationTolerance, bool[] keepMask = null)
	{
		float[][] curves = new float[1][] { Conveyance };
		keepMask = HydraulicCurveCompactor.BuildKeepMask(Wse, SolvedWseCount, curves, MaximumBedElevation(), curvatureTolerance, interpolationTolerance, keepMask);
		float[] wseValues = HydraulicCurveCompactor.SelectWse(Wse, SolvedWseCount, keepMask);
		SCMResult sCMResult = new SCMResult(Geometry, Units, wseValues)
		{
			FinalConveyanceError = FinalConveyanceError
		};
		int ordinal = 0;
		for (int i = 0; i < SolvedWseCount; i++)
		{
			if (keepMask[i])
			{
				sCMResult.CopyRow(this, i, ordinal++);
			}
		}
		return sCMResult;
	}

	private void CopyRow(SCMResult source, int sourceIndex, int destinationIndex)
	{
		Area[destinationIndex] = source.Area[sourceIndex];
		WettedPerimeter[destinationIndex] = source.WettedPerimeter[sourceIndex];
		WettedWidth[destinationIndex] = source.WettedWidth[sourceIndex];
		Conveyance[destinationIndex] = source.Conveyance[sourceIndex];
	}

	private float MaximumBedElevation()
	{
		float faceBedElevation = Geometry.FaceBedElevations[0];
		for (int i = 1; i <= Geometry.CellCount; i++)
		{
			faceBedElevation = MathF.Max(faceBedElevation, Geometry.FaceBedElevations[i]);
		}
		return faceBedElevation;
	}
}
