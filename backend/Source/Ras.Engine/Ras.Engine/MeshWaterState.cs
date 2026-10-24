using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.Extensions;
using Utility.Memory;
using Utility.Progress;
using Utility.Systems;

namespace Ras.Engine;

public class MeshWaterState : IHotStartState
{
	protected Mesh _mesh;

	protected MeshPropertyTables _props;

	protected HydraulicMeshComputer _hmesh;

	public int SourceCount;

	public long dtMicros;

	public long tMicros;

	[Expose(MeshElementType.Cell)]
	public double[] CellVolume;

	public double[] CellElevation;

	private Geospatial.Vectors.Vector[] _cellWaterSurfaceGradients;

	private HydraulicMeshComputer.ScalarGradientWlsWorkspace _cellWaterSurfaceGradientWorkspace;

	private double[] _nodeFaceWaterSurfaceValues;

	private HydraulicMeshComputer.NodeFaceInterpolationWorkspace _nodeFaceWaterSurfaceInterpolation;

	private HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace _waterSurfaceBoundaryReconstruction;

	public double[] FaceVelocity;

	[Expose(MeshElementType.Face)]
	public double[] FaceArea;

	public double[] SourceFlows;

	public double[] CellArea;

	public float[] CellHydraulicDepth;

	public Geospatial.Vectors.Vector[] CellVelocity;

	public bool ErrorCellVolumeNegative;

	public int[] CellPriorIndexes;

	[Expose(MeshElementType.Face)]
	public double[] FaceElevation;

	[Expose(MeshElementType.Face)]
	public double[] FaceFlow;

	public double[] FaceVelTangA;

	public double[] FaceVelTangB;

	public float[] FaceVelMag;

	public float[] FaceNonlinFrictCoeff;

	public float[] FaceShearFluxCoeff;

	public double[] PinnedFaceStage;

	public float[] FaceHydraulicRadius;

	public float[] FaceHydraulicDepth;

	public double[] FaceConveyance;

	public float[] FaceMannings;

	public int[] FacePriorIndexes;

	public TurbulenceState turb;

	public SecondaryFlowState secondaryFlow;

	public byte[] CellLevel0Lts;

	[Expose(MeshElementType.Cell)]
	public byte[] CellLevel2Lts;

	public byte[] CellLevel4Lts;

	public byte[] FaceLevel1Lts;

	public byte[] FaceLevel3Lts;

	[Expose(MeshElementType.Cell)]
	public float[] CellDtMaxAdvection;

	[Expose(MeshElementType.Cell)]
	public float[] CellDtMaxDiffusion;

	[Expose(MeshElementType.Cell)]
	public float[] CellDtMaxCelerity;

	public int CellCount => _mesh.CellCount;

	public int FaceCount => _mesh.FaceCount;

	public double Time => ComputeDriver.MicrosToSeconds(tMicros);

	public bool CellWaterSurfaceGradientsUpdated { get; private set; }

	public bool NodeFaceWaterSurfaceValuesUpdated { get; private set; }

	public HydraulicMeshComputer.MultidimensionalSlopeLimiter CellWaterSurfaceGradientLimiter { get; private set; }

	public HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace WaterSurfaceBoundaryReconstruction => _waterSurfaceBoundaryReconstruction;

	public Geospatial.Vectors.Vector[] CellWaterSurfaceGradients
	{
		get
		{
			if (!CellWaterSurfaceGradientsUpdated)
			{
				throw new InvalidOperationException("Cell water-surface gradients are not current.");
			}
			return _cellWaterSurfaceGradients;
		}
	}

	public double[] NodeFaceWaterSurfaceValues
	{
		get
		{
			if (!NodeFaceWaterSurfaceValuesUpdated)
			{
				throw new InvalidOperationException("Node-face water-surface values are not current.");
			}
			return _nodeFaceWaterSurfaceValues;
		}
	}

	public HydraulicMeshComputer.NodeFaceInterpolationWorkspace NodeFaceWaterSurfaceInterpolation
	{
		get
		{
			if (!NodeFaceWaterSurfaceValuesUpdated)
			{
				throw new InvalidOperationException("Node-face water-surface values are not current.");
			}
			return _nodeFaceWaterSurfaceInterpolation;
		}
	}

	double[] IHotStartState.CellElevation => CellElevation;

	double[] IHotStartState.FaceVelocity => FaceVelocity;

	long IHotStartState.dtMicros => dtMicros;

	public float[] FaceNuH => turb?.faceEddyVis;

	public void UpdateFrom(MeshWaterState src)
	{
		if (src != this)
		{
			bool cellWaterSurfaceGradientsUpdated = src.CellWaterSurfaceGradientsUpdated;
			bool nodeFaceWaterSurfaceValuesUpdated = src.NodeFaceWaterSurfaceValuesUpdated;
			PrepareWaterSurfaceCacheCopy(src, cellWaterSurfaceGradientsUpdated, nodeFaceWaterSurfaceValuesUpdated);
			UpdateFromThreadCore(src, 0.0, 1.0, cellWaterSurfaceGradientsUpdated, nodeFaceWaterSurfaceValuesUpdated);
			CellWaterSurfaceGradientsUpdated = cellWaterSurfaceGradientsUpdated;
			NodeFaceWaterSurfaceValuesUpdated = nodeFaceWaterSurfaceValuesUpdated;
			if (cellWaterSurfaceGradientsUpdated)
			{
				CellWaterSurfaceGradientLimiter = src.CellWaterSurfaceGradientLimiter;
			}
			CopyWaterSurfaceBoundaryState(src);
		}
	}

	public void UpdateFrom(MeshWaterState src, ThreadFor threadFor)
	{
		if (src != this)
		{
			bool copyCellWaterSurfaceGradients = src.CellWaterSurfaceGradientsUpdated;
			bool copyNodeFaceWaterSurfaceValues = src.NodeFaceWaterSurfaceValuesUpdated;
			PrepareWaterSurfaceCacheCopy(src, copyCellWaterSurfaceGradients, copyNodeFaceWaterSurfaceValues);
			threadFor.Run(10000, (int begin, int end) =>
			{
				double fBegin = (double)begin / 10000.0;
				double fEnd = (double)(end + 1) / 10000.0;
				UpdateFromThreadCore(src, fBegin, fEnd, copyCellWaterSurfaceGradients, copyNodeFaceWaterSurfaceValues);
			});
			CellWaterSurfaceGradientsUpdated = copyCellWaterSurfaceGradients;
			NodeFaceWaterSurfaceValuesUpdated = copyNodeFaceWaterSurfaceValues;
			if (copyCellWaterSurfaceGradients)
			{
				CellWaterSurfaceGradientLimiter = src.CellWaterSurfaceGradientLimiter;
			}
			CopyWaterSurfaceBoundaryState(src);
		}
	}

	public void UpdateFrom<T>(MeshWaterState src, ThreadFor threadFor) where T : IWaterStateConfig
	{
		if (src != this)
		{
			bool copyCellWaterSurfaceGradients = src.CellWaterSurfaceGradientsUpdated;
			bool copyNodeFaceWaterSurfaceValues = src.NodeFaceWaterSurfaceValuesUpdated;
			PrepareWaterSurfaceCacheCopy(src, copyCellWaterSurfaceGradients, copyNodeFaceWaterSurfaceValues);
			threadFor.Run(10000, (int begin, int end) =>
			{
				double fBegin = (double)begin / 10000.0;
				double fEnd = (double)(end + 1) / 10000.0;
				UpdateFromThreadCore<T>(src, fBegin, fEnd, copyCellWaterSurfaceGradients, copyNodeFaceWaterSurfaceValues);
			});
			CellWaterSurfaceGradientsUpdated = copyCellWaterSurfaceGradients;
			NodeFaceWaterSurfaceValuesUpdated = copyNodeFaceWaterSurfaceValues;
			if (copyCellWaterSurfaceGradients)
			{
				CellWaterSurfaceGradientLimiter = src.CellWaterSurfaceGradientLimiter;
			}
			CopyWaterSurfaceBoundaryState(src);
		}
	}

