using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Geospatial;
using Geospatial.Vectors;
using Ras.Engine.Engine;

namespace Ras.Engine;

public class BoundaryConditionCollection : IEquatable<BoundaryConditionCollection>
{
	public readonly List<BoundaryCondition> FlowBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> InternalFlowBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> StageBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> RatingCurveBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> NormalDepthBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> VariableFlowBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> VariableStageBoundaryConditions = new List<BoundaryCondition>();

	public readonly List<BoundaryCondition> SpatiallyVariableStageVelocityBoundaryConditions = new List<BoundaryCondition>();

	public PrecipitationBC Precipitation;

	public EvapotranspirationBC Evapotranspiration;

	public LandSurface LandSurf;

	public AirPressureBC AirPressure;

	public WindBC Wind;

	private readonly Mesh _mesh;

	public double[] CellExcessFlow;

	public int[] CellIndicesInternalFlows;

	public List<int>[] InternalFlowIndices;

	public double[] CellInternalFlows;

	public double[] CellInternalFlowLimited;

	public bool HasPrecipitation => Precipitation != null;

	public bool HasEvapotranspiration => Evapotranspiration != null;

	public bool HasAirPressure => AirPressure != null;

	public bool HasWind => Wind != null;

	public bool HasLandSurface
	{
		get
		{
			if (!HasPrecipitation)
			{
				return HasEvapotranspiration;
			}
			return true;
		}
	}

	public bool HasInternalFlows => InternalFlowBoundaryConditions.Count > 0;

	public BoundaryConditionCollection(Mesh mesh)
	{
		_mesh = mesh;
	}

	public void AddBoundaryCondition(BoundaryCondition condition, BoundaryCondition.BCType type)
	{
		switch (type)
		{
		case BoundaryCondition.BCType.Flow:
			FlowBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.Stage:
			StageBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.RatingCurve:
			RatingCurveBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.NormalDepth:
			NormalDepthBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.VariableFlow:
			VariableFlowBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.VariableStage:
			VariableStageBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.InternalFlow:
			InternalFlowBoundaryConditions.Add(condition);
			return;
		case BoundaryCondition.BCType.SpatiallyVariableStageVelocity:
			SpatiallyVariableStageVelocityBoundaryConditions.Add(condition);
			return;
		}
		throw new SolverException($"Boundary condition type {type} cannot be added to a {"BoundaryConditionCollection"}.");
	}

	public bool Equals(BoundaryConditionCollection other)
	{
		if (FlowBoundaryConditions != other.FlowBoundaryConditions)
		{
			return false;
		}
		if (InternalFlowBoundaryConditions != other.InternalFlowBoundaryConditions)
		{
			return false;
		}
		if (StageBoundaryConditions != other.StageBoundaryConditions)
		{
			return false;
		}
		if (RatingCurveBoundaryConditions != other.RatingCurveBoundaryConditions)
		{
			return false;
		}
		if (VariableFlowBoundaryConditions != other.VariableFlowBoundaryConditions)
		{
			return false;
		}
		if (VariableStageBoundaryConditions != other.VariableStageBoundaryConditions)
		{
			return false;
		}
		if (NormalDepthBoundaryConditions != other.NormalDepthBoundaryConditions)
		{
			return false;
		}
		if (SpatiallyVariableStageVelocityBoundaryConditions != other.SpatiallyVariableStageVelocityBoundaryConditions)
		{
			return false;
		}
		if (Precipitation != other.Precipitation)
		{
			return false;
		}
		if (Evapotranspiration != other.Evapotranspiration)
		{
			return false;
		}
		if (AirPressure != other.AirPressure)
		{
			return false;
		}
		if (Wind != other.Wind)
		{
			return false;
		}
		return true;
	}

	public void SetInternalFlow(int[] cells, double[] cellFractions, double[] timeSteps, double[] values, string name = null)
	{
		int count = values.Length;
		int cellLength = cells.Length;
		double[,] cellValues = new double[count, cellLength];
		for (int i = 0; i < count; i++)
		{
			for (int j = 0; j < cellLength; j++)
			{
				cellValues[i, j] = values[i] * cellFractions[j];
			}
		}
		BoundaryCondition boundaryCondition = new BoundaryCondition
		{
			Name = name,
			Cells = cells,
			CellFractions = cellFractions,
			Timesteps = timeSteps,
			Values = values,
			CellValues = cellValues
		};
		InternalFlowBoundaryConditions.Add(boundaryCondition);
	}

	public void SetPrecipitation(double[] precipTimesteps, Func<int, float[]> getPrecip, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		Precipitation = new PrecipitationBC(precipTimesteps, getPrecip, scaleFactor, cellWeights, _mesh);
	}

	public void SetPrecipitation(DateTime[] precipTimesteps, DateTime time0, Func<int, float[]> getPrecip, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		double[] precipTimesteps2 = precipTimesteps.Select((DateTime t) => (t - time0).TotalSeconds).ToArray();
		SetPrecipitation(precipTimesteps2, getPrecip, scaleFactor, cellWeights);
	}

