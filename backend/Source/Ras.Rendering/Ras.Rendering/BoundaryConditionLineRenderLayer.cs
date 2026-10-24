using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Memory;

namespace Ras.Rendering;

public class BoundaryConditionLineRenderLayer : PolylineRenderLayer
{
	private readonly BoundaryConditionLineLayer _bclLayer;

	private bool _paintAttachedFaces;

	private static readonly VectorSymbology _meshFaceVectorSymb = new VectorSymbology(Color.Red, 5.0);

	private static readonly VectorSymbology _internalCellVectorSymb = new VectorSymbology(Color.FromArgb(255, 0, 200, 100), 5.0);

	[PlotOption("Paint Attached Faces (Result Only)", EnabledIf = "IsResult")]
	public bool ShowAttachedFaces
	{
		get
		{
			return _paintAttachedFaces;
		}
		set
		{
			if (_paintAttachedFaces != value)
			{
				_paintAttachedFaces = value;
				IncrementRenderState();
			}
		}
	}

	public bool IsResult => _bclLayer.BoundaryCondition.IsResult;

	public BoundaryConditionLineRenderLayer(BoundaryConditionLineLayer bclLayer)
		: base(bclLayer.AsPolylineCollection())
	{
		_bclLayer = bclLayer;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		bool condition = false;
		condition |= base.RenderInternal(request, ref cache);
		if (ShowAttachedFaces)
		{
			condition |= RenderAttachedFaces(request);
		}
		return condition;
	}

	private bool RenderAttachedFaces(PlotRequest request)
	{
		BoundaryCondition boundaryCondition = _bclLayer.BoundaryCondition;
		if (!boundaryCondition.IsResult)
		{
			return false;
		}
		Mesh mesh = boundaryCondition.Result?.Geometry?.Mesh;
		if (mesh == null)
		{
			return false;
		}
		Memory<int> mem;
		using (SharedMemoryPool<int>.Rent(request.RasterQuery.CellCount, 0, out mem))
		{
			Color.FromArgb(100, 0, 150, 255).ToArgb();
			bool condition = false;
			HashSet<int> intSet = new HashSet<int>();
			for (int i = 0; i < _bclLayer.Count; i++)
			{
				BoundaryConditionLine boundaryConditionLine = _bclLayer[i];
				Polyline polyline = boundaryConditionLine.Polyline;
				if (!Polyline.IsValidPolyline(polyline))
				{
					continue;
				}
				List<ProfileFaceRange> perimeterFacesForBoundaryConditionLine = _bclLayer.BoundaryCondition.GetPerimeterFacesForBoundaryConditionLine(mesh, boundaryConditionLine);
				if (perimeterFacesForBoundaryConditionLine == null || perimeterFacesForBoundaryConditionLine.Count == 0)
				{
					if (mesh.Perimeter == null || !mesh.Perimeter.ContainsFuzzy(polyline))
					{
						continue;
					}
					List<ProfileCellRange> cellsForInternalBoundaryConditionLine = _bclLayer.BoundaryCondition.GetCellsForInternalBoundaryConditionLine(mesh, boundaryConditionLine);
					if (cellsForInternalBoundaryConditionLine == null)
					{
						continue;
					}
					foreach (ProfileCellRange cellsForInternalBoundaryConditionLine2 in cellsForInternalBoundaryConditionLine)
					{
						if (cellsForInternalBoundaryConditionLine2.Cell >= 0 && intSet.Add(-cellsForInternalBoundaryConditionLine2.Cell - 1))
						{
							Polygon polygon = mesh.CellPolygon(cellsForInternalBoundaryConditionLine2.Cell);
							if (polygon.Extent.Overlaps(request.RasterQuery.Extent))
							{
								condition |= PolylineRenderLayer.Paint(polygon, _internalCellVectorSymb, request);
							}
						}
					}
					continue;
				}
				foreach (ProfileFaceRange perimeterFacesForBoundaryConditionLine2 in perimeterFacesForBoundaryConditionLine)
				{
					if (perimeterFacesForBoundaryConditionLine2.Face >= 0 && intSet.Add(perimeterFacesForBoundaryConditionLine2.Face))
					{
						Polyline polyline2 = mesh.FacePolyline(perimeterFacesForBoundaryConditionLine2.Face);
						if (polyline2.Extent.Overlaps(request.RasterQuery.Extent))
						{
							condition |= PolylineRenderLayer.Paint(polyline2, _meshFaceVectorSymb, request);
						}
					}
				}
			}
			return condition;
		}
	}
}
