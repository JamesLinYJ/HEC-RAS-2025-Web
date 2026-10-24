using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Native;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine.Interop;

public class CudaSolver : IExplicitSolver, IDisposable
{
	public const double DtMaxIncrease = 0.05;

	public const int nLookahead = 4;

	private static readonly ILogger _logger = LogHelper.GetLogger("CudaSolver");

	private unsafe void* _nativeCudaSolver = null;

	public Action<IExplicitSolver> TimestepDone { get; set; }

	public ComputeOptions ComputeOptions { get; set; }

	public long TMicro { get; private set; }

	public long EndMicro { get; private set; }

	public double Time => (double)TMicro / 1000000.0;

	public long DtMicroGlobal { get; set; }

	public long DtMicroCurrent { get; set; }

	public double DTCurrent => (double)DtMicroCurrent / 1000000.0;

	public ProgressReporter Reporter { get; set; }

	private unsafe void WriteToNativeBuffer<T>(BufferCollectionTarget target, string bufferName, T[] values) where T : unmanaged
	{
		ConversionBase conversionBase = ((typeof(T) == typeof(double)) ? ConversionBase.CB_DOUBLE : ((typeof(T) == typeof(NativeVec2)) ? ConversionBase.CB_VEC2 : ConversionBase.CB_NONE));
		fixed (T* localValue = values)
		{
			void* data = localValue;
			NativeCudaSolver.WriteToBuffer(_nativeCudaSolver, target, bufferName, data, sizeof(T) * values.Length, conversionBase);
		}
	}

