using System;
using System.Collections.Generic;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Output;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Mapping;

public class MapperCache
{
	private static readonly ILogger _logger = LogHelper.GetLogger("MapperCache");

	private readonly Dictionary<int, MeshMapperCache> _perMeshCache = new Dictionary<int, MeshMapperCache>();

	private readonly IHorizontalOutputBlock _ob;

	private readonly MeshPropertyTables[] _tables;

	public MapperCache(IHorizontalOutputBlock ob, MeshPropertyTables[] tables)
	{
		_ob = ob;
		_tables = tables;
	}

	public void SetTimestepCacheCount(int count)
	{
		for (int i = 0; i < _tables.Length; i++)
		{
			GetPerMeshCache(i).SetTimestepCacheCount(count);
		}
	}

	public ImOnLoan<WaterSurfaceMeshInputs> Get2DWaterSurfaceInputs(int meshIdx, int profile)
	{
		return GetPerMeshCache(meshIdx).GetWaterSurface(profile);
	}

	private MeshMapperCache GetPerMeshCache(int meshIdx)
	{
		MeshMapperCache meshMapperCache;
		lock (_perMeshCache)
		{
			if (!_perMeshCache.TryGetValue(meshIdx, out meshMapperCache))
			{
				if (meshIdx >= _tables.Length)
				{
					throw new Exception("Queried mesh " + meshIdx + " out of range for provided property tables.");
				}
				meshMapperCache = new MeshMapperCache(_ob, _tables[meshIdx], meshIdx);
				_perMeshCache.Add(meshIdx, meshMapperCache);
			}
		}
		return meshMapperCache;
	}

	public ImOnLoan<VectorMeshInputs> Get2DVelocityInputs(int meshIdx, int profile)
	{
		return GetPerMeshCache(meshIdx).GetVelocity(profile);
	}

	public ImOnLoan<GenericMeshInputs> Get2DGenericInputs(GenericMapType mt, int meshIdx, int profile)
	{
		return GetPerMeshCache(meshIdx).GetGeneric(mt, profile);
	}
}
