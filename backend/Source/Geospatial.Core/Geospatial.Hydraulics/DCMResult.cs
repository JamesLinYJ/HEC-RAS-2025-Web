using System;
using Utility.Units;

namespace Geospatial.Hydraulics;

public sealed class DCMResult
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

	public float[] Flow { get; }

	public float[] Alpha { get; }

	public float[] Beta { get; }

	public float[][] CellVelocity { get; }

	public bool HasAlpha => Alpha.Length != 0;

	public bool HasBeta => Beta.Length != 0;

	public bool HasVelocityDistribution => CellVelocity.Length != 0;

	internal DCMResult(FaceGeometry geometry, DCMControl control, float[] wseValues)
		: this(geometry, control.Units, wseValues, control.ComputeAlpha, control.ComputeBeta, control.SaveVelocityDistribution)
	{
	}

	private DCMResult(FaceGeometry geometry, UnitSystem units, float[] wseValues, bool hasAlpha, bool hasBeta, bool hasVelocityDistribution)
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
		Alpha = (hasAlpha ? new float[Wse.Length] : Array.Empty<float>());
		Beta = (hasBeta ? new float[Wse.Length] : Array.Empty<float>());
		Flow = (hasVelocityDistribution ? new float[Wse.Length] : Array.Empty<float>());
		CellVelocity = (hasVelocityDistribution ? new float[Wse.Length][] : Array.Empty<float[]>());
	}

	internal void SetDry(int wseIndex)
	{
		Area[wseIndex] = 0f;
		WettedPerimeter[wseIndex] = 0f;
		WettedWidth[wseIndex] = 0f;
		Conveyance[wseIndex] = 0f;
		if (HasAlpha)
		{
			Alpha[wseIndex] = 1f;
		}
		if (HasBeta)
		{
			Beta[wseIndex] = 1f;
		}
		if (HasVelocityDistribution)
		{
			Flow[wseIndex] = 0f;
			CellVelocity[wseIndex] = new float[Geometry.CellCount];
		}
	}

	public DCMResult Compact(float curvatureTolerance, float interpolationTolerance, bool[] keepMask = null)
	{
		float[][] curves = CompactionCurves();
		keepMask = HydraulicCurveCompactor.BuildKeepMask(Wse, SolvedWseCount, curves, MaximumBedElevation(), curvatureTolerance, interpolationTolerance, keepMask);
		float[] wseValues = HydraulicCurveCompactor.SelectWse(Wse, SolvedWseCount, keepMask);
		DCMResult dCMResult = new DCMResult(Geometry, Units, wseValues, HasAlpha, HasBeta, HasVelocityDistribution)
		{
			FinalConveyanceError = FinalConveyanceError
		};
		int ordinal = 0;
		for (int i = 0; i < SolvedWseCount; i++)
		{
			if (keepMask[i])
			{
				dCMResult.CopyRow(this, i, ordinal++);
			}
		}
		return dCMResult;
	}

	private float[][] CompactionCurves()
	{
		float[][] floatValues = new float[1 + (HasAlpha ? 1 : 0) + (HasBeta ? 1 : 0)][];
		int ordinal = 0;
		floatValues[ordinal++] = Conveyance;
		if (HasAlpha)
		{
			floatValues[ordinal++] = Alpha;
		}
		if (HasBeta)
		{
			floatValues[ordinal] = Beta;
		}
		return floatValues;
	}

	private void CopyRow(DCMResult source, int sourceIndex, int destinationIndex)
	{
		Area[destinationIndex] = source.Area[sourceIndex];
		WettedPerimeter[destinationIndex] = source.WettedPerimeter[sourceIndex];
		WettedWidth[destinationIndex] = source.WettedWidth[sourceIndex];
		Conveyance[destinationIndex] = source.Conveyance[sourceIndex];
		if (HasAlpha)
		{
			Alpha[destinationIndex] = source.Alpha[sourceIndex];
		}
		if (HasBeta)
		{
			Beta[destinationIndex] = source.Beta[sourceIndex];
		}
		if (HasVelocityDistribution)
		{
			Flow[destinationIndex] = source.Flow[sourceIndex];
			CellVelocity[destinationIndex] = source.CellVelocity[sourceIndex];
		}
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
