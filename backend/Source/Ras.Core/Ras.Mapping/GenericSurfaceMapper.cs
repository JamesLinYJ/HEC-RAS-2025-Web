using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Ras.Output;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Mapping;

public class GenericSurfaceMapper : Mapper
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GenericSurfaceMapper");

	public override MapTypes MapType { get; } = MapTypes.Generic;

	public GenericMapType GenericMapType { get; }

	public GenericSurfaceMapper(GenericMapType mt, IHorizontalOutputBlock ob, MeshPropertyTables[] tables, MapperCache cache = null)
		: base(ob, tables, cache)
	{
		GenericMapType = mt;
	}

	public override void Compute(int profile, GeometryMapPoints mps, Memory<float> clipTerrain, Memory<float> results)
	{
		if (mps.InputCount < 0)
		{
			throw new ArgumentException("mps");
		}
		if (results.Length != mps.InputCount)
		{
			throw new ArgumentException("results");
		}
		ValidateProfileIndex(profile);
		results.Span.Fill(-9999f);
		if (mps.MappedPixelCount != 0)
		{
			ComputeValueInternal2D(mps, profile, results);
		}
	}

	protected void ComputeValueInternal2D(GeometryMapPoints mps, int profileIdx, Memory<float> outValues)
	{
		if (mps.FlatMapped2DAreas != null)
		{
			foreach (FlatMeshMap flatMapped2DArea in mps.FlatMapped2DAreas)
			{
				if (flatMapped2DArea.Cells.Count == 0)
				{
					continue;
				}
				using ImOnLoan<GenericMeshInputs> imOnLoan = _mapperCache.Get2DGenericInputs(GenericMapType, flatMapped2DArea.MeshIndex, profileIdx);
				if (imOnLoan == null || imOnLoan.Item == null)
				{
					throw new Exception("Error getting water surface values.");
				}
				if (GenericMapType.MeshElementType == MeshElementType.Cell)
				{
					flatMapped2DArea.ComputeCellValuesFlat(imOnLoan.Item.Values, outValues, MeshClip.Empty, RenderOptions);
					continue;
				}
				_ = GenericMapType.MeshElementType;
				_ = 1;
				throw new NotImplementedException();
			}
		}
		if (mps.SlopingMapped2DAreas == null)
		{
			return;
		}
		foreach (SlopingMeshMap slopingMapped2DArea in mps.SlopingMapped2DAreas)
		{
			if (slopingMapped2DArea.Cells.Count == 0)
			{
				continue;
			}
			if (RenderOptions.CellRenderMode == CellRenderMethod.AsComputed)
			{
				throw new NotImplementedException();
			}
			if (RenderOptions.CellRenderMode == CellRenderMethod.Pretty)
			{
				using (ImOnLoan<GenericMeshInputs> imOnLoan2 = _mapperCache.Get2DGenericInputs(GenericMapType, slopingMapped2DArea.MeshIndex, profileIdx))
				{
					if (imOnLoan2 == null || imOnLoan2.Item == null)
					{
						throw new Exception("Error getting generic mesh values: " + GenericMapType.VariableName);
					}
					if (GenericMapType.MeshElementType == MeshElementType.Cell)
					{
						slopingMapped2DArea.ComputeCellValuesFlat(imOnLoan2.Item.Values, outValues, MeshClip.Empty, RenderOptions);
					}
					else if (GenericMapType.MeshElementType == MeshElementType.Face)
					{
						throw new NotImplementedException();
					}
				}
				continue;
			}
			throw new NotImplementedException();
		}
	}
}
