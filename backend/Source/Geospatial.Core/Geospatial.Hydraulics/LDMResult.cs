using System;
using Utility.Units;

namespace Geospatial.Hydraulics;

public sealed class LDMResult
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

	public int[] Iterations { get; }

	public float[] FinalVelocityError { get; }

	public bool[] Converged { get; }

	public float[] Dispersion { get; }

	public bool HasAlpha => Alpha.Length != 0;

	public bool HasBeta => Beta.Length != 0;

	public bool HasVelocityDistribution => CellVelocity.Length != 0;

	public bool HasDispersion => Dispersion.Length != 0;

	public float[] GetCellVelocityForFlow(int wseIndex, float targetFlow)
	{
		if (!HasVelocityDistribution)
		{
			throw new InvalidOperationException("The result does not contain velocity distributions.");
		}
		if ((uint)wseIndex >= (uint)SolvedWseCount)
		{
			throw new ArgumentOutOfRangeException("wseIndex");
		}
		if (!float.IsFinite(targetFlow))
		{
			throw new ArgumentOutOfRangeException("targetFlow", "Target flow must be finite.");
		}
		float flow = Flow[wseIndex];
		if (!float.IsFinite(flow))
		{
			throw new InvalidOperationException("The LDM flow must be finite before velocities can be scaled.");
		}
		if (flow == 0f)
		{
			if (targetFlow == 0f)
			{
				return new float[Geometry.CellCount];
			}
			throw new InvalidOperationException("A zero-flow LDM velocity shape cannot be scaled to a nonzero target flow.");
		}
		float targetFlowOverFlow = targetFlow / flow;
		if (!float.IsFinite(targetFlowOverFlow))
		{
			throw new InvalidOperationException("The LDM velocity scale must be finite.");
		}
		float[] cellVelocity = CellVelocity[wseIndex];
		float[] floatValues = new float[cellVelocity.Length];
		for (int i = 0; i < cellVelocity.Length; i++)
		{
			floatValues[i] = cellVelocity[i] * targetFlowOverFlow;
			if (!float.IsFinite(floatValues[i]))
			{
				throw new InvalidOperationException("The scaled cell velocity must be finite.");
			}
		}
		return floatValues;
	}

	internal LDMResult(FaceGeometry geometry, LDMControl control, float[] wseValues)
		: this(geometry, control.Units, wseValues, control.ComputeAlpha, control.ComputeBeta, control.SaveVelocityDistribution, control.ComputeDispersion)
	{
	}

	private LDMResult(FaceGeometry geometry, UnitSystem units, float[] wseValues, bool hasAlpha, bool hasBeta, bool hasVelocityDistribution, bool hasDispersion)
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
		Iterations = new int[Wse.Length];
		FinalVelocityError = new float[Wse.Length];
		Converged = new bool[Wse.Length];
		Dispersion = (hasDispersion ? new float[Wse.Length] : Array.Empty<float>());
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
		if (HasDispersion)
		{
			Dispersion[wseIndex] = 0f;
		}
		Iterations[wseIndex] = 0;
		FinalVelocityError[wseIndex] = 0f;
		Converged[wseIndex] = true;
	}

	public LDMResult Compact(float curvatureTolerance, float interpolationTolerance, bool[] keepMask = null)
	{
		float[][] curves = CompactionCurves();
		keepMask = HydraulicCurveCompactor.BuildKeepMask(Wse, SolvedWseCount, curves, MaximumBedElevation(), curvatureTolerance, interpolationTolerance, keepMask);
		float[] wseValues = HydraulicCurveCompactor.SelectWse(Wse, SolvedWseCount, keepMask);
		LDMResult lDMResult = new LDMResult(Geometry, Units, wseValues, HasAlpha, HasBeta, HasVelocityDistribution, HasDispersion)
		{
			FinalConveyanceError = FinalConveyanceError
		};
		int ordinal = 0;
		for (int i = 0; i < SolvedWseCount; i++)
		{
			if (keepMask[i])
			{
				lDMResult.CopyRow(this, i, ordinal++);
			}
		}
		return lDMResult;
	}

	private float[][] CompactionCurves()
	{
		float[][] floatValues = new float[1 + (HasAlpha ? 1 : 0) + (HasBeta ? 1 : 0) + (HasDispersion ? 1 : 0)][];
		int ordinal = 0;
		floatValues[ordinal++] = Conveyance;
		if (HasAlpha)
		{
			floatValues[ordinal++] = Alpha;
		}
		if (HasBeta)
		{
			floatValues[ordinal++] = Beta;
		}
		if (HasDispersion)
		{
			floatValues[ordinal] = Dispersion;
		}
		return floatValues;
	}

	private void CopyRow(LDMResult source, int sourceIndex, int destinationIndex)
	{
		Area[destinationIndex] = source.Area[sourceIndex];
		WettedPerimeter[destinationIndex] = source.WettedPerimeter[sourceIndex];
		WettedWidth[destinationIndex] = source.WettedWidth[sourceIndex];
		Conveyance[destinationIndex] = source.Conveyance[sourceIndex];
		Iterations[destinationIndex] = source.Iterations[sourceIndex];
		FinalVelocityError[destinationIndex] = source.FinalVelocityError[sourceIndex];
		Converged[destinationIndex] = source.Converged[sourceIndex];
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
		if (HasDispersion)
		{
			Dispersion[destinationIndex] = source.Dispersion[sourceIndex];
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
