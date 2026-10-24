using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility;

namespace Ras.Engine;

public class SecondaryFlowSolver
{
	private const double MinDepth = 0.0001;

	private const double MinVelocity = 1E-08;

	private readonly Mesh _mesh;

	private readonly HydraulicMeshComputer _hmesh;

	private readonly SolverControl _control;

	private readonly SecondaryFlowState _state;

	private readonly List<int> _allCells = new List<int>();

	private readonly ThreadFor _threadFor;

	private MeshWaterState _waterState;

	private MeshPropertyTables _propertyTables;

	private TransportSolver _transport;

	private double _gravity;

	private double _manningConversion;

	private double[] _cellDepth;

	private double[] _faceMannings;

	private double[] _faceRoughnessParameter;

	private double[] _source;

	private double[] _sink;

	private double[] _faceDiffusivity;

	private GradientUVXY[] _velocityGradient;

	private float[] _strainScratch;

	private double[] _dxx;

	private double[] _dxy;

	private double[] _dyy;

	public TransportSolver Transport => _transport;

	public SecondaryFlowState State => _state;

	public SecondaryFlowSolver(Mesh mesh, SolverControl control, SecondaryFlowState state)
	{
		_mesh = mesh;
		_hmesh = mesh.MetadataCache;
		_control = control;
		_state = state;
		_threadFor = new ThreadFor((control.numCores <= 0) ? 1 : control.numCores);
		for (int i = 0; i < mesh.CellCount; i++)
		{
			_allCells.Add(i);
		}
	}