	public void UpdateFromThread(MeshWaterState src, double fBegin, double fEnd)
	{
		if (src != this)
		{
			InvalidateCellWaterSurfaceGradients();
			UpdateFromThreadCore(src, fBegin, fEnd, copyCellWaterSurfaceGradients: false, copyNodeFaceWaterSurfaceValues: false);
		}
	}

	private void UpdateFromThreadCore(MeshWaterState src, double fBegin, double fEnd, bool copyCellWaterSurfaceGradients, bool copyNodeFaceWaterSurfaceValues)
	{
		if (fBegin == 0.0)
		{
			tMicros = src.tMicros;
			dtMicros = src.dtMicros;
			ErrorCellVolumeNegative = src.ErrorCellVolumeNegative;
		}
		int lengthTimesFBegin = (int)((double)src.FaceElevation.Length * fBegin);
		int length = (int)((double)src.FaceElevation.Length * fEnd) - lengthTimesFBegin;
		int lengthTimesFBegin2 = (int)((double)src.CellElevation.Length * fBegin);
		int length2 = (int)((double)src.CellElevation.Length * fEnd) - lengthTimesFBegin2;
		Array.Copy(src.CellVolume, lengthTimesFBegin2, CellVolume, lengthTimesFBegin2, length2);
		if (CellArea != null)
		{
			Array.Copy(src.CellArea, lengthTimesFBegin2, CellArea, lengthTimesFBegin2, length2);
		}
		Array.Copy(src.CellElevation, lengthTimesFBegin2, CellElevation, lengthTimesFBegin2, length2);
		if (copyCellWaterSurfaceGradients)
		{
			Array.Copy(src._cellWaterSurfaceGradients, lengthTimesFBegin2, _cellWaterSurfaceGradients, lengthTimesFBegin2, length2);
		}
		if (copyNodeFaceWaterSurfaceValues)
		{
			int lengthTimesFBegin3 = (int)((double)src._nodeFaceWaterSurfaceValues.Length * fBegin);
			int length3 = (int)((double)src._nodeFaceWaterSurfaceValues.Length * fEnd) - lengthTimesFBegin3;
			Array.Copy(src._nodeFaceWaterSurfaceValues, lengthTimesFBegin3, _nodeFaceWaterSurfaceValues, lengthTimesFBegin3, length3);
		}
		if (CellHydraulicDepth != null)
		{
			Array.Copy(src.CellHydraulicDepth, lengthTimesFBegin2, CellHydraulicDepth, lengthTimesFBegin2, length2);
		}
		Array.Copy(src.CellPriorIndexes, lengthTimesFBegin2, CellPriorIndexes, lengthTimesFBegin2, length2);
		if (FaceFlow != null)
		{
			Array.Copy(src.FaceFlow, lengthTimesFBegin, FaceFlow, lengthTimesFBegin, length);
		}
		Array.Copy(src.FaceElevation, lengthTimesFBegin, FaceElevation, lengthTimesFBegin, length);
		Array.Copy(src.FaceArea, lengthTimesFBegin, FaceArea, lengthTimesFBegin, length);
		Array.Copy(src.FaceVelocity, lengthTimesFBegin, FaceVelocity, lengthTimesFBegin, length);
		if (FaceVelTangA != null)
		{
			Array.Copy(src.FaceVelTangA, lengthTimesFBegin, FaceVelTangA, lengthTimesFBegin, length);
			Array.Copy(src.FaceVelTangB, lengthTimesFBegin, FaceVelTangB, lengthTimesFBegin, length);
		}
		if (FaceVelMag != null)
		{
			Array.Copy(src.FaceVelMag, lengthTimesFBegin, FaceVelMag, lengthTimesFBegin, length);
		}
		if (CellVelocity != null)
		{
			Array.Copy(src.CellVelocity, lengthTimesFBegin2, CellVelocity, lengthTimesFBegin2, length2);
		}
		if (FaceHydraulicRadius != null)
		{
			Array.Copy(src.FaceHydraulicRadius, lengthTimesFBegin, FaceHydraulicRadius, lengthTimesFBegin, length);
		}
		if (FaceHydraulicDepth != null)
		{
			Array.Copy(src.FaceHydraulicDepth, lengthTimesFBegin, FaceHydraulicDepth, lengthTimesFBegin, length);
		}
		if (src.FaceMannings != null)
		{
			Array.Copy(src.FaceMannings, lengthTimesFBegin, FaceMannings, lengthTimesFBegin, length);
		}
		Array.Copy(src.FacePriorIndexes, lengthTimesFBegin, FacePriorIndexes, lengthTimesFBegin, length);
		Array.Copy(src.FaceNonlinFrictCoeff, lengthTimesFBegin, FaceNonlinFrictCoeff, lengthTimesFBegin, length);
		if (FaceShearFluxCoeff != null)
		{
			Array.Copy(src.FaceShearFluxCoeff, lengthTimesFBegin, FaceShearFluxCoeff, lengthTimesFBegin, length);
		}
		if (FaceConveyance != null)
		{
			Array.Copy(src.FaceConveyance, lengthTimesFBegin, FaceConveyance, lengthTimesFBegin, length);
		}
		int lengthTimesFBegin4 = (int)((double)src.SourceFlows.Length * fBegin);
		int length4 = (int)((double)src.SourceFlows.Length * fEnd) - lengthTimesFBegin4;
		if (SourceFlows != null)
		{
			Array.Copy(src.SourceFlows, lengthTimesFBegin4, SourceFlows, lengthTimesFBegin4, length4);
		}
	}

	public void UpdateFromThread<T>(MeshWaterState src, double fBegin, double fEnd) where T : IWaterStateConfig
	{
		if (src != this)
		{
			InvalidateCellWaterSurfaceGradients();
			UpdateFromThreadCore<T>(src, fBegin, fEnd, copyCellWaterSurfaceGradients: false, copyNodeFaceWaterSurfaceValues: false);
		}
	}

