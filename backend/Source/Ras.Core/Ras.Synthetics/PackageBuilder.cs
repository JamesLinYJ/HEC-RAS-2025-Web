using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;
using Utility.Logging;
using Utility.Units;

namespace Ras.Synthetics;

public static class PackageBuilder
{
	public static ComputePackage Build(BaseParams p, SolverControl controlOverride = null, UnitSystem units = UnitSystem.SI, SolverControl.EquationSet equationSet = SolverControl.EquationSet.SWE, SolverControl.TimeStepping timeStepping = SolverControl.TimeStepping.Explicit)
	{
		BuildCommon(p, controlOverride, units, equationSet, timeStepping, out var mesh, out var props, out var elevs, out var nvals, out var solverControl, out var initialState);
		Solver solver = (Solver)solverControl.SolverFactory(null, mesh, props, initialState);
		AddBoundaryConditions(p, solver.BoundaryConditions, mesh, elevs);
		p.AddStructures(solver.HydraulicStructures, mesh);
		solver.HydraulicStructures.IdentifyStructureCellsAndFaces(mesh);
		return new ComputePackage(solver, mesh, props, elevs, nvals);
	}

	public static ComputePackage BuildDriver(BaseParams p, SolverControl controlOverride = null, UnitSystem units = UnitSystem.SI, SolverControl.EquationSet equationSet = SolverControl.EquationSet.SWE, SolverControl.TimeStepping timeStepping = SolverControl.TimeStepping.Explicit)
	{
		BuildCommon(p, controlOverride, units, equationSet, timeStepping, out var mesh, out var props, out var elevs, out var nvals, out var solverControl, out var initialState);
		ComputeDriver computeDriver = new ComputeDriver(mesh, props, initialState, solverControl.computeControl, solverControl);
		AddBoundaryConditions(p, computeDriver.BoundaryConditions, mesh, elevs);
		p.AddStructures(computeDriver.HydraulicStructures, mesh);
		computeDriver.HydraulicStructures.IdentifyStructureCellsAndFaces(mesh);
		return new ComputePackage(computeDriver, mesh, props, elevs, nvals);
	}

	public static ComputePackage BuildMultiSolverDriver(BaseParams p, IList<SolverControl> controls, UnitSystem units = UnitSystem.SI)
	{
		BuildCommon(p, controls[0], units, SolverControl.EquationSet.SWE, SolverControl.TimeStepping.Explicit, out var mesh, out var props, out var elevs, out var nvals, out var _, out var initialState);
		for (int i = 1; i < controls.Count; i++)
		{
			controls[i].UsePartialGrid = false;
			p.InitSolverControl(controls[i]);
		}
		int[] cellMapping = p.GetCellMapping(mesh) ?? throw new Exception(p.GetType().Name + ".GetCellMapping returned null; required for multi-solver driver.");
		if (cellMapping.Length != mesh.CellCount)
		{
			throw new Exception($"GetCellMapping length {cellMapping.Length} != mesh.CellCount {mesh.CellCount}.");
		}
		ComputeDriver computeDriver = new ComputeDriver(mesh, props, initialState, new ComputeControl(), controls.ToList(), cellMapping);
		AddBoundaryConditions(p, computeDriver.BoundaryConditions, mesh, elevs);
		p.AddStructures(computeDriver.HydraulicStructures, mesh);
		computeDriver.HydraulicStructures.IdentifyStructureCellsAndFaces(mesh);
		return new ComputePackage(computeDriver, mesh, props, elevs, nvals);
	}

	private static void BuildCommon(BaseParams p, SolverControl controlOverride, UnitSystem units, SolverControl.EquationSet equationSet, SolverControl.TimeStepping timeStepping, out Mesh mesh, out MeshPropertyTables props, out IResample<float> elevs, out IResample<float> nvals, out SolverControl solverControl, out MeshWaterState initialState)
	{
		mesh = p.CreateMesh();
		if (mesh == null || !mesh.IsValid())
		{
			throw new Exception("Mesh generation problem.");
		}
		Extent ext = mesh.Extent.Extend(0.1);
		elevs = p.TerrainResampler() ?? new FunctionalResampler<float>(ext, -9999f, p.TerrainElevation);
		nvals = p.NValueResampler() ?? new FunctionalResampler<float>(ext, -9999f, p.SpatiallyVaryingNValue);
		OperationResult operationResult = MeshPropertyTables.ComputeFrom(mesh, elevs, nvals, PropertyTableOptions.Default(units), out props);
		if (!operationResult)
		{
			throw new Exception("Could not compute property tables: " + operationResult.GetConcatenatedMessages());
		}
		solverControl = controlOverride ?? new SolverControl(units, equationSet, timeStepping);
		solverControl.UsePartialGrid = false;
		p.InitSolverControl(solverControl);
		initialState = new MeshWaterState(mesh, props);
		p.UpdateInitialState(initialState, mesh, props);
		ISpatialTransformer transformer = p.GetTransformer(mesh);
		if (transformer != null)
		{
			mesh = mesh.Transform(transformer);
			elevs = elevs.WithTransformer(transformer);
			nvals = nvals.WithTransformer(transformer);
		}
	}