	public void SetEvapotranspiration(double[] evapTimesteps, Func<int, float[]> getEvap, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		Evapotranspiration = new EvapotranspirationBC(evapTimesteps, getEvap, scaleFactor, cellWeights, _mesh);
	}

	public void SetEvapotranspiration(DateTime[] evapTimesteps, DateTime time0, Func<int, float[]> getEvap, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		double[] evapTimesteps2 = evapTimesteps.Select((DateTime t) => (t - time0).TotalSeconds).ToArray();
		SetEvapotranspiration(evapTimesteps2, getEvap, scaleFactor, cellWeights);
	}

	public void SetAirPressure(double[] airPresRelTimes, Func<int, float[]> getAirPres, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		AirPressure = new AirPressureBC(airPresRelTimes, getAirPres, scaleFactor, cellWeights, _mesh);
	}

	public void SetAirPressure(DateTime[] airPresTimesteps, DateTime time0, Func<int, float[]> getAirPres, float scaleFactor, JaggedWeights<float> cellWeights)
	{
		double[] airPresRelTimes = airPresTimesteps.Select((DateTime t) => (t - time0).TotalSeconds).ToArray();
		SetAirPressure(airPresRelTimes, getAirPres, scaleFactor, cellWeights);
	}

	public void SetWind(double[] windRelTimes, Func<int, Vector2[]> getWind, Func<int, float> getAirDensity, float scaleFactor, JaggedWeights<float> faceWeights, bool isLagrangian, bool isWindStress)
	{
		Wind = new WindBC(windRelTimes, getWind, getAirDensity, scaleFactor, faceWeights, _mesh, isLagrangian, isWindStress);
	}

	public void SetWind(DateTime[] windTimesteps, DateTime time0, Func<int, Vector2[]> getWind, Func<int, float> getAirDensity, float scaleFactor, JaggedWeights<float> faceWeights, bool isLagrangian, bool isWindStress)
	{
		double[] windRelTimes = windTimesteps.Select((DateTime t) => (t - time0).TotalSeconds).ToArray();
		SetWind(windRelTimes, getWind, getAirDensity, scaleFactor, faceWeights, isLagrangian, isWindStress);
	}

	public void Prepare(double LToSI)
	{
		if (HasPrecipitation)
		{
			Precipitation.Prepare();
		}
		if (HasEvapotranspiration)
		{
			Evapotranspiration.Prepare();
		}
		if (HasLandSurface)
		{
			LandSurf = new LandSurface(_mesh);
			LandSurf.Prepare();
			CellExcessFlow = LandSurf.CellExcessFlow;
		}
		if (HasAirPressure)
		{
			AirPressure.Prepare();
		}
		if (HasWind)
		{
			Wind.Prepare(LToSI);
		}
		if (!HasInternalFlows)
		{
			return;
		}
		int cellCount = _mesh.CellCount;
		InternalFlowIndices = new List<int>[cellCount];
		for (int i = 0; i < cellCount; i++)
		{
			InternalFlowIndices[i] = new List<int>();
		}
		CellInternalFlows = new double[cellCount];
		CellInternalFlowLimited = new double[cellCount];
		List<int> intList = new List<int>();
		for (int j = 0; j < InternalFlowBoundaryConditions.Count; j++)
		{
			int[] cells = InternalFlowBoundaryConditions[j].Cells;
			for (int k = 0; k < cells.Length; k++)
			{
				int cellIndex = cells[k];
				if (InternalFlowIndices[cells[k]].Count == 0)
				{
					intList.Add(cellIndex);
				}
				InternalFlowIndices[cells[k]].Add(j);
			}
		}
		CellIndicesInternalFlows = intList.ToArray();
	}

	public bool TryGetInternalFlow(int cellIdx, out double flow)
	{
		if (!HasInternalFlows)
		{
			flow = 0.0;
			return false;
		}
		flow = CellInternalFlowLimited[cellIdx];
		return true;
	}

	public void UpdateStepIntegrated(double absTime, double dt, float[] cellDepth, double[] cellArea)
	{
		double[] cellPrecipRate = (HasPrecipitation ? Precipitation.CellPrecipRate : null);
		double[] cellEvapotransRate = (HasEvapotranspiration ? Evapotranspiration.CellEvapotransRate : null);
		if (HasPrecipitation)
		{
			Precipitation.Update(absTime, dt);
		}
		if (HasEvapotranspiration)
		{
			Evapotranspiration.Update(absTime, dt, cellPrecipRate, cellDepth, cellArea);
		}
		if (HasLandSurface)
		{
			LandSurf.Update(cellPrecipRate, cellEvapotransRate);
		}
	}

	public void UpdateStepAverage(double absTime, Memory<double> faceInvDist, Geospatial.Vectors.Vector[] cellVelocity)
	{
		if (HasAirPressure)
		{
			AirPressure.Update(absTime, faceInvDist);
		}
		if (HasWind)
		{
			Wind.Update(absTime, cellVelocity);
		}
	}
}
