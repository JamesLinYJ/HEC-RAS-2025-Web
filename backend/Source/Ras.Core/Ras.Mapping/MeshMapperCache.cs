using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Ras.Output;
using Utility.Memory;

namespace Ras.Mapping;

internal class MeshMapperCache
{
	private readonly IHorizontalOutputBlock _ob;

	private readonly RecycledCache<int, WaterSurfaceMeshInputs> _wsCache;

	private readonly RecycledCache<int, VectorMeshInputs> _velCache;

	private readonly Dictionary<string, RecycledCache<int, GenericMeshInputs>> _genericCache;

	private readonly int _nMesh;

	private readonly int _meshIdx;

	private readonly Mesh _mesh;

	private readonly MeshPropertyTables _tables;

	internal MeshMapperCache(IHorizontalOutputBlock ob, MeshPropertyTables tables, int meshIdx)
	{
		MeshMapperCache meshMapperCache = this;
		_ob = ob;
		_meshIdx = meshIdx;
		_tables = tables;
		if (tables == null || tables.CellMinimumElevations == null || tables.FaceMinimumElevations == null)
		{
			throw new ArgumentNullException("tables or some necessary internal buffer.");
		}
		Mesh mesh = ob.Geometry.FlowAreaLayer[_meshIdx].Mesh;
		_mesh = mesh;
		_nMesh = ob.Geometry.FlowAreaLayer.Count;
		_wsCache = new RecycledCache<int, WaterSurfaceMeshInputs>(() => new WaterSurfaceMeshInputs(mesh, tables), (int pfIdx, WaterSurfaceMeshInputs wsmi) =>
		{
			int nMesh = meshMapperCache._nMesh;
			Memory<float>[] floatSpanArray = new Memory<float>[nMesh];
			for (int i = 0; i < nMesh; i++)
			{
				floatSpanArray[i] = Memory<float>.Empty;
			}
			floatSpanArray[meshMapperCache._meshIdx] = wsmi.CellWaterSurfaces;
			if (meshMapperCache._ob.TryReadPerMesh(OutputVariables.Depth, pfIdx, floatSpanArray))
			{
				// Only this mesh was requested; the other mesh buffers are empty.
				_tables.ConvertDepthToStage(wsmi.CellWaterSurfaces);
				wsmi.Build();
			}
		}, (WaterSurfaceMeshInputs wsmi) =>
		{
		});
		_velCache = new RecycledCache<int, VectorMeshInputs>(() => new VectorMeshInputs(mesh), (int pfIdx, VectorMeshInputs vmi) =>
		{
			ImOnLoan<WaterSurfaceMeshInputs> imOnLoan = (ImOnLoan<WaterSurfaceMeshInputs>)(vmi.Cleanup = meshMapperCache.GetWaterSurface(pfIdx));
			vmi.WaterSurfaceInputs = imOnLoan.Item;
			int nMesh = meshMapperCache._nMesh;
			Memory<float>[] floatSpanArraySpan = new Memory<float>[nMesh];
			for (int i = 0; i < nMesh; i++)
			{
				floatSpanArraySpan[i] = Memory<float>.Empty;
			}
			floatSpanArraySpan[meshMapperCache._meshIdx] = vmi.FacePerpendicularValues;
			if (meshMapperCache._ob.TryReadPerMesh(OutputVariables.Velocity, pfIdx, floatSpanArraySpan))
			{
				vmi.Build();
			}
		}, (VectorMeshInputs vmi) =>
		{
			vmi.Cleanup.Dispose();
			vmi.Cleanup = null;
		});
		_genericCache = new Dictionary<string, RecycledCache<int, GenericMeshInputs>>();
	}

	public void SetTimestepCacheCount(int count)
	{
		_wsCache.MaxCachedCount = count;
		_velCache.MaxCachedCount = count;
	}

	public ImOnLoan<WaterSurfaceMeshInputs> GetWaterSurface(int profile)
	{
		return _wsCache.Get(profile);
	}

	public ImOnLoan<VectorMeshInputs> GetVelocity(int profile)
	{
		return _velCache.Get(profile);
	}

	public ImOnLoan<GenericMeshInputs> GetGeneric(GenericMapType mt, int profile)
	{
		RecycledCache<int, GenericMeshInputs> genericCache = null;
		lock (_genericCache)
		{
			if (!_genericCache.TryGetValue(mt.VariableName, out genericCache))
			{
				genericCache = new RecycledCache<int, GenericMeshInputs>(() => new GenericMeshInputs(mt, _mesh, _tables), (int pfIdx, GenericMeshInputs inputs) =>
				{
					int nMesh = _nMesh;
					Memory<float>[] floatSpanArray = new Memory<float>[nMesh];
					for (int i = 0; i < nMesh; i++)
					{
						floatSpanArray[i] = Memory<float>.Empty;
					}
					floatSpanArray[_meshIdx] = inputs.Values;
					OutputVariable variable = new OutputVariable(mt.VariableName, d1: false, sa: false, d2: true, mt.MeshElementType)
					{
						IsDebug = mt.IsDebug
					};
					_ob.TryReadPerMesh(variable, pfIdx, floatSpanArray);
				}, (GenericMeshInputs wsmi) =>
				{
				});
				_genericCache[mt.VariableName] = genericCache;
			}
		}
		return genericCache.Get(profile);
	}
}