	private void UpdateFromThreadCore<T>(MeshWaterState src, double fBegin, double fEnd, bool copyCellWaterSurfaceGradients, bool copyNodeFaceWaterSurfaceValues) where T : IWaterStateConfig
	{
		if (fBegin == 0.0)
		{
			tMicros = src.tMicros;
			dtMicros = src.dtMicros;
			ErrorCellVolumeNegative = src.ErrorCellVolumeNegative;
		}
		int lengthTimesFBegin = (int)((double)src.FaceElevation.Length * fBegin);
		int length = (int)((double)src.FaceElevation.Length * fEnd) - lengthTimesFBegin;
		int lengthTimesFBegin2 = (int)((double)src.CellElevation.Length * fBegin);
		int length2 = (int)((double)src.CellElevation.Length * fEnd) - lengthTimesFBegin2;
		Array.Copy(src.CellVolume, lengthTimesFBegin2, CellVolume, lengthTimesFBegin2, length2);
		if (T.StateHasCellArea())
		{
			Array.Copy(src.CellArea, lengthTimesFBegin2, CellArea, lengthTimesFBegin2, length2);
		}
		Array.Copy(src.CellElevation, lengthTimesFBegin2, CellElevation, lengthTimesFBegin2, length2);
		if (copyCellWaterSurfaceGradients)
		{
			Array.Copy(src._cellWaterSurfaceGradients, lengthTimesFBegin2, _cellWaterSurfaceGradients, lengthTimesFBegin2, length2);
		}
		if (copyNodeFaceWaterSurfaceValues)
		{
			int lengthTimesFBegin3 = (int)((double)src._nodeFaceWaterSurfaceValues.Length * fBegin);
			int length3 = (int)((double)src._nodeFaceWaterSurfaceValues.Length * fEnd) - lengthTimesFBegin3;
			Array.Copy(src._nodeFaceWaterSurfaceValues, lengthTimesFBegin3, _nodeFaceWaterSurfaceValues, lengthTimesFBegin3, length3);
		}
		if (T.StateHasCellHydraulicDepth())
		{
			Array.Copy(src.CellHydraulicDepth, lengthTimesFBegin2, CellHydraulicDepth, lengthTimesFBegin2, length2);
		}
		Array.Copy(src.CellPriorIndexes, lengthTimesFBegin2, CellPriorIndexes, lengthTimesFBegin2, length2);
		if (T.StateHasFaceFlow())
		{
			Array.Copy(src.FaceFlow, lengthTimesFBegin, FaceFlow, lengthTimesFBegin, length);
		}
		Array.Copy(src.FaceElevation, lengthTimesFBegin, FaceElevation, lengthTimesFBegin, length);
		Array.Copy(src.FaceArea, lengthTimesFBegin, FaceArea, lengthTimesFBegin, length);
		Array.Copy(src.FaceVelocity, lengthTimesFBegin, FaceVelocity, lengthTimesFBegin, length);
		if (T.StateHasFaceVelTang())
		{
			Array.Copy(src.FaceVelTangA, lengthTimesFBegin, FaceVelTangA, lengthTimesFBegin, length);
			Array.Copy(src.FaceVelTangB, lengthTimesFBegin, FaceVelTangB, lengthTimesFBegin, length);
		}
		if (T.StateHasFaceVelMag())
		{
			Array.Copy(src.FaceVelMag, lengthTimesFBegin, FaceVelMag, lengthTimesFBegin, length);
		}
		Array.Copy(src.CellVelocity, lengthTimesFBegin2, CellVelocity, lengthTimesFBegin2, length2);
		if (T.StateHasFaceHydraulicRadius())
		{
			Array.Copy(src.FaceHydraulicRadius, lengthTimesFBegin, FaceHydraulicRadius, lengthTimesFBegin, length);
		}
		if (T.StateHasFaceHydraulicDepth())
		{
			Array.Copy(src.FaceHydraulicDepth, lengthTimesFBegin, FaceHydraulicDepth, lengthTimesFBegin, length);
		}
		if (T.StateHasFaceMannings())
		{
			Array.Copy(src.FaceMannings, lengthTimesFBegin, FaceMannings, lengthTimesFBegin, length);
		}
		Array.Copy(src.FacePriorIndexes, lengthTimesFBegin, FacePriorIndexes, lengthTimesFBegin, length);
		Array.Copy(src.FaceNonlinFrictCoeff, lengthTimesFBegin, FaceNonlinFrictCoeff, lengthTimesFBegin, length);
		if (T.StateHasFaceShearFluxCoeff())
		{
			Array.Copy(src.FaceShearFluxCoeff, lengthTimesFBegin, FaceShearFluxCoeff, lengthTimesFBegin, length);
		}
		if (T.StateHasFaceConveyance())
		{
			Array.Copy(src.FaceConveyance, lengthTimesFBegin, FaceConveyance, lengthTimesFBegin, length);
		}
		int lengthTimesFBegin4 = (int)((double)src.SourceFlows.Length * fBegin);
		int length4 = (int)((double)src.SourceFlows.Length * fEnd) - lengthTimesFBegin4;
		Array.Copy(src.SourceFlows, lengthTimesFBegin4, SourceFlows, lengthTimesFBegin4, length4);
	}

	private void PrepareWaterSurfaceCacheCopy(MeshWaterState source, bool copyCellWaterSurfaceGradients, bool copyNodeFaceWaterSurfaceValues)
	{
		InvalidateCellWaterSurfaceGradients();
		if (copyCellWaterSurfaceGradients && (_cellWaterSurfaceGradients == null || _cellWaterSurfaceGradients.Length != CellCount))
		{
			_cellWaterSurfaceGradients = new Geospatial.Vectors.Vector[CellCount];
		}
		if (copyNodeFaceWaterSurfaceValues)
		{
			int nodeFaceValueCount = _hmesh.NodeComputes.NodeFaceValueCount;
			if (_nodeFaceWaterSurfaceValues == null || _nodeFaceWaterSurfaceValues.Length != nodeFaceValueCount)
			{
				_nodeFaceWaterSurfaceValues = new double[nodeFaceValueCount];
			}
			if (_nodeFaceWaterSurfaceInterpolation == null)
			{
				_nodeFaceWaterSurfaceInterpolation = _hmesh.NodeComputes.CreateNodeFaceInterpolationWorkspace();
			}
			_nodeFaceWaterSurfaceInterpolation.CopyFrom(source._nodeFaceWaterSurfaceInterpolation);
		}
	}

	public void InvalidateCellWaterSurfaceGradients()
	{
		CellWaterSurfaceGradientsUpdated = false;
		NodeFaceWaterSurfaceValuesUpdated = false;
		CellWaterSurfaceGradientLimiter = HydraulicMeshComputer.MultidimensionalSlopeLimiter.None;
	}

	public void UpdateCellWaterSurfaceGradients(ThreadFor threadFor, HydraulicMeshComputer.MultidimensionalSlopeLimiter limiterType = HydraulicMeshComputer.MultidimensionalSlopeLimiter.BarthJespersen)
	{
		if (CellWaterSurfaceGradientsUpdated && CellWaterSurfaceGradientLimiter == limiterType)
		{
			return;
		}
		if (_cellWaterSurfaceGradients == null || _cellWaterSurfaceGradients.Length != CellCount)
		{
			_cellWaterSurfaceGradients = new Geospatial.Vectors.Vector[CellCount];
		}
		if (_cellWaterSurfaceGradientWorkspace == null)
		{
			_cellWaterSurfaceGradientWorkspace = _hmesh.CreateScalarGradientWlsWorkspace();
		}
		CellWaterSurfaceGradientsUpdated = false;
		CellWaterSurfaceGradientLimiter = HydraulicMeshComputer.MultidimensionalSlopeLimiter.None;
		HydraulicMeshComputer.ScalarGradientWlsWorkspace workspace = _cellWaterSurfaceGradientWorkspace;
		if (threadFor == null)
		{
			for (int i = 0; i < CellCount; i++)
			{
				workspace.PrepareCell(i, CellVolume);
				_cellWaterSurfaceGradients[i] = workspace.ScalarGradientLimited(i, CellElevation, limiterType, out var _);
			}
		}
		else
		{
			threadFor.Run(CellCount, (int start, int stop) =>
			{
				ReadOnlySpan<double> cellValues = CellElevation.AsSpan();
				ReadOnlySpan<double> cellVolumes = CellVolume.AsSpan();
				Span<Geospatial.Vectors.Vector> cellWaterSurfaceGradients = _cellWaterSurfaceGradients.AsSpan();
				for (int j = start; j <= stop; j++)
				{
					workspace.PrepareCell(j, cellVolumes);
					cellWaterSurfaceGradients[j] = workspace.ScalarGradientLimited(j, cellValues, limiterType, out var _);
				}
			});
		}
		_waterSurfaceBoundaryReconstruction?.ApplyCellGradientOverrides(workspace, CellElevation, CellVolume, _cellWaterSurfaceGradients, limiterType);
		CellWaterSurfaceGradientLimiter = limiterType;
		CellWaterSurfaceGradientsUpdated = true;
	}

