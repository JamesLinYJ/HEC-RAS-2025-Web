using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Structures;

namespace Ras.Engine;

public class ComputePartition
{
	private Mesh _mesh;

	private int[] _cellMapping;

	private (int, int)[] _faceMapping;

	private Dictionary<int, int> _cellGlobalToLocal;

	private Dictionary<int, int> _faceGlobalToLocal;

	public ISolver solver { get; private set; }

	public ReadOnlySpan<int> CellMapping => _cellMapping;

	public ReadOnlySpan<(int, int)> FaceMapping => _faceMapping;

	public bool IdentityMapping
	{
		get
		{
			if (_cellMapping == null)
			{
				return _faceMapping == null;
			}
			return false;
		}
	}

	public BoundaryConditionCollection BoundaryConditions => solver.GetBoundaryConditions();

	public HydraulicStructureCollection HydraulicStructures => solver.GetHydraulicStructures();

	public ComputePartition(ISolver solver, Mesh globalMesh)
	{
		this.solver = solver;
		_mesh = solver.GetMesh();
		_cellMapping = null;
		_faceMapping = null;
		_cellGlobalToLocal = null;
		_faceGlobalToLocal = null;
	}

	public ComputePartition(SolverControl solverControl, List<int> cellIndexes, Mesh globalMesh, MeshPropertyTables globalProps, MeshWaterState globalState, ComputeDriver driver)
	{
		bool condition = true;
		if (cellIndexes != null)
		{
			if (cellIndexes.Count != globalMesh.CellCount)
			{
				condition = false;
			}
			else
			{
				for (int i = 0; i < cellIndexes.Count; i++)
				{
					if (cellIndexes[i] != i)
					{
						condition = false;
						break;
					}
				}
			}
		}
		MeshPropertyTables props;
		if (condition)
		{
			_mesh = globalMesh;
			props = globalProps;
		}
		else
		{
			_mesh = globalMesh.Subset(cellIndexes, out _cellMapping, out _faceMapping, out _cellGlobalToLocal, out _faceGlobalToLocal);
			props = globalProps.Subset(_mesh, _cellMapping, _faceMapping);
		}
		solver = solverControl.SolverFactory(driver, _mesh, props);
		if (condition)
		{
			solver.SetPrincipalState(globalState.CellElevation, globalState.CellVolume, globalState.FaceVelocity);
			return;
		}
		double[] cellWaterSurface = _cellMapping.Select((int cIdxGlobal) => globalState.CellElevation[cIdxGlobal]).ToArray();
		double[] cellVolume = _cellMapping.Select((int cIdxGlobal) => globalState.CellVolume[cIdxGlobal]).ToArray();
		double[] faceVelocity = _faceMapping.Select(((int, int) fm) => globalState.FaceVelocity[fm.Item1] * (double)fm.Item2).ToArray();
		solver.SetPrincipalState(cellWaterSurface, cellVolume, faceVelocity);
	}

	public void UpdateMeshFlags(Span<ComputeControl.CellFlag> cellFlags, Dictionary<string, ComputeControl.CellFlag> cellFlagMap, MeshTags meshTags)
	{
		if (meshTags == null)
		{
			return;
		}
		foreach (KeyValuePair<string, ComputeControl.CellFlag> cellFlagMap2 in cellFlagMap)
		{
			string key = cellFlagMap2.Key;
			IEnumerable<int> enumerable = meshTags.Cells.Get(key, true);
			ComputeControl.CellFlag cellFlag = cellFlagMap2.Value;
			foreach (int enumerable2 in enumerable)
			{
				if (MapCellToLocal(enumerable2) >= 0)
				{
					cellFlags[MapCellToLocal(enumerable2)] |= cellFlag;
				}
			}
		}
	}

	public int MapCellToGlobal(int localCell)
	{
		if (IdentityMapping)
		{
			return localCell;
		}
		return _cellMapping[localCell];
	}

	public (int, int) MapFaceToGlobal(int localFace)
	{
		if (IdentityMapping)
		{
			return (localFace, 1);
		}
		return _faceMapping[localFace];
	}

	public int MapCellToLocal(int globalCell)
	{
		if (IdentityMapping)
		{
			return globalCell;
		}
		return _cellGlobalToLocal.GetValueOrDefault(globalCell, -1);
	}

	public Tuple<int, int> MapFaceToLocal(int globalFace)
	{
		if (!_faceGlobalToLocal.TryGetValue(globalFace, out var localValue))
		{
			return new Tuple<int, int>(-1, 1);
		}
		return new Tuple<int, int>(localValue, _faceMapping[localValue].Item2);
	}
}
