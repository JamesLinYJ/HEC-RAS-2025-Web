using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.DataStructures;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Rendering.RenderLayers;

public class RasClassicMeshErrorRenderLayer : RenderLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasClassicMeshErrorRenderLayer");

	private static readonly Pool<PolygonRasterizer> _rasterizerPool = new Pool<PolygonRasterizer>(() => PolygonRasterizer.MakeUninitializedPolygonRasterizer());

	private static readonly VectorSymbology _cellSymbology = new VectorSymbology(Color.FromArgb(110, 214, 45, 45), 1.0);

	private static readonly VectorSymbology _nodeSymbology = new VectorSymbology(Color.FromArgb(255, 120, 0, 0), 1.0, Color.FromArgb(255, 235, 45, 45))
	{
		PointSize = 7
	};

	private readonly IMeshCollection _meshes;

	private IReadOnlyList<MeshNodeDegreeError> _errors = Array.Empty<MeshNodeDegreeError>();

	private long _errorsVersion;

	private readonly Versioned<SpatialIndex<int>> _errorIndexCache = new Versioned<SpatialIndex<int>>();

	public override Extent ValidExtent => _meshes.Extent;

	public IReadOnlyList<MeshNodeDegreeError> Errors
	{
		get
		{
			return _errors;
		}
		set
		{
			_errors = value ?? Array.Empty<MeshNodeDegreeError>();
			_errorsVersion++;
			IncrementRenderState();
		}
	}

	private SpatialIndex<int> ErrorIndex => _errorIndexCache.Get(_errorsVersion, _errors, (IReadOnlyList<MeshNodeDegreeError> errors) =>
	{
		if (errors.Count == 0)
		{
			return (SpatialIndex<int>)null;
		}
		Extent extent = errors[0].NodePoint.Extent;
		foreach (MeshNodeDegreeError error in errors)
		{
			extent = Extent.Union(extent, error.NodePoint.Extent);
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent);
		for (int i = 0; i < errors.Count; i++)
		{
			spatialIndex.Add(errors[i].NodePoint, i);
		}
		return spatialIndex;
	});

	public RasClassicMeshErrorRenderLayer(IMeshCollection meshes)
	{
		_meshes = meshes;
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		IReadOnlyList<MeshNodeDegreeError> errors = _errors;
		if (errors.Count == 0)
		{
			return false;
		}
		try
		{
			_meshes.Lock.AcquireReaderLock(1);
			try
			{
				Extent other = plot.RasterQuery.Extent;
				if (!ValidExtent.Overlaps(in other))
				{
					return false;
				}
				return RenderCells(plot, errors, other) | RenderNodes(plot, errors, other);
			}
			finally
			{
				_meshes.Lock.ReleaseReaderLock();
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Possibly failed to acquire read lock.");
			return false;
		}
	}

	private bool RenderCells(PlotRequest plot, IReadOnlyList<MeshNodeDegreeError> errors, Extent query)
	{
		int toArgb = _cellSymbology.FillColor.ToArgb();
		PooledArray<int> pooledArray = ValueSharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out var mem);
		try
		{
			PoolHandle<PolygonRasterizer> poolHandle = _rasterizerPool.Acquire();
			PolygonRasterizer data = poolHandle.Data;
			data.Reset(plot.RasterQuery);
			(Memory<int>, int) t = (mem, toArgb);
			bool condition = false;
			for (int i = 0; i < errors.Count; i++)
			{
				MeshNodeDegreeError meshNodeDegreeError = errors[i];
				if (!TryGetMesh(meshNodeDegreeError.MeshIndex, out var mesh))
				{
					continue;
				}
				IReadOnlyList<int> cells = meshNodeDegreeError.Cells;
				for (int j = 0; j < cells.Count; j++)
				{
					int cellIndex = cells[j];
					if (cellIndex >= 0 && cellIndex < mesh.CellCount && mesh.CellExtent(cellIndex).Overlaps(in query))
					{
						condition |= data.Rasterize(mesh.CellPolygons[cellIndex], t, RENDER);
					}
				}
			}
			_rasterizerPool.Return(poolHandle);
			if (condition)
			{
				plot.Splat(mem);
			}
			return condition;
		}
		finally
		{
			pooledArray.Dispose();
		}
		static void RENDER(int idx, (Memory<int>, int) payload)
		{
			payload.Item1.Span[idx] = payload.Item2;
		}
	}

	private bool RenderNodes(PlotRequest plot, IReadOnlyList<MeshNodeDegreeError> errors, Extent query)
	{
		SpatialIndex<int> errorIndex = ErrorIndex;
		if (errorIndex == null)
		{
			return false;
		}
		List<int> elements = errorIndex.GetElements(query);
		if (elements.Count == 0)
		{
			return false;
		}
		return PointRenderLayer.PaintPoints(elements, (int i) => errors[i].NodePoint, _nodeSymbology, plot);
	}

	private bool TryGetMesh(int meshIndex, out Mesh mesh)
	{
		if (meshIndex < 0 || meshIndex >= _meshes.Count)
		{
			mesh = null;
			return false;
		}
		mesh = _meshes[meshIndex];
		return mesh != null;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + _meshes.VersionXY;
	}
}