	public void UpdateNodeFaceWaterSurfaceValues(ThreadFor threadFor)
	{
		if (NodeFaceWaterSurfaceValuesUpdated && _nodeFaceWaterSurfaceInterpolation.WetStatusMatches(FaceArea))
		{
			return;
		}
		if (_nodeFaceWaterSurfaceInterpolation == null)
		{
			_nodeFaceWaterSurfaceInterpolation = _hmesh.NodeComputes.CreateNodeFaceInterpolationWorkspace();
		}
		_nodeFaceWaterSurfaceInterpolation.Prepare(FaceArea);
		int nodeFaceValueCount = _hmesh.NodeComputes.NodeFaceValueCount;
		if (_nodeFaceWaterSurfaceValues == null || _nodeFaceWaterSurfaceValues.Length != nodeFaceValueCount)
		{
			_nodeFaceWaterSurfaceValues = new double[nodeFaceValueCount];
		}
		NodeFaceWaterSurfaceValuesUpdated = false;
		if (threadFor == null)
		{
			_hmesh.NodeComputes.InterpolateNodeFaceValues(_nodeFaceWaterSurfaceInterpolation, CellElevation, _nodeFaceWaterSurfaceValues);
		}
		else
		{
			threadFor.Run(_mesh.NodeCount, (int start, int stop) =>
			{
				for (int i = start; i <= stop; i++)
				{
					_hmesh.NodeComputes.InterpolateNodeFaceValues(i, _nodeFaceWaterSurfaceInterpolation, CellElevation, _nodeFaceWaterSurfaceValues);
				}
			});
		}
		_waterSurfaceBoundaryReconstruction?.ApplyNodeFaceValueOverrides(CellElevation, CellVolume, _nodeFaceWaterSurfaceInterpolation, _nodeFaceWaterSurfaceValues);
		NodeFaceWaterSurfaceValuesUpdated = true;
	}

	public void SetPropertyTables(MeshPropertyTables props)
	{
		_props = props;
	}

	public MeshWaterState Clone()
	{
		MeshWaterState meshWaterState = new MeshWaterState();
		meshWaterState._mesh = _mesh;
		meshWaterState._hmesh = _hmesh;
		meshWaterState._props = _props;
		meshWaterState.tMicros = tMicros;
		meshWaterState.dtMicros = dtMicros;
		meshWaterState.CellVolume = CellVolume.ToArray();
		meshWaterState.CellArea = CellArea?.ToArray();
		meshWaterState.CellElevation = CellElevation.ToArray();
		if (CellWaterSurfaceGradientsUpdated)
		{
			meshWaterState._cellWaterSurfaceGradients = _cellWaterSurfaceGradients.ToArray();
			meshWaterState.CellWaterSurfaceGradientLimiter = CellWaterSurfaceGradientLimiter;
			meshWaterState.CellWaterSurfaceGradientsUpdated = true;
		}
		if (NodeFaceWaterSurfaceValuesUpdated)
		{
			meshWaterState._nodeFaceWaterSurfaceValues = _nodeFaceWaterSurfaceValues.ToArray();
			meshWaterState._nodeFaceWaterSurfaceInterpolation = _nodeFaceWaterSurfaceInterpolation.Clone();
			meshWaterState.NodeFaceWaterSurfaceValuesUpdated = true;
		}
		meshWaterState.PinnedFaceStage = PinnedFaceStage?.ToArray();
		if (_waterSurfaceBoundaryReconstruction != null)
		{
			meshWaterState._waterSurfaceBoundaryReconstruction = _hmesh.CreateScalarBoundaryReconstructionWorkspace();
			meshWaterState._waterSurfaceBoundaryReconstruction.CopyFrom(_waterSurfaceBoundaryReconstruction);
		}
		meshWaterState.CellHydraulicDepth = CellHydraulicDepth?.ToArray();
		meshWaterState.CellPriorIndexes = new int[CellCount];
		meshWaterState.FaceFlow = FaceFlow?.ToArray();
		meshWaterState.FaceElevation = FaceElevation.ToArray();
		meshWaterState.FaceArea = FaceArea.ToArray();
		meshWaterState.FaceHydraulicRadius = FaceHydraulicRadius?.ToArray();
		meshWaterState.FaceHydraulicDepth = FaceHydraulicDepth?.ToArray();
		meshWaterState.FaceMannings = FaceMannings?.ToArray();
		meshWaterState.FacePriorIndexes = new int[FaceCount];
		meshWaterState.FaceVelocity = FaceVelocity.ToArray();
		meshWaterState.SourceFlows = SourceFlows?.ToArray();
		meshWaterState.FaceNonlinFrictCoeff = FaceNonlinFrictCoeff.ToArray();
		meshWaterState.FaceShearFluxCoeff = FaceShearFluxCoeff?.ToArray();
		meshWaterState.FaceConveyance = FaceConveyance?.ToArray();
		meshWaterState.turb = turb?.Clone();
		meshWaterState.secondaryFlow = secondaryFlow?.Clone();
		meshWaterState.CellVelocity = CellVelocity?.ToArray();
		meshWaterState.FaceVelMag = FaceVelMag?.ToArray();
		meshWaterState.FaceVelTangA = FaceVelTangA?.ToArray();
		meshWaterState.FaceVelTangB = FaceVelTangB?.ToArray();
		return meshWaterState;
	}

	public MeshWaterState CloneFull()
	{
		MeshWaterState meshWaterState = Clone();
		meshWaterState.CellLevel2Lts = CellLevel2Lts?.ToArray();
		meshWaterState.CellDtMaxAdvection = CellDtMaxAdvection?.ToArray();
		meshWaterState.CellDtMaxCelerity = CellDtMaxCelerity?.ToArray();
		meshWaterState.CellDtMaxDiffusion = CellDtMaxDiffusion?.ToArray();
		return meshWaterState;
	}

	protected MeshWaterState()
	{
	}

	public MeshWaterState(Mesh mesh, MeshPropertyTables tables)
	{
		_mesh = mesh;
		_hmesh = mesh.MetadataCache;
		_props = tables;
		int cellCount = CellCount;
		int faceCount = FaceCount;
		CellVolume = new double[cellCount];
		CellElevation = new double[cellCount];
		FaceVelocity = new double[faceCount];
		FaceArea = new double[faceCount];
		CellArea = new double[cellCount];
		CellHydraulicDepth = new float[cellCount];
		CellVelocity = new Geospatial.Vectors.Vector[cellCount];
		CellPriorIndexes = new int[cellCount];
		FaceVelMag = new float[faceCount];
		FaceVelTangA = new double[faceCount];
		FaceVelTangB = new double[faceCount];
		FaceFlow = new double[faceCount];
		FaceElevation = new double[faceCount];
		FaceHydraulicRadius = new float[faceCount];
		FaceHydraulicDepth = new float[faceCount];
		FaceMannings = new float[faceCount];
		FaceNonlinFrictCoeff = new float[faceCount];
		FaceShearFluxCoeff = new float[faceCount];
		FacePriorIndexes = new int[faceCount];
		FaceConveyance = new double[faceCount];
	}