	public void Prepare(Solver host)
	{
		_waterState = host.WaterState;
		_propertyTables = host.PropertyTables;
		_gravity = host.grav;
		_manningConversion = host.nSItoUnits;
		int cellCount = _mesh.CellCount;
		_cellDepth = new double[cellCount];
		_faceMannings = new double[_mesh.FaceCount];
		_faceRoughnessParameter = new double[_mesh.FaceCount];
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			int faceTableStart = _propertyTables.FaceTableStart[i];
			_faceMannings[i] = _manningConversion * (double)_propertyTables.FaceManningsTable[faceTableStart];
		}
		_velocityGradient = new GradientUVXY[cellCount];
		_strainScratch = new float[cellCount];
		_dxx = new double[cellCount];
		_dxy = new double[cellCount];
		_dyy = new double[cellCount];
		if (_control.SecondaryFlow.Model == SecondaryFlowModel.NonEquilibrium)
		{
			_source = new double[cellCount];
			_sink = new double[cellCount];
			_faceDiffusivity = new double[_mesh.FaceCount];
			TransportControl control = new TransportControl
			{
				ThetaImplicit = 1.0,
				advectionScheme = AdvectionScheme.FirstOrderUpwind,
				faceNormalGradientMethod = FaceNormalGradientMethod.OverRelaxation,
				DeriveVolumeChangeFromFlows = true,
				numCores = _control.numCores,
				MatrixSolverPrimary = _control.MatrixSolverPrimary,
				ExplicitTrapezoidalSink = false
			};
			TransportFlowField flow = TransportFlowField.Alias(host.OldState, host.WaterState, null, null, host.CellWaterSources.EnableTransportTracking());
			_transport = ((_control.timeStepping == SolverControl.TimeStepping.Explicit) ? ((TransportSolver)new TransportSolverExp(_mesh, control, flow, 1, new TransportBoundaryConditionCollection())) : ((TransportSolver)new TransportSolverImp(_mesh, control, flow, 1, new TransportBoundaryConditionCollection())));
			_transport.SetCellSourceMassRate(0, _source);
			_transport.SetCellSinkCoefficient(0, _sink);
			_transport.SetFaceDiffusivity(0, _faceDiffusivity);
			_transport.Prepare(host.Reporter);
		}
		UpdateHydraulics();
		Array.Copy(_state.CellEquilibriumIntensity, _state.CellIntensity, cellCount);
		_transport?.SetCellConcentration(0, _state.CellIntensity);
		UpdateDispersion();
	}

	public ITransport.Status SolveStep(double absTime, double dt, out int failureCell)
	{
		failureCell = -1;
		UpdateHydraulics();
		if (_transport != null)
		{
			UpdateTransportCoefficients();
			int count = Math.Max(_control.SecondaryFlow.OuterIterations, 1);
			ITransport.Status status = ITransport.Status.OK;
			for (int i = 0; i < count; i++)
			{
				status = ((i == 0) ? _transport.SolveStep(absTime, dt, out failureCell) : _transport.ResolveStep(absTime, dt, out failureCell));
				if (status != ITransport.Status.OK)
				{
					return status;
				}
			}
			_transport.GetCellConcentration(0).CopyTo(_state.CellIntensity);
		}
		else
		{
			Array.Copy(_state.CellEquilibriumIntensity, _state.CellIntensity, _mesh.CellCount);
		}
		UpdateDispersion();
		return ITransport.Status.OK;
	}

	private void UpdateHydraulics()
	{
		_hmesh.CellComputes.StrainRate(_allCells, _waterState.FaceVelocity, _waterState.FaceVelTangA, _waterState.FaceVelTangB, _waterState.CellVolume, _strainScratch, _velocityGradient);
		UpdateRoughnessParameters();
		SecondaryFlowControl secondaryFlow = _control.SecondaryFlow;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			double cellArea = _hmesh.CellAreas[i];
			double cellDepth = ((cellArea > 0.0) ? (_waterState.CellVolume[i] / cellArea) : 0.0);
			_cellDepth[i] = cellDepth;
			Vector vector = _waterState.CellVelocity[i];
			double x = vector.X;
			double y = vector.Y;
			double computed = Math.Sqrt(x * x + y * y);
			if (cellDepth < 0.0001 || computed < 1E-08)
			{
				_state.CellCurvature[i] = 0.0;
				_state.CellEquilibriumIntensity[i] = 0.0;
				continue;
			}
			GradientUVXY gradient = _velocityGradient[i];
			double cellCurvature = CalculateCurvature(x, y, gradient, cellDepth, cellArea);
			_state.CellCurvature[i] = cellCurvature;
			_state.CellEquilibriumIntensity[i] = secondaryFlow.IntensityFactor * cellDepth * computed * cellCurvature;
		}
	}

	private void UpdateRoughnessParameters()
	{
		float[] faceHydraulicRadius = _waterState.FaceHydraulicRadius;
		double[] faceArea = _waterState.FaceArea;
		_threadFor.Run(_mesh.FaceCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				double hydRad;
				if (faceHydraulicRadius != null)
				{
					hydRad = faceHydraulicRadius[i];
				}
				else
				{
					_propertyTables.GetFaceHydraulicRadiusAtWaterSurface(i, _waterState.FaceElevation[i], out hydRad, out var _);
				}
				_faceRoughnessParameter[i] = SecondaryFlowControl.CalculateRoughnessParameter(_faceMannings[i], hydRad, _gravity);
			}
		});
		_threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				double faceArea2OverFaceRoughnessParameterSum = 0.0;
				double faceAreaSum = 0.0;
				Span<int> faces = _mesh.Cells[i].Faces.Span;
				for (int j = 0; j < faces.Length; j++)
				{
					int faceIndex = faces[j];
					double faceArea2 = faceArea[faceIndex];
					double faceRoughnessParameter = _faceRoughnessParameter[faceIndex];
					if (!(faceArea2 <= 0.0) && !(faceRoughnessParameter <= 0.0) && !double.IsNaN(faceRoughnessParameter))
					{
						faceArea2OverFaceRoughnessParameterSum += faceArea2 / faceRoughnessParameter;
						faceAreaSum += faceArea2;
					}
				}
				double naN = ((faceAreaSum > 0.0) ? (faceArea2OverFaceRoughnessParameterSum / faceAreaSum) : double.NaN);
				ref double reference = ref _state.CellRoughnessParameter[i];
				double positiveInfinity = ((naN > 0.0) ? (1.0 / naN) : ((naN == 0.0) ? double.PositiveInfinity : double.NaN));
				reference = positiveInfinity;
			}
		});
	}

	internal static double CalculateCurvature(double u, double v, GradientUVXY gradient, double depth, double cellArea)
	{
		double computed = Math.Sqrt(u * u + v * v);
		if (depth < 0.0001 || computed < 1E-08 || cellArea <= 0.0)
		{
			return 0.0;
		}
		double dV_DXGradientTerm = (u * u * gradient.DV_DX + u * v * (gradient.DV_DY - gradient.DU_DX) - v * v * gradient.DU_DY) / (computed * computed * computed);
		double depthCellAreaTerm = Math.Min(10.0 * depth, 3.0 * Math.Sqrt(cellArea));
		return Math.Clamp(dV_DXGradientTerm, -1.0 / depthCellAreaTerm, 1.0 / depthCellAreaTerm);
	}

	private void UpdateTransportCoefficients()
	{
		SecondaryFlowControl secondaryFlow = _control.SecondaryFlow;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			double cellDepth = _cellDepth[i];
			double length = _waterState.CellVelocity[i].Length;
			double cellRoughnessParameter = _state.CellRoughnessParameter[i];
			double adaptationLength = secondaryFlow.AdaptationLength(cellDepth, cellRoughnessParameter);
			if (cellDepth < 0.0001 || length < 1E-08 || adaptationLength <= 0.0 || double.IsNaN(cellRoughnessParameter))
			{
				_source[i] = 0.0;
				_sink[i] = 0.0;
			}
			else
			{
				double cellVolumeWaterStateTerm = _waterState.CellVolume[i] * length / adaptationLength;
				_source[i] = cellVolumeWaterStateTerm * _state.CellEquilibriumIntensity[i];
				_sink[i] = 0.0 - cellVolumeWaterStateTerm;
			}
		}
		for (int j = 0; j < _mesh.FaceCount; j++)
		{
			double[] faceDiffusivity = _faceDiffusivity;
			int ordinal = j;
			double diffusionFactor = secondaryFlow.DiffusionFactor;
			float[] faceNuH = _waterState.FaceNuH;
			faceDiffusivity[ordinal] = diffusionFactor * (((double?)((faceNuH != null) ? new float?(faceNuH[j]) : ((float?)null))) ?? 0.0);
		}
	}

	private void UpdateDispersion()
	{
		SecondaryFlowControl secondaryFlow = _control.SecondaryFlow;
		for (int i = 0; i < _mesh.CellCount; i++)
		{
			Vector vector = _waterState.CellVelocity[i];
			double x = vector.X;
			double y = vector.Y;
			double computed = Math.Sqrt(x * x + y * y);
			double cellRoughnessParameter = _state.CellRoughnessParameter[i];
			if (_cellDepth[i] < 0.0001 || computed < 1E-08 || cellRoughnessParameter <= 0.0 || double.IsNaN(cellRoughnessParameter))
			{
				_state.CellSurfaceSecondaryVelocity[i] = new Vector(0.0, 0.0);
				_dxx[i] = (_dxy[i] = (_dyy[i] = 0.0));
				continue;
			}
			double surfaceVelocityCoefficientTerm = SecondaryFlowControl.SurfaceVelocityCoefficient(cellRoughnessParameter) * _state.CellIntensity[i] / computed;
			_state.CellSurfaceSecondaryVelocity[i] = new Vector((0.0 - y) * surfaceVelocityCoefficientTerm, x * surfaceVelocityCoefficientTerm);
			double scaledCellRoughnessParameter = 1.0 / cellRoughnessParameter;
			double dispersionFactorSecondaryFlowTerm = secondaryFlow.DispersionFactor * (5.0 * scaledCellRoughnessParameter - 15.0 * scaledCellRoughnessParameter * scaledCellRoughnessParameter + 37.5 * scaledCellRoughnessParameter * scaledCellRoughnessParameter * scaledCellRoughnessParameter);
			double cellIntensityStateTerm = _state.CellIntensity[i] * dispersionFactorSecondaryFlowTerm / computed;
			_dxx[i] = 2.0 * x * y * cellIntensityStateTerm;
			_dyy[i] = 0.0 - _dxx[i];
			_dxy[i] = (0.0 - (x * x - y * y)) * cellIntensityStateTerm;
		}
		for (int j = 0; j < _mesh.CellCount; j++)
		{
			double computed2 = 0.0;
			double computed3 = 0.0;
			Span<int> faces = _mesh.Cells[j].Faces.Span;
			for (int k = 0; k < faces.Length; k++)
			{
				int faceIndex = faces[k];
				Face face = _mesh.Faces[faceIndex];
				int cellB2 = ((face.cellA == j) ? face.cellB : face.cellA);
				FaceAB faceAB = _hmesh.FaceAlpha[faceIndex];
				double dxx = ((cellB2 >= 0) ? (faceAB.ValueA * _dxx[face.cellA] + faceAB.ValueB * _dxx[face.cellB]) : _dxx[j]);
				double dxy = ((cellB2 >= 0) ? (faceAB.ValueA * _dxy[face.cellA] + faceAB.ValueB * _dxy[face.cellB]) : _dxy[j]);
				double dyy = ((cellB2 >= 0) ? (faceAB.ValueA * _dyy[face.cellA] + faceAB.ValueB * _dyy[face.cellB]) : _dyy[j]);
				double cellDepth = ((cellB2 >= 0) ? (faceAB.ValueA * _cellDepth[face.cellA] + faceAB.ValueB * _cellDepth[face.cellB]) : _cellDepth[j]);
				Vector vector2 = _hmesh.FaceNormal[faceIndex];
				double cellAFaceTerm = ((face.cellA == j) ? 1.0 : (-1.0));
				double faceSimpleLength = _mesh.FaceSimpleLength(faceIndex);
				computed2 += cellAFaceTerm * faceSimpleLength * cellDepth * (dxx * vector2.X + dxy * vector2.Y);
				computed3 += cellAFaceTerm * faceSimpleLength * cellDepth * (dxy * vector2.X + dyy * vector2.Y);
			}
			double cellVolume = _waterState.CellVolume[j];
			_state.CellDispersion[j] = ((cellVolume > 0.0) ? new Vector(computed2 / cellVolume, computed3 / cellVolume) : new Vector(0.0, 0.0));
		}
		for (int l = 0; l < _mesh.FaceCount; l++)
		{
			Face face2 = _mesh.Faces[l];
			int cellA = face2.cellA;
			int cellB = face2.cellB;
			Vector vector3;
			if (cellB < 0)
			{
				vector3 = _state.CellDispersion[cellA];
			}
			else
			{
				FaceAB faceAB2 = _hmesh.FaceAlpha[l];
				vector3 = faceAB2.ValueA * _state.CellDispersion[cellA] + faceAB2.ValueB * _state.CellDispersion[cellB];
			}
			Vector vector4 = _hmesh.FaceNormal[l];
			_state.FaceMomentumDispersion[l] = vector3.X * vector4.X + vector3.Y * vector4.Y;
		}
	}

	public void Cleanup()
	{
		_transport?.Cleanup();
		_threadFor.Dispose();
	}
}