	private static void AddBoundaryConditions(BaseParams p, BoundaryConditionCollection bcs, Mesh mesh, IResample<float> elevations)
	{
		List<BoundaryConditionLine> bCLines = p.GetBCLines(null);
		if (bCLines != null)
		{
			TranslateBCLines(p, bCLines, bcs, mesh, elevations);
		}
		p.AddCustomEngineBCs(bcs, mesh);
	}

	private static void TranslateBCLines(BaseParams p, IEnumerable<BoundaryConditionLine> bcLines, BoundaryConditionCollection bcs, Mesh mesh, IResample<float> elevations)
	{
		foreach (BoundaryConditionLine bcLine in bcLines)
		{
			if (bcLine?.Polyline == null || bcLine.Payloads == null)
			{
				continue;
			}
			foreach (PayloadBase payload in bcLine.Payloads)
			{
				if (!IsHydraulicPayload(payload, out var variable))
				{
					continue;
				}
				List<ProfileFaceRange> perimeterFacesAlongPolyline = mesh.PerimeterFacesAlongPolyline(bcLine.Polyline, out var _);
				int[] faces = perimeterFacesAlongPolyline.Select((ProfileFaceRange faceRange) => faceRange.Face).ToArray();
				if (faces.Length == 0)
				{
					throw new Exception("BC line '" + bcLine.Name + "' did not match any perimeter faces.");
				}
				BoundaryNodeBedElevationSampler.TrySample(mesh, perimeterFacesAlongPolyline, elevations, out var elevations2);
				Ras.Engine.BoundaryCondition boundaryCondition;
				if (payload is ConstantPayload constantPayload)
				{
					boundaryCondition = Ras.Engine.BoundaryCondition.ConstantFaceValue(faces, constantPayload.Value);
				}
				else
				{
					if (!(payload is TimeSeriesPayload { Values: var values } timeSeriesPayload))
					{
						if (payload is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload)
						{
							double[] relativeTimeSeconds = spatiallyVariableTimeSeriesPayload.TimeSeriesDefinition.GetRelativeTimeSeconds(spatiallyVariableTimeSeriesPayload.Values.Count, DateTime.MinValue);
							int count = faces.Length;
							FaceStationSpan[] faceSpans = SpatiallyVariablePayloadInterpolator.FaceStationsAlongLine(mesh, bcLine.Polyline, perimeterFacesAlongPolyline);
							switch (variable)
							{
							case "Stage":
							case "Flow":
							{
								OperationResult operationResult = spatiallyVariableTimeSeriesPayload.ValidateForCompute();
								if (!operationResult)
								{
									throw new Exception(operationResult.GetConcatenatedMessages(" | "));
								}
								double[,] faceValues = SpatiallyVariablePayloadInterpolator.BuildFaceValues(spatiallyVariableTimeSeriesPayload, relativeTimeSeconds, faceSpans);
								boundaryCondition = Ras.Engine.BoundaryCondition.VariableFaceValues(faces, relativeTimeSeconds, faceValues);
								boundaryCondition.Name = bcLine.Name;
								ConfigureFlowDistribution(boundaryCondition, bcLine, elevations2);
								p.ConfigureEngineBC(boundaryCondition, bcLine, variable);
								AddSpatialByVariable(bcs, boundaryCondition, variable);
								continue;
							}
							case "Normal Depth":
								throw new NotSupportedException("Normal-depth BCs do not support spatially variable time series.");
							}
							if (variable != "StageVelocityVelocity")
							{
								throw new NotImplementedException($"Boundary Condition Line '{bcLine.Name}' has unsupported payload variable '{spatiallyVariableTimeSeriesPayload.Variable}'.");
							}
							double[,,] faceMultipleValues = new double[relativeTimeSeconds.Length, count, 3];
							double[] stations = spatiallyVariableTimeSeriesPayload.Stations.ToArray();
							for (int intervalIndex = 0; intervalIndex < relativeTimeSeconds.Length; intervalIndex++)
							{
								double[] payloadValues = spatiallyVariableTimeSeriesPayload.Values[intervalIndex].ToArray();
								double[] interpolateBlockToFaces = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans);
								double[] interpolateBlockToFacesArray = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans, spatiallyVariableTimeSeriesPayload.StationCount);
								double[] interpolateBlockToFaces2 = SpatiallyVariablePayloadInterpolator.InterpolateBlockToFaces(stations, payloadValues, faceSpans, spatiallyVariableTimeSeriesPayload.StationCount * 2);
								for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
								{
									faceMultipleValues[intervalIndex, intervalIndex2, 0] = interpolateBlockToFaces[intervalIndex2];
									faceMultipleValues[intervalIndex, intervalIndex2, 1] = interpolateBlockToFacesArray[intervalIndex2];
									faceMultipleValues[intervalIndex, intervalIndex2, 2] = interpolateBlockToFaces2[intervalIndex2];
								}
							}
							boundaryCondition = new Ras.Engine.BoundaryCondition
							{
								Name = bcLine.Name,
								Faces = faces,
								FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray(),
								Timesteps = relativeTimeSeconds,
								FaceMultipleValues = faceMultipleValues
							};
							ConfigureFlowDistribution(boundaryCondition, bcLine, elevations2);
							bcs.SpatiallyVariableStageVelocityBoundaryConditions.Add(boundaryCondition);
							p.ConfigureEngineBC(boundaryCondition, bcLine, variable);
							continue;
						}
						throw new NotImplementedException("Payload type " + payload.GetType().Name + " not supported by PackageBuilder.");
					}
					double[] doubleValues = values.ToArray();
					boundaryCondition = new Ras.Engine.BoundaryCondition
					{
						Faces = faces,
						FaceFractions = Enumerable.Repeat(1.0 / (double)faces.Length, faces.Length).ToArray(),
						Values = doubleValues,
						Timesteps = timeSeriesPayload.TimeSeriesDefinition.GetRelativeTimeSeconds(doubleValues.Length, DateTime.MinValue)
					};
				}
				boundaryCondition.Name = bcLine.Name;
				ConfigureFlowDistribution(boundaryCondition, bcLine, elevations2);
				p.ConfigureEngineBC(boundaryCondition, bcLine, variable);
				AddByVariable(bcs, boundaryCondition, variable);
			}
		}
	}

	private static void ConfigureFlowDistribution(Ras.Engine.BoundaryCondition engineBc, BoundaryConditionLine bcl, float[] boundaryNodeBedElevations)
	{
		engineBc.UseLateralDistribution = bcl.FlowDistributionMethod == BoundaryFlowDistributionMethod.LateralDistribution;
		engineBc.BoundaryNodeBedElevations = boundaryNodeBedElevations;
	}

	private static bool IsHydraulicPayload(PayloadBase payload, out string variable)
	{
		variable = payload?.Variable;
		if (!(variable == "Stage") && !(variable == "Flow") && !(variable == "Normal Depth"))
		{
			return variable == "StageVelocityVelocity";
		}
		return true;
	}

	private static void AddByVariable(BoundaryConditionCollection bcs, Ras.Engine.BoundaryCondition engineBc, string variable)
	{
		switch (variable)
		{
		case "Stage":
			bcs.StageBoundaryConditions.Add(engineBc);
			break;
		case "Flow":
			bcs.FlowBoundaryConditions.Add(engineBc);
			break;
		case "Normal Depth":
			bcs.NormalDepthBoundaryConditions.Add(engineBc);
			break;
		default:
			throw new NotImplementedException("Hydraulic variable '" + variable + "' not supported.");
		}
	}

	private static void AddSpatialByVariable(BoundaryConditionCollection bcs, Ras.Engine.BoundaryCondition engineBc, string variable)
	{
		if (variable == "Stage")
		{
			bcs.VariableStageBoundaryConditions.Add(engineBc);
			return;
		}
		if (variable == "Flow")
		{
			bcs.VariableFlowBoundaryConditions.Add(engineBc);
			return;
		}
		throw new NotSupportedException("Hydraulic variable '" + variable + "' does not support spatially variable data.");
	}
}