	public void InitializeSources(int sourceCount)
	{
		SourceCount = sourceCount;
		SourceFlows = new double[sourceCount];
	}

	public int GetWetCellCount()
	{
		int ordinal = 0;
		if (CellVolume != null && CellVolume.Length != 0)
		{
			for (int i = 0; i < CellVolume.Length; i++)
			{
				if (CellVolume[i] > 0.001)
				{
					ordinal++;
				}
			}
			return ordinal;
		}
		if (CellElevation != null && CellElevation.Length != 0 && _props != null)
		{
			for (int j = 0; j < CellElevation.Length; j++)
			{
				double cellMinElevation = _props.CellMinElevation(j);
				if (CellElevation[j] > cellMinElevation + 0.001)
				{
					ordinal++;
				}
			}
			return ordinal;
		}
		return 0;
	}

	public static MeshWaterState Factory<T>(Mesh mesh, MeshPropertyTables tables) where T : IWaterStateConfig
	{
		MeshWaterState meshWaterState = new MeshWaterState();
		meshWaterState._mesh = mesh;
		meshWaterState._hmesh = mesh.MetadataCache;
		meshWaterState._props = tables;
		meshWaterState.EnsureFields<T>();
		return meshWaterState;
	}

	public void EnsureFields<T>() where T : IWaterStateConfig
	{
		int cellCount = CellCount;
		int faceCount = FaceCount;
		int sourceCount = SourceCount;
		MeshWaterState meshWaterState = this;
		if (meshWaterState.CellVolume == null)
		{
			meshWaterState.CellVolume = new double[cellCount];
		}
		meshWaterState = this;
		if (meshWaterState.CellElevation == null)
		{
			meshWaterState.CellElevation = new double[cellCount];
		}
		meshWaterState = this;
		if (meshWaterState.FaceVelocity == null)
		{
			meshWaterState.FaceVelocity = new double[faceCount];
		}
		meshWaterState = this;
		if (meshWaterState.FaceArea == null)
		{
			meshWaterState.FaceArea = new double[faceCount];
		}
		meshWaterState = this;
		if (meshWaterState.SourceFlows == null)
		{
			meshWaterState.SourceFlows = new double[sourceCount];
		}
		if (T.StateHasCellArea())
		{
			meshWaterState = this;
			if (meshWaterState.CellArea == null)
			{
				meshWaterState.CellArea = new double[cellCount];
			}
		}
		else
		{
			CellArea = null;
		}
		if (T.StateHasCellHydraulicDepth())
		{
			meshWaterState = this;
			if (meshWaterState.CellHydraulicDepth == null)
			{
				meshWaterState.CellHydraulicDepth = new float[cellCount];
			}
		}
		else
		{
			CellHydraulicDepth = null;
		}
		meshWaterState = this;
		if (meshWaterState.CellVelocity == null)
		{
			meshWaterState.CellVelocity = new Geospatial.Vectors.Vector[cellCount];
		}
		meshWaterState = this;
		if (meshWaterState.CellPriorIndexes == null)
		{
			meshWaterState.CellPriorIndexes = new int[cellCount];
		}
		if (T.StateHasFaceVelMag())
		{
			meshWaterState = this;
			if (meshWaterState.FaceVelMag == null)
			{
				meshWaterState.FaceVelMag = new float[faceCount];
			}
		}
		else
		{
			FaceVelMag = null;
		}
		if (T.StateHasFaceVelTang())
		{
			meshWaterState = this;
			if (meshWaterState.FaceVelTangA == null)
			{
				meshWaterState.FaceVelTangA = new double[faceCount];
			}
			meshWaterState = this;
			if (meshWaterState.FaceVelTangB == null)
			{
				meshWaterState.FaceVelTangB = new double[faceCount];
			}
		}
		else
		{
			FaceVelTangA = null;
			FaceVelTangB = null;
		}
		if (T.StateHasFaceFlow())
		{
			meshWaterState = this;
			if (meshWaterState.FaceFlow == null)
			{
				meshWaterState.FaceFlow = new double[faceCount];
			}
		}
		else
		{
			FaceFlow = null;
		}
		meshWaterState = this;
		if (meshWaterState.FaceElevation == null)
		{
			meshWaterState.FaceElevation = new double[faceCount];
		}
		if (T.StateHasFaceHydraulicRadius())
		{
			meshWaterState = this;
			if (meshWaterState.FaceHydraulicRadius == null)
			{
				meshWaterState.FaceHydraulicRadius = new float[faceCount];
			}
		}
		else
		{
			FaceHydraulicRadius = null;
		}
		if (T.StateHasCellHydraulicDepth())
		{
			meshWaterState = this;
			if (meshWaterState.FaceHydraulicDepth == null)
			{
				meshWaterState.FaceHydraulicDepth = new float[faceCount];
			}
		}
		else
		{
			FaceHydraulicDepth = null;
		}
		if (T.StateHasFaceMannings())
		{
			meshWaterState = this;
			if (meshWaterState.FaceMannings == null)
			{
				meshWaterState.FaceMannings = new float[faceCount];
			}
		}
		else
		{
			FaceMannings = null;
		}
		meshWaterState = this;
		if (meshWaterState.FaceNonlinFrictCoeff == null)
		{
			meshWaterState.FaceNonlinFrictCoeff = new float[faceCount];
		}
		if (T.StateHasFaceShearFluxCoeff())
		{
			meshWaterState = this;
			if (meshWaterState.FaceShearFluxCoeff == null)
			{
				meshWaterState.FaceShearFluxCoeff = new float[faceCount];
			}
		}
		else
		{
			FaceShearFluxCoeff = null;
		}
		meshWaterState = this;
		if (meshWaterState.FacePriorIndexes == null)
		{
			meshWaterState.FacePriorIndexes = new int[faceCount];
		}
		if (T.StateHasFaceConveyance())
		{
			meshWaterState = this;
			if (meshWaterState.FaceConveyance == null)
			{
				meshWaterState.FaceConveyance = new double[faceCount];
			}
		}
		else
		{
			FaceConveyance = null;
		}
	}

	public static MeshWaterState CreateEmpty(Mesh m, MeshPropertyTables tables)
	{
		return new MeshWaterState
		{
			_mesh = m,
			_props = tables,
			_hmesh = m.MetadataCache
		};
	}

	public void StreamPrinciple(BinaryWriter bw)
	{
		Pipe.WriteArray(bw, CellElevation);
		Pipe.WriteArray(bw, CellVolume);
		Pipe.WriteArray(bw, FaceVelocity);
		Pipe.WriteArray(bw, FaceElevation);
		Pipe.WriteArray(bw, SourceFlows);
	}

	public void ComputeFaceVelocity()
	{
		if (FaceVelocity == null)
		{
			FaceVelocity = new double[_mesh.FaceCount];
		}
		for (int i = 0; i < FaceVelocity.Length; i++)
		{
			if (FaceArea[i] == 0.0)
			{
				FaceVelocity[i] = 0.0;
			}
			else
			{
				FaceVelocity[i] = FaceFlow[i] / FaceArea[i];
			}
		}
	}

