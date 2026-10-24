using System.Collections.Generic;
using Geospatial;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public abstract class BasicRectangleParams : BaseParams
{
	public int CellsWide;

	public int CellsTall;

	public double CellSize;

	public double CellSizeX;

	public double Slope;

	public TurbulenceParams Turbulence = new TurbulenceParams();

	public bool HexMesh;

	public double RotateDegrees;

	private Mesh _mesh;

	public override int ReportFrequency { get; set; } = 1;

	public override Extent SystemExtent => new Extent(0.0, (double)CellsWide * ((CellSizeX > 0.0) ? CellSizeX : CellSize), 0.0, (double)CellsTall * CellSize);

	public float SimplePlanarTerrain(Point pt)
	{
		return (float)(Slope * (SystemExtent.MaxX - pt.X));
	}

	public override float TerrainElevation(Point pt)
	{
		return SimplePlanarTerrain(pt);
	}

	public override Mesh CreateMesh()
	{
		if (_mesh != null)
		{
			return _mesh;
		}
		_mesh = MeshFactory.FromExtent(SystemExtent, CellsWide, CellsTall, HexMesh);
		return _mesh;
	}

	public override ISpatialTransformer GetTransformer(Mesh mesh)
	{
		if (RotateDegrees == 0.0)
		{
			return null;
		}
		return new RotateTransformer(mesh.Extent.Center, RotateDegrees, RotationUnits.Degrees, Orientation.Clockwise);
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.UseDiffusion = Turbulence.IsActive;
		pl.TurbulenceModel = Turbulence.Model;
		pl.BaseEddyViscosity = Turbulence.BaseEddyViscosity;
		pl.LongMixCoeff = Turbulence.LongMixCoeff;
		pl.TransMixCoeff = Turbulence.TransMixCoeff;
		pl.HorizMixCoeff = Turbulence.HorizMixCoeff;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		double cellSizeXCellSizeTerm = 0.1 * ((CellSizeX > 0.0) ? CellSizeX : CellSize);
		double x = extent.MinX - cellSizeXCellSizeTerm;
		double x2 = extent.MaxX + cellSizeXCellSizeTerm;
		double minY = extent.MinY;
		double maxY = extent.MaxY;
		Polyline pline = Polyline.FromSegment(new Segment(new Point(x, minY), new Point(x, maxY)));
		Polyline pline2 = Polyline.FromSegment(new Segment(new Point(x2, maxY), new Point(x2, minY)));
		BoundaryConditionLine upstreamBC = GetUpstreamBC(bc, pline);
		BoundaryConditionLine downstreamBC = GetDownstreamBC(bc, pline2);
		if (upstreamBC == null && downstreamBC == null)
		{
			return null;
		}
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>();
		if (upstreamBC != null)
		{
			boundaryConditionLines.Add(upstreamBC);
		}
		if (downstreamBC != null)
		{
			boundaryConditionLines.Add(downstreamBC);
		}
		return boundaryConditionLines;
	}

	public virtual BoundaryConditionLine GetUpstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		return null;
	}

	public virtual BoundaryConditionLine GetDownstreamBC(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		return null;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.Turbulence = Turbulence;
	}
}