	private void WriteNativeMesh(Mesh mesh, MeshConnectivity connectivity, Solver.FaceFlag[] faceFlags)
	{
		NativeVec2[] nativeVec2s = new NativeVec2[mesh.Faces.Length];
		NativeVec2[] nativeVec2sArray = new NativeVec2[mesh.Faces.Length];
		for (int i = 0; i < mesh.Faces.Length; i++)
		{
			Face face = mesh.Faces[i];
			Point point = mesh.FacePoints[face.fpA].Point;
			NativeVec2 nativeVec = new NativeVec2
			{
				x = point.X,
				y = point.Y
			};
			Point point2 = mesh.FacePoints[face.fpB].Point;
			NativeVec2 nativeVec2 = new NativeVec2
			{
				x = point2.X,
				y = point2.Y
			};
			nativeVec2s[i] = nativeVec;
			nativeVec2sArray[i] = nativeVec2;
		}
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "facesPointA", nativeVec2s);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "facesPointB", nativeVec2sArray);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "cellNeighborOffsets", connectivity.CellNeighborOffsets);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "cellNeighborCells", connectivity.CellNeighborCells);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "cellNeighborFaces", connectivity.CellNeighborFaces);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "cellFaceOrientations", connectivity.CellFaceOrientations);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "facesCellA", connectivity.FaceCellA);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "facesCellB", connectivity.FaceCellB);
		WriteToNativeBuffer(BufferCollectionTarget.BT_MESH, "faceFlags", faceFlags);
	}

	private void WriteNativePropertyTables(MeshPropertyTables tables, bool turbulence)
	{
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellTableStart", tables.CellTableStart);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellTableCount", tables.CellTableCount);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellVolumeTable", tables.CellVolumeTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellElevationTable", tables.CellElevationTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellMinElevations", tables.CellMinimumElevations);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceMinElevations", tables.FaceMinimumElevations);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "cellWettedAreaTable", tables.CellWettedAreaTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceTableStart", tables.FaceTableStart);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceTableCount", tables.FaceTableCount);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceAreaTable", tables.FaceAreaTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceElevationTable", tables.FaceElevationTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceWettedPerimeterTable", tables.FaceWettedPerimeterTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceHydraulicRadiusTable", tables.FaceHydraulicRadiusTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceMeanElevation", tables.FaceMeanElevation);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceManningsTable", tables.FaceManningsTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceBedFrictVarTable", tables.FaceBedFrictVarTable);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceBedFrictVarA", tables.FaceBedFrictVarA);
		WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceBedFrictVarB", tables.FaceBedFrictVarB);
		if (turbulence)
		{
			WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceShearFluxVarTable", tables.FaceShearFluxVarTable);
			WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceShearFluxVarA", tables.FaceShearFluxVarA);
			WriteToNativeBuffer(BufferCollectionTarget.BT_PROPERTYTABLES, "faceShearFluxVarB", tables.FaceShearFluxVarB);
		}
	}

	public void WriteNativeInitialWaterState(MeshWaterState ws)
	{
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellArea", ws.CellArea);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellVolume", ws.CellVolume);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellElevation", ws.CellElevation);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellPriorIndexes", ws.CellPriorIndexes);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellHydraulicDepth", ws.CellHydraulicDepth);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceArea", ws.FaceArea);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceFlow", ws.FaceFlow);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceVelocity", ws.FaceVelocity);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceVelMag", ws.FaceVelMag);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceVelTangA", ws.FaceVelTangA);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceVelTangB", ws.FaceVelTangB);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceElevation", ws.FaceElevation);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceHydraulicRadius", ws.FaceHydraulicRadius);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "facePriorIndexes", ws.FacePriorIndexes);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceNonlinFrictCoeff", ws.FaceNonlinFrictCoeff);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "faceShearFluxCoeff", ws.FaceShearFluxCoeff);
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "pinnedFaceStage", ws.PinnedFaceStage);
		NativeVec2[] nativeVec2s = new NativeVec2[ws.CellVelocity.Length];
		for (int i = 0; i < ws.CellVelocity.Length; i++)
		{
			nativeVec2s[i].x = ws.CellVelocity[i].X;
			nativeVec2s[i].y = ws.CellVelocity[i].Y;
		}
		WriteToNativeBuffer(BufferCollectionTarget.BT_WATERSTATE, "cellVelocity", nativeVec2s);
	}

	private void WriteHydraulicMeshComputer(HydraulicMeshComputer hydraulicMesh)
	{
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "cellAreas", hydraulicMesh.CellAreas);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "cellPolygonAreas", hydraulicMesh.CellPolygonAreas);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceLengths", hydraulicMesh.FaceSimpleLengths);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceDistances", hydraulicMesh.FaceRepresentativeDistances);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceInvDistances", hydraulicMesh.FaceRepresentativeInverseDistances);
		NativeVec2[] nativeVec2s = new NativeVec2[hydraulicMesh.Mesh.FaceCount];
		NativeVec2[] nativeVec2sArray = new NativeVec2[hydraulicMesh.Mesh.FaceCount];
		NativeVec2[] nativeVec2s2 = new NativeVec2[hydraulicMesh.Mesh.FaceCount];
		for (int i = 0; i < hydraulicMesh.Mesh.FaceCount; i++)
		{
			nativeVec2s[i].x = hydraulicMesh.FaceNormal[i].X;
			nativeVec2s[i].y = hydraulicMesh.FaceNormal[i].Y;
			nativeVec2sArray[i].x = hydraulicMesh.FaceAlphaCellAInv[i].ValueA;
			nativeVec2sArray[i].y = hydraulicMesh.FaceAlphaCellAInv[i].ValueB;
			nativeVec2s2[i].x = hydraulicMesh.FaceAlpha[i].ValueA;
			nativeVec2s2[i].y = hydraulicMesh.FaceAlpha[i].ValueB;
		}
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceNormals", nativeVec2s);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceAlphaCellAInv", nativeVec2sArray);
		WriteToNativeBuffer(BufferCollectionTarget.BT_HYDRAULICMESHCOMPUTER, "faceAlpha", nativeVec2s2);
	}

	private void WriteNativeLocalCoordinates(LocalCoordinates localCoordinates, Mesh mesh)
	{
		NativeVec2[] nativeVec2s = new NativeVec2[localCoordinates.FaceNormal.Length];
		for (int i = 0; i < 3; i++)
		{
			for (int j = 0; j < mesh.FaceCount; j++)
			{
				Vector vector = localCoordinates.FaceNormal[j, i];
				NativeVec2 nativeVec = new NativeVec2
				{
					x = vector.DeltaX,
					y = vector.DeltaY
				};
				nativeVec2s[i * mesh.FaceCount + j] = nativeVec;
			}
		}
		double[] doubleValues = new double[localCoordinates.CellCurvature.Length];
		for (int k = 0; k < 2; k++)
		{
			for (int l = 0; l < mesh.CellCount; l++)
			{
				doubleValues[k * mesh.CellCount + l] = localCoordinates.CellCurvature[l, k];
			}
		}
		WriteToNativeBuffer(BufferCollectionTarget.BT_LOCALCOORDINATES, "faceNormal", nativeVec2s);
		WriteToNativeBuffer(BufferCollectionTarget.BT_LOCALCOORDINATES, "faceInvDistances", localCoordinates.FaceInvDistances);
		WriteToNativeBuffer(BufferCollectionTarget.BT_LOCALCOORDINATES, "cellCurvature", doubleValues);
	}

	private static string PrettyDriverVersion(int version)
	{
		int versionTerm = (int)Math.Floor((double)version / 1000.0);
		int versionTerm2 = (int)Math.Floor((double)(version - versionTerm * 1000) / 10.0);
		return versionTerm + "." + versionTerm2;
	}

	public unsafe static string TryFindCompatibleGPU()
	{
		CUDACompatibilityResult compatiblity = NativeCudaSolver.GetCompatiblity();
		if (!compatiblity.compatible)
		{
			switch (compatiblity.error)
			{
			case CudaCompatibilityErrorType.NO_CUDA_DEVICE:
				throw new Exception("No CUDA compatible NVIDIA GPU detected");
			case CudaCompatibilityErrorType.NO_DRIVER:
				throw new Exception("No NVIDIA driver detected");
			case CudaCompatibilityErrorType.DRIVER_OUT_OF_DATE:
				throw new Exception("NVIDIA drivers out of date. Your driver supports up to CUDA " + PrettyDriverVersion(compatiblity.driverVersion) + " but the GPU solver requires drivers with CUDA " + PrettyDriverVersion(compatiblity.runtimeVersion));
			case CudaCompatibilityErrorType.COMPUTE_INCAPABLE:
			{
				string computeCapabilityMajorCompatiblityTerm = compatiblity.computeCapabilityMajor + "." + compatiblity.computeCapabilityMinor;
				string requiredComputeCapabilityMajorTerm = compatiblity.requiredComputeCapabilityMajor + "." + compatiblity.requiredComputeCapabilityMinor;
				throw new Exception("Your device only supports up to compute version " + computeCapabilityMajorCompatiblityTerm + " but the GPU solver requires compute capability " + requiredComputeCapabilityMajorTerm);
			}
			case CudaCompatibilityErrorType.QUERY_ERROR:
				throw new Exception($"CUDA query error. Try updating your NVIDIA drivers ({compatiblity.queryError})");
			}
		}
		return Marshal.PtrToStringAnsi((nint)compatiblity.deviceName);
	}

	public unsafe void Init(Mesh mesh, MeshConnectivity connectivity, MeshPropertyTables tables, MeshWaterState ws, HydraulicMeshComputer hmc, LocalCoordinates localCoordinates, Solver.FaceFlag[] faceFlags, List<BoundaryCondition> flowBoundaryConditions, List<BoundaryCondition> stageBoundaryConditions, List<BoundaryCondition> normalDepthBoundaryConditions, SpatiotemporalBoundaryCondition precipBoundaryCondition, SolverControl control, ComputeOptions opts)
	{
		string tryFindCompatibleGPU = TryFindCompatibleGPU();
		Reporter.ReportMessage("Found compatible NVIDIA GPU: " + tryFindCompatibleGPU);
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		ComputeOptions = opts;
		if (_nativeCudaSolver == null)
		{
			_nativeCudaSolver = NativeCudaSolver.CreateSolver(mesh.CellCount, mesh.FaceCount);
		}
		WriteNativeMesh(mesh, connectivity, faceFlags);
		WriteNativePropertyTables(tables, control.Turbulence.IsActive);
		WriteNativeInitialWaterState(ws);
		WriteHydraulicMeshComputer(hmc);
		WriteNativeLocalCoordinates(localCoordinates, mesh);
		NativeCudaSolver.FinishBufferWrites(_nativeCudaSolver);
		NativeCudaSolver.SetControl(_nativeCudaSolver, control.computeControl.grav, control.computeControl.LToSI, control.dzMinAdvection, control.computeControl.nSItoUnits, control.fCoriolis, control.checkMaxElevationChange, control.TimestepSafetyFactor, control.MinAdvDt, control.MaxRatioWetAdvDt, control.Turbulence.IsActive, control.advectionMethod != SolverControl.AdvectionMethod.None, control.dtLimitDiffusion);
		if (control.Turbulence.IsActive)
		{
			NativeCudaSolver.SetTurbulence(_nativeCudaSolver, control.Turbulence.BaseEddyViscosity, control.Turbulence.LongMixCoeff, control.Turbulence.TransMixCoeff, control.Turbulence.HorizMixCoeff);
			if (control.Turbulence.UseCrossDiffusion)
			{
				Reporter.ReportMessage("Momentum cross-diffusion is not implemented on the GPU; running without it.");
			}
		}
		foreach (BoundaryCondition flowBoundaryCondition in flowBoundaryConditions)
		{
			fixed (double* faceFractions = flowBoundaryCondition.FaceFractions)
			{
				fixed (double* timesteps = flowBoundaryCondition.Timesteps)
				{
					fixed (double* values = flowBoundaryCondition.Values)
					{
						fixed (int* faces = flowBoundaryCondition.Faces)
						{
							NativeCudaSolver.AddBoundaryCondition(_nativeCudaSolver, NativeBoundaryConditionType.BC_FLOW, faces, faceFractions, flowBoundaryCondition.Faces.Length, timesteps, values, flowBoundaryCondition.Timesteps.Length, flowBoundaryCondition.EGSlope);
						}
					}
				}
			}
		}
		foreach (BoundaryCondition stageBoundaryCondition in stageBoundaryConditions)
		{
			fixed (double* faceFractions2 = stageBoundaryCondition.FaceFractions)
			{
				fixed (double* timesteps2 = stageBoundaryCondition.Timesteps)
				{
					fixed (double* values2 = stageBoundaryCondition.Values)
					{
						fixed (int* faces2 = stageBoundaryCondition.Faces)
						{
							NativeCudaSolver.AddBoundaryCondition(_nativeCudaSolver, NativeBoundaryConditionType.BC_STAGE, faces2, faceFractions2, stageBoundaryCondition.Faces.Length, timesteps2, values2, stageBoundaryCondition.Timesteps.Length, stageBoundaryCondition.EGSlope);
						}
					}
				}
			}
		}
		foreach (BoundaryCondition normalDepthBoundaryCondition in normalDepthBoundaryConditions)
		{
			fixed (double* faceFractions3 = normalDepthBoundaryCondition.FaceFractions)
			{
				fixed (double* timesteps3 = normalDepthBoundaryCondition.Timesteps)
				{
					fixed (double* values3 = normalDepthBoundaryCondition.Values)
					{
						fixed (int* faces3 = normalDepthBoundaryCondition.Faces)
						{
							NativeCudaSolver.AddBoundaryCondition(_nativeCudaSolver, NativeBoundaryConditionType.BC_NORMALDEPTH, faces3, faceFractions3, normalDepthBoundaryCondition.Faces.Length, timesteps3, values3, normalDepthBoundaryCondition.Timesteps.Length, normalDepthBoundaryCondition.EGSlope);
						}
					}
				}
			}
		}
		if (precipBoundaryCondition != null)
		{
			int cellCount = mesh.CellCount;
			int sourceTimestepLength = precipBoundaryCondition.SourceTimesteps.Length;
			int maxValue = int.MaxValue;
			int scalarForTimestepLength = precipBoundaryCondition.GetScalarForTimestep(0).Length;
			for (int i = 0; i < cellCount; i++)
			{
				maxValue = int.Min(maxValue, precipBoundaryCondition.InterpWeights.CountInfo.Span[i]);
			}
			float[] floatValues = new float[scalarForTimestepLength * sourceTimestepLength];
			for (int j = 0; j < sourceTimestepLength; j++)
			{
				float[] scalarForTimestep = precipBoundaryCondition.GetScalarForTimestep(j);
				for (int k = 0; k < scalarForTimestepLength; k++)
				{
					floatValues[j * scalarForTimestepLength + k] = scalarForTimestep[k];
				}
			}
			int[] intValues = new int[maxValue * cellCount];
			float[] floatValuesArray = new float[maxValue * cellCount];
			for (int l = 0; l < cellCount; l++)
			{
				precipBoundaryCondition.InterpWeights.GetWeightsForElement(l, out var info, out var weights);
				Span<int> infoSpan = info.Span;
				Span<float> weightsSpan = weights.Span;
				for (int m = 0; m < maxValue; m++)
				{
					if (m < intValues.Length)
					{
						intValues[m * cellCount + l] = infoSpan[m];
						floatValuesArray[m * cellCount + l] = weightsSpan[m];
					}
					else
					{
						intValues[m * cellCount + l] = 0;
						floatValuesArray[m * cellCount + l] = 0f;
					}
				}
			}
			fixed (double* sourceTimesteps = precipBoundaryCondition.SourceTimesteps)
			{
				NativeCudaSolver.InitStepIntegratedBC(StepIntegratedBCType.BC_PRECIP, _nativeCudaSolver, sourceTimesteps, precipBoundaryCondition.SourceTimesteps.Length, (void*)Marshal.GetFunctionPointerForDelegate(precipBoundaryCondition.CachedScalarForTimestepPtr));
			}
		}
		NativeCudaSolver.DoPrecalculations(_nativeCudaSolver);
		stopwatch.Stop();
		ReportTimer(stopwatch, "Native CUDA solver init");
	}

	public unsafe ExplicitSolverResult Solve(long durationSeconds, long dt_micro)
	{
		Stopwatch stopwatch = new Stopwatch();
		Stopwatch stopwatch2 = new Stopwatch();
		stopwatch.Start();
		ExplicitSolverResult explicitSolverResult = new ExplicitSolverResult();
		DtMicroCurrent = NativeCudaSolver.ComputeInitTimestep(_nativeCudaSolver, dt_micro);
		TimestepDone?.Invoke(this);
		DtMicroGlobal = dt_micro;
		EndMicro = durationSeconds * 1000000;
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double scaledDtMicroCurrentSum = 0.0;
		long timestepCount = 0L;
		while (TMicro < EndMicro)
		{
			if (Reporter.CancelRequested)
			{
				explicitSolverResult.Status = Status.USER_CANCELLED;
				return explicitSolverResult;
			}
			if (ComputeOptions.UseAdapativeTimestep)
			{
				DtMicroCurrent = LimitMaxDt();
				if (DtMicroCurrent <= 0)
				{
					throw new Exception("Timestep advance zeroed out.");
				}
			}
			else
			{
				DtMicroCurrent = DtMicroGlobal;
			}
			double scaledDtMicroCurrent = (double)DtMicroCurrent / 1000000.0;
			maxValue = Math.Min(maxValue, scaledDtMicroCurrent);
			minValue = Math.Max(minValue, scaledDtMicroCurrent);
			scaledDtMicroCurrentSum += scaledDtMicroCurrent;
			ApplyBoundaryConditions(Time);
			UpdateStepIntegratedBoundaryConditions(Time, DTCurrent);
			ForwardBack(TMicro, DtMicroCurrent);
			timestepCount++;
			TMicro += DtMicroCurrent;
			Reporter?.ReportProgress((int)(100 * TMicro / EndMicro));
			if (TMicro % DtMicroGlobal == 0L)
			{
				stopwatch2.Start();
				TimestepDone?.Invoke(this);
				stopwatch2.Stop();
			}
		}
		TimestepDone?.Invoke(this);
		stopwatch.Stop();
		ReportTimer(stopwatch, "Total solve runtime");
		ReportTimer(stopwatch2, "Timestep reporting/recording ");
		explicitSolverResult.Status = Status.OK;
		explicitSolverResult.MinTimestep = maxValue;
		explicitSolverResult.MaxTimestep = minValue;
		explicitSolverResult.AverageTimestep = scaledDtMicroCurrentSum / (double)timestepCount;
		explicitSolverResult.TimestepRetries = 0;
		explicitSolverResult.TimestepCount = timestepCount;
		return explicitSolverResult;
	}

	public unsafe double ComputeMaxTimestep()
	{
		return NativeCudaSolver.ComputeMaxTimestep(_nativeCudaSolver);
	}

	public long LimitMaxDt()
	{
		long dtMicroGlobalSum = TMicro % DtMicroGlobal;
		if (dtMicroGlobalSum < 0)
		{
			dtMicroGlobalSum += DtMicroGlobal;
		}
		long tMicroDtMicroGlobalTerm = Math.Min(TMicro + (DtMicroGlobal - dtMicroGlobalSum), EndMicro);
		long secondsToMicros = ComputeDriver.SecondsToMicros(Math.Min(ComputeMaxTimestep(), 8640000.0));
		long dtMicroCurrent = DtMicroCurrent;
		long secondsToMicros2 = secondsToMicros;
		if (dtMicroCurrent > 0)
		{
			long secondsToMicros22 = (long)(1.05 * (double)dtMicroCurrent);
			if (secondsToMicros22 < secondsToMicros2)
			{
				secondsToMicros2 = secondsToMicros22;
			}
		}
		long tMicroTerm = tMicroDtMicroGlobalTerm - TMicro;
		if (tMicroTerm < 4 * secondsToMicros2)
		{
			int secondsToMicros2Term = (int)((tMicroTerm - 1) / secondsToMicros2) + 1;
			secondsToMicros2 = tMicroTerm / secondsToMicros2Term;
		}
		return secondsToMicros2;
	}

	public unsafe void ForwardBack(long absTimeMicro, long dtMicro)
	{
		NativeCudaSolver.SolveForwardBack(_nativeCudaSolver, absTimeMicro, dtMicro);
	}

	public unsafe void ApplyBoundaryConditions(double absTime)
	{
		NativeCudaSolver.ApplyBoundaryConditions(_nativeCudaSolver, absTime);
	}

	public unsafe void UpdateStepIntegratedBoundaryConditions(double absTime, double dt)
	{
		NativeCudaSolver.UpdateStepIntegratedBoundaryConditions(_nativeCudaSolver, absTime, dt);
	}

	public unsafe void CopyCellVolume(double[] doubleValues)
	{
		fixed (double* localValue = doubleValues)
		{
			void* dst = localValue;
			NativeCudaSolver.CopyBuffer(_nativeCudaSolver, dst, BufferCollectionTarget.BT_WATERSTATE, "cellVolume", ConversionBase.CB_DOUBLE);
		}
	}

	public unsafe void CopyCellWaterSurface(double[] doubleValues)
	{
		fixed (double* localValue = doubleValues)
		{
			void* dst = localValue;
			NativeCudaSolver.CopyBuffer(_nativeCudaSolver, dst, BufferCollectionTarget.BT_WATERSTATE, "cellElevation", ConversionBase.CB_DOUBLE);
		}
	}

	public unsafe void CopyFaceVelocity(float[] floatValues)
	{
		fixed (float* localValue = floatValues)
		{
			void* dst = localValue;
			NativeCudaSolver.CopyBuffer(_nativeCudaSolver, dst, BufferCollectionTarget.BT_WATERSTATE, "faceVelocity", ConversionBase.CB_NONE);
		}
	}

	public unsafe void Dispose()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		stopwatch.Start();
		NativeCudaSolver.DestroySolver(_nativeCudaSolver);
		stopwatch.Stop();
		ReportTimer(stopwatch, "Dispose");
	}

	public void ReportTimer(Stopwatch timer, string name)
	{
		Reporter.ReportMessage(name + " took : " + timer.Elapsed.ToPrettyString());
	}
}