	public void ClearCellPriorIndexes(List<int> cellIndexes)
	{
		foreach (int cellIndex in cellIndexes)
		{
			CellPriorIndexes[cellIndex] = 0;
		}
	}

	public void InitializePinnedFaceStages()
	{
		if (PinnedFaceStage == null || PinnedFaceStage.Length != FaceElevation.Length)
		{
			PinnedFaceStage = new double[FaceElevation.Length];
		}
		PinnedFaceStage.AsSpan().Fill(-9999.0);
	}

	public void SetPinnedFaceStages(Dictionary<int, double> faceStages)
	{
		InitializePinnedFaceStages();
		foreach (KeyValuePair<int, double> faceStage in faceStages)
		{
			PinnedFaceStage[faceStage.Key] = faceStage.Value;
		}
	}

	public void SetPinnedFaceStages(int[] faceIndexes, double[] faceStages)
	{
		InitializePinnedFaceStages();
		for (int i = 0; i < faceIndexes.Length; i++)
		{
			PinnedFaceStage[faceIndexes[i]] = faceStages[i];
		}
	}

	public void ClearPinnedFaceStages()
	{
		PinnedFaceStage = null;
	}

	public HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace ResetWaterSurfaceBoundaryReconstruction()
	{
		HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace scalarBoundaryReconstructionWorkspace = EnsureWaterSurfaceBoundaryReconstruction();
		scalarBoundaryReconstructionWorkspace.Clear();
		InvalidateCellWaterSurfaceGradients();
		return scalarBoundaryReconstructionWorkspace;
	}

	private HydraulicMeshComputer.ScalarBoundaryReconstructionWorkspace EnsureWaterSurfaceBoundaryReconstruction()
	{
		if (_waterSurfaceBoundaryReconstruction == null)
		{
			_waterSurfaceBoundaryReconstruction = _hmesh.CreateScalarBoundaryReconstructionWorkspace();
		}
		return _waterSurfaceBoundaryReconstruction;
	}

	private void CopyWaterSurfaceBoundaryState(MeshWaterState source)
	{
		if (source.PinnedFaceStage == null)
		{
			PinnedFaceStage = null;
		}
		else
		{
			if (PinnedFaceStage == null || PinnedFaceStage.Length != source.PinnedFaceStage.Length)
			{
				PinnedFaceStage = new double[source.PinnedFaceStage.Length];
			}
			Array.Copy(source.PinnedFaceStage, PinnedFaceStage, PinnedFaceStage.Length);
		}
		if (source._waterSurfaceBoundaryReconstruction == null)
		{
			_waterSurfaceBoundaryReconstruction?.Clear();
		}
		else
		{
			EnsureWaterSurfaceBoundaryReconstruction().CopyFrom(source._waterSurfaceBoundaryReconstruction);
		}
	}

	public void UpdateCells(double[] newCellVolumes, List<int> cellIndexes, ThreadFor threadFor)
	{
		InvalidateCellWaterSurfaceGradients();
		if (newCellVolumes == null)
		{
			if (cellIndexes != null)
			{
				ClearCellPriorIndexes(cellIndexes);
			}
			newCellVolumes = CellVolume;
		}
		if (cellIndexes == null)
		{
			cellIndexes = new List<int>();
			for (int i = 0; i < _mesh.CellCount; i++)
			{
				cellIndexes.Add(i);
			}
		}
		_props.UpdateCells(CellElevation, CellArea, CellVolume, CellHydraulicDepth, CellPriorIndexes, newCellVolumes, cellIndexes, threadFor);
	}

	public void UpdateFaces(FaceElevationMethod method, List<int> faceIndexes, ThreadFor threadFor)
	{
		threadFor.Run(faceIndexes.Count, (int start, int stop) =>
		{
			UpdateFacesMANY(method, faceIndexes, start, stop);
		});
	}

	public void UpdateFacesMANY(FaceElevationMethod method, List<int> faceIndexes, int start, int stop)
	{
		int[] facePriorIndexes = FacePriorIndexes;
		double[] faceElevation = FaceElevation;
		double[] faceArea = FaceArea;
		float[] faceHydraulicRadius = FaceHydraulicRadius;
		float[] faceHydraulicDepth = FaceHydraulicDepth;
		float[] faceMannings = FaceMannings;
		float[] faceNonlinFrictCoeff = FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = FaceShearFluxCoeff;
		double[] pinnedFaceStage = PinnedFaceStage;
		double[] cellElevation = CellElevation;
		double[] faceVelocity = FaceVelocity;
		double[] faceConveyance = FaceConveyance;
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndexes[i];
			double wsel = _hmesh.FaceComputes.WSEL_Engine(fIdx, cellElevation, pinnedFaceStage, faceVelocity, method, _props);
			_props.UpdateFaceONE(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, in wsel);
		}
	}

	public void UpdateFacesMANY<T>(FaceElevationMethod method, List<int> faceIndexes, int start, int stop) where T : IWaterStateConfig
	{
		int[] facePriorIndexes = FacePriorIndexes;
		double[] faceElevation = FaceElevation;
		double[] faceArea = FaceArea;
		float[] faceHydraulicRadius = FaceHydraulicRadius;
		float[] faceHydraulicDepth = FaceHydraulicDepth;
		float[] faceMannings = FaceMannings;
		float[] faceNonlinFrictCoeff = FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = FaceShearFluxCoeff;
		double[] pinnedFaceStage = PinnedFaceStage;
		double[] cellElevation = CellElevation;
		double[] faceVelocity = FaceVelocity;
		double[] faceConveyance = FaceConveyance;
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndexes[i];
			double wsel = _hmesh.FaceComputes.WSEL_Engine(fIdx, cellElevation, pinnedFaceStage, faceVelocity, method, _props);
			_props.UpdateFaceONE<T>(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, in wsel);
		}
	}

	public void UpdateFacesProperties(int[] faceIndexes, double newFaceWSE)
	{
		int[] facePriorIndexes = FacePriorIndexes;
		double[] faceElevation = FaceElevation;
		double[] faceArea = FaceArea;
		float[] faceHydraulicRadius = FaceHydraulicRadius;
		float[] faceHydraulicDepth = FaceHydraulicDepth;
		float[] faceMannings = FaceMannings;
		float[] faceNonlinFrictCoeff = FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = FaceShearFluxCoeff;
		double[] faceConveyance = FaceConveyance;
		for (int i = 0; i < faceIndexes.Length; i++)
		{
			int fIdx = faceIndexes[i];
			_props.UpdateFaceONE(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, in newFaceWSE);
		}
	}

	public void UpdateFacesProperties(List<int> faceIndexes)
	{
		int[] facePriorIndexes = FacePriorIndexes;
		double[] faceElevation = FaceElevation;
		double[] faceArea = FaceArea;
		float[] faceHydraulicRadius = FaceHydraulicRadius;
		float[] faceHydraulicDepth = FaceHydraulicDepth;
		float[] faceMannings = FaceMannings;
		float[] faceNonlinFrictCoeff = FaceNonlinFrictCoeff;
		float[] faceShearFluxCoeff = FaceShearFluxCoeff;
		double[] faceConveyance = FaceConveyance;
		for (int i = 0; i < faceIndexes.Count; i++)
		{
			int fIdx = faceIndexes[i];
			_props.UpdateFaceONE(in fIdx, facePriorIndexes, faceElevation, faceArea, faceHydraulicRadius, faceHydraulicDepth, faceMannings, faceNonlinFrictCoeff, faceShearFluxCoeff, faceConveyance, in faceElevation[fIdx]);
		}
	}

	public void UpdateFaceFlowFromVelocityArea()
	{
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			FaceFlow[i] = FaceArea[i] * FaceVelocity[i];
		}
	}

	public double CellInflow(int cIdx)
	{
		Cell cell = _mesh.Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		double faceFlowSum = 0.0;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = _mesh.Faces[faceIndex];
			double sign = 1.0;
			if (cIdx == face.cellA)
			{
				sign = -1.0;
			}
			faceFlowSum += sign * FaceFlow[faceIndex];
		}
		return faceFlowSum;
	}

	public string DebugCellInfo(int cIdx, string delimiter = null)
	{
		string format = "E3";
		if (string.IsNullOrEmpty(delimiter))
		{
			delimiter = Environment.NewLine;
		}
		string cellInfo = "Volume " + CellVolume[cIdx].ToString(format) + delimiter + "Elevation " + CellElevation[cIdx].ToString(format) + delimiter + "Area " + CellArea[cIdx].ToString(format);
		if (CellVelocity != null)
		{
			cellInfo = cellInfo + delimiter + "Cell Vel " + CellVelocity[cIdx].X.ToString(format) + "," + CellVelocity[cIdx].Y.ToString(format);
		}
		return cellInfo;
	}

	public virtual string DebugFaceInfo(int fIdx, string delimiter = null)
	{
		string format = "E3";
		if (string.IsNullOrEmpty(delimiter))
		{
			delimiter = Environment.NewLine;
		}
		string faceInfo = "Flow " + FaceFlow[fIdx].ToString(format) + delimiter + "Stage " + FaceElevation[fIdx].ToString(format) + delimiter + "Area " + FaceArea[fIdx].ToString(format) + delimiter + "HRad " + FaceHydraulicRadius[fIdx].ToString(format) + delimiter + "Mann " + FaceMannings[fIdx].ToString(format) + delimiter + "Velo " + FaceVelocity[fIdx].ToString(format);
		if (turb != null)
		{
			faceInfo = faceInfo + delimiter + "FaceEddyVis " + turb.faceEddyVis[fIdx].ToString(format);
		}
		return faceInfo;
	}

	public void SetConstantElevation(double elev)
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			double cellVolume = _props.InterpolateCellVolume(i, elev);
			CellVolume[i] = cellVolume;
			CellElevation[i] = elev;
		}
		for (int j = 0; j < FaceCount; j++)
		{
			double faceArea = _props.InterpolateFaceArea(j, elev);
			FaceArea[j] = faceArea;
			FaceElevation[j] = elev;
		}
	}

	public void SetConstantDepth(double depth)
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			double cellElevation = (double)_props.CellMinElevation(i) + depth;
			double cellVolume = _props.InterpolateCellVolume(i, cellElevation);
			CellVolume[i] = cellVolume;
			CellElevation[i] = cellElevation;
		}
		for (int j = 0; j < FaceCount; j++)
		{
			double faceElevation = (double)_props.FaceMinElevation(j) + depth;
			double faceArea = _props.InterpolateFaceArea(j, faceElevation);
			FaceArea[j] = faceArea;
			FaceElevation[j] = faceElevation;
		}
	}

	public void SetConstantDepth(double cellDepth, double faceDepth)
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			double cellElevation = (double)_props.CellMinElevation(i) + cellDepth;
			double cellVolume = _props.InterpolateCellVolume(i, cellElevation);
			CellVolume[i] = cellVolume;
			CellElevation[i] = cellElevation;
		}
		for (int j = 0; j < FaceCount; j++)
		{
			double faceElevation = (double)_props.FaceMinElevation(j) + faceDepth;
			double faceArea = _props.InterpolateFaceArea(j, faceElevation);
			FaceArea[j] = faceArea;
			FaceElevation[j] = faceElevation;
		}
	}

	public void SetFloodfillStage(List<Tuple<double, List<int>>> cellStageMap)
	{
		InvalidateCellWaterSurfaceGradients();
		double[] cellStages = new double[CellCount];
		double[] faceStages = new double[FaceCount];
		cellStages.Fill(-9999.0);
		faceStages.Fill(-9999.0);
		foreach (Tuple<double, List<int>> cellStageMap2 in cellStageMap)
		{
			double stage = cellStageMap2.Item1;
			List<int> cellStageMap2Term = cellStageMap2.Item2;
			if (cellStageMap2Term == null || cellStageMap2Term.Count == 0)
			{
				continue;
			}
			for (int i = 0; i < cellStageMap2Term.Count; i++)
			{
				int seedCell = cellStageMap2Term[i];
				_mesh.Floodfill(seedCell, (int ff) =>
				{
					if ((double)_props.FaceMinimumElevations[ff] < stage)
					{
						if (faceStages[ff] == -9999.0 || faceStages[ff] < stage)
						{
							faceStages[ff] = stage;
							return true;
						}
						return false;
					}
					return false;
				}, (int cc) =>
				{
					cellStages[cc] = stage;
				}, includeSeedCell: true);
			}
		}
		for (int intervalIndex = 0; intervalIndex < CellCount; intervalIndex++)
		{
			double cellStage = cellStages[intervalIndex];
			double cellMinimumElevation = _props.CellMinimumElevations[intervalIndex];
			if (cellStage == -9999.0 || cellStage <= cellMinimumElevation)
			{
				CellElevation[intervalIndex] = cellMinimumElevation;
				CellVolume[intervalIndex] = 0.0;
			}
			else
			{
				CellElevation[intervalIndex] = cellStage;
				CellVolume[intervalIndex] = _props.InterpolateCellVolume(intervalIndex, cellStage);
			}
		}
		for (int intervalIndex2 = 0; intervalIndex2 < FaceCount; intervalIndex2++)
		{
			double faceStage = faceStages[intervalIndex2];
			double faceMinimumElevation = _props.FaceMinimumElevations[intervalIndex2];
			if (faceStage == -9999.0 || faceStage <= faceMinimumElevation)
			{
				FaceElevation[intervalIndex2] = faceMinimumElevation;
				FaceArea[intervalIndex2] = 0.0;
			}
			else
			{
				FaceElevation[intervalIndex2] = faceStage;
				FaceArea[intervalIndex2] = _props.InterpolateFaceArea(intervalIndex2, faceStage);
			}
		}
	}

	public void SetDry()
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			CellVolume[i] = 0.0;
			CellElevation[i] = _props.CellMinElevation(i);
		}
		for (int j = 0; j < FaceCount; j++)
		{
			FaceArea[j] = 0.0;
			FaceElevation[j] = _props.FaceMinElevation(j);
		}
	}

	public void SetConstantInitialState(double cellVol, double cellElev, double faceArea, double faceStage)
	{
		InvalidateCellWaterSurfaceGradients();
		int cellVolumeLength = CellVolume.Length;
		int faceFlowLength = FaceFlow.Length;
		for (int i = 0; i < cellVolumeLength; i++)
		{
			CellVolume[i] = cellVol;
			CellElevation[i] = cellElev;
		}
		for (int j = 0; j < faceFlowLength; j++)
		{
			FaceArea[j] = faceArea;
			FaceElevation[j] = faceStage;
		}
	}

	public void SetInitialStageFromCellCenters(IResample<float> stageSurface)
	{
		InvalidateCellWaterSurfaceGradients();
		Point[] cellCenters = _mesh.CellCenters.ToArray();
		float[] samplePoints = stageSurface.SamplePoints(cellCenters);
		for (int i = 0; i < CellCount; i++)
		{
			double samplePoint = samplePoints[i];
			samplePoint = Math.Max(samplePoint, _props.CellMinimumElevations[i]);
			CellElevation[i] = samplePoint;
			CellVolume[i] = _props.InterpolateCellVolume(i, samplePoint);
		}
	}

	public void SetInitialFromCellStage(double[] cellStage)
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			CellElevation[i] = cellStage[i];
			CellVolume[i] = _props.InterpolateCellVolume(i, cellStage[i]);
		}
	}

	public void SetInitialFromRestartVars(double[] cellElevation, double[] faceVelocity, long dtMicros)
	{
		InvalidateCellWaterSurfaceGradients();
		for (int i = 0; i < CellCount; i++)
		{
			CellElevation[i] = cellElevation[i];
			CellVolume[i] = _props.InterpolateCellVolume(i, cellElevation[i]);
		}
		Array.Copy(faceVelocity, FaceVelocity, FaceCount);
		this.dtMicros = dtMicros;
	}

	public void SetInitialFromCellDepth(Memory<float> cellDepth)
	{
		InvalidateCellWaterSurfaceGradients();
		Span<float> ensureCellMinElevationTable = _props.EnsureCellMinElevationTable().Span;
		Span<float> cellDepthSpan = cellDepth.Span;
		for (int i = 0; i < CellCount; i++)
		{
			double cellElevation = cellDepthSpan[i] + ensureCellMinElevationTable[i];
			CellElevation[i] = cellElevation;
			CellVolume[i] = _props.InterpolateCellVolume(i, cellElevation);
		}
	}

	public void SetInitialFaceVelocity(Memory<float> faceVelocity)
	{
		Span<float> faceVelocitySpan = faceVelocity.Span;
		for (int i = 0; i < FaceCount; i++)
		{
			FaceVelocity[i] = faceVelocitySpan[i];
		}
	}

	public void SetInitialStageMultisample(IResample<float> stageSurface, IResample<float> terrainSurface, ProgressReporter pr = null)
	{
		InvalidateCellWaterSurfaceGradients();
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		pr.ReportMessage("Computing Initial Water Surface");
		int sz = 2000;
		Memory<float> mem;
		using (SharedMemoryPool<float>.RentNoFill(sz, out mem))
		{
			Memory<float> mem2;
			using (SharedMemoryPool<float>.RentNoFill(sz, out mem2))
			{
				Memory<byte> mem3;
				using (SharedMemoryPool<byte>.RentNoFill(sz, out mem3))
				{
					PolygonRasterizer polygonRasterizer = new PolygonRasterizer(RasterDefinition.UnitRaster((int)Math.Sqrt(1000.0)));
					for (int i = 0; i < CellCount; i++)
					{
						Polygon polygon = _mesh.CellPolygon(i);
						Extent extent = polygon.Extent;
						RasterDefinition rdef = RasterDefinition.ComputeBoundingRaster(extent, 1000);
						polygonRasterizer.Reset(rdef);
						Memory<byte> mask = mem3.Slice(0, rdef.CellCount);
						mask.Span.Fill(0);
						polygonRasterizer.Rasterize(polygon, (int row, int start, int stop) =>
						{
							Span<byte> maskSpan = mask.Span;
							for (int j = start; j <= stop; j++)
							{
								int index = rdef.CellIndex(row, j);
								maskSpan[index] = 1;
							}
						});
						Memory<float> outData = mem.Slice(0, rdef.CellCount);
						Memory<float> outData2 = mem2.Slice(0, rdef.CellCount);
						outData.Span.Fill(-9999f);
						outData2.Span.Fill(-9999f);
						ResampleArgs args = new ResampleArgs(rdef);
						terrainSurface.Resample(args, outData);
						stageSurface.Resample(args, outData2);
						double cellVolume = 0.0;
						Span<byte> mask2 = mask.Span;
						Span<float> outDataSpan = outData.Span;
						Span<float> outData2Span = outData2.Span;
						double cellAreaSum = 0.0;
						for (int intervalIndex = 0; intervalIndex < rdef.CellCount; intervalIndex++)
						{
							if (mask2[intervalIndex] != 0)
							{
								cellAreaSum += rdef.CellArea;
								float outData22 = outData2Span[intervalIndex];
								float outData3 = outDataSpan[intervalIndex];
								if (outData22 != -9999f && outData3 != -9999f && !(outData22 <= outData3))
								{
									float outData22AboveOutData3 = outData22 - outData3;
									cellVolume += (double)outData22AboveOutData3 * rdef.CellArea;
								}
							}
						}
						if (cellVolume == 0.0)
						{
							CellVolume[i] = 0.0;
							CellElevation[i] = _props.CellMinElevation(i);
						}
						else
						{
							double area = polygon.Area;
							cellVolume *= area / cellAreaSum;
							CellVolume[i] = cellVolume;
							CellElevation[i] = _props.InterpolateCellElevation(i, cellVolume);
						}
						if (i % 100 == 0)
						{
							pr.ReportProgressFraction(i, CellCount);
						}
					}
					pr.ReportProgress(100);
				}
			}
		}
	}

	public void SetInitialVelocityFromFaceCenters(IResample<float> velocityXSurface, IResample<float> velocityYSurface, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		pr.ReportMessage("Computing Initial Face Velocities");
		Point[] points = (from f in Enumerable.Range(0, FaceCount)
			select _mesh.FaceMidPoint(f)).ToArray();
		float[] samplePoints = velocityXSurface.SamplePoints(points);
		float[] samplePointsArray = velocityYSurface.SamplePoints(points);
		for (int intervalIndex = 0; intervalIndex < FaceCount; intervalIndex++)
		{
			FaceVelocity[intervalIndex] = 0.0;
			if (samplePoints[intervalIndex] != -9999f && samplePointsArray[intervalIndex] != -9999f)
			{
				Vector2 vector2 = new Vector2(samplePoints[intervalIndex], samplePointsArray[intervalIndex]);
				Vector2 faceNormalF = _hmesh.FaceNormalF[intervalIndex];
				if (_mesh.Faces[intervalIndex].cellB >= 0)
				{
					FaceVelocity[intervalIndex] = Vector2.Dot(faceNormalF, vector2);
				}
			}
			if (intervalIndex % 100 == 0)
			{
				pr.ReportProgressFraction(intervalIndex, FaceCount);
			}
		}
		pr.ReportProgress(100);
	}

	public void SetInitialFaceStageFromFaceCenters(IResample<float> faceStageSurface)
	{
		Point[] points = (from f in Enumerable.Range(0, FaceCount)
			select _mesh.FaceMidPoint(f)).ToArray();
		float[] samplePoints = faceStageSurface.SamplePoints(points);
		for (int intervalIndex = 0; intervalIndex < FaceCount; intervalIndex++)
		{
			double faceMinimumElevation = _props.FaceMinimumElevations[intervalIndex];
			double faceMinimumElevation2 = ((samplePoints[intervalIndex] != -9999f) ? Math.Max(samplePoints[intervalIndex], faceMinimumElevation) : faceMinimumElevation);
			FaceElevation[intervalIndex] = faceMinimumElevation2;
			FaceArea[intervalIndex] = _props.InterpolateFaceArea(intervalIndex, faceMinimumElevation2);
		}
	}
}
