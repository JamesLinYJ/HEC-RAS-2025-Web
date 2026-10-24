using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Features;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Hydraulics.Culverts;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Culvert Barrels")]
public class CulvertBarrelLayer : GeometryLayerBase<CulvertBarrel>
{
	public static class MessageCodes
	{
		public static readonly MessageClass BarrelUpstreamInvertBelowCell = new MessageClass("Barrel_UpstreamInvertBelowCell", MessageSeverity.Error, "Culvert Barrel \"{BarrelName}\" (FID[{FID}]) has an upstream invert below the cell's minimum elevation. ({CellInvert} > {BarrelInvert})");

		public static readonly MessageClass BarrelDownstreamInvertBelowCell = new MessageClass("Barrel_DownstreamInvertBelowCell", MessageSeverity.Error, "Culvert Barrel \"{BarrelName}\" (FID[{FID}]) has a downstream invert below the cell's minimum elevation. ({CellInvert} > {BarrelInvert})");

		public static Message GetBarrelUpstreamInvertBelowCell(string barrelName, int fid, double cellMinElev, double invert)
		{
			return BarrelUpstreamInvertBelowCell.Create(barrelName, fid, cellMinElev, invert);
		}

		public static Message GetBarrelDownstreamInvertBelowCell(string barrelName, int fid, double cellMinElev, double invert)
		{
			return BarrelDownstreamInvertBelowCell.Create(barrelName, fid, cellMinElev, invert);
		}
	}

	public const string CulvertBaseGroup = "/Geometry/Culverts";

	public const string HDF5GroupName = "/Geometry/Culverts/Barrels";

	protected override string HDF5Group => "/Geometry/Culverts/Barrels";

	public CulvertBarrelLayer(Geometry geom)
		: base(geom)
	{
	}

	public override void Add(CulvertBarrel localValue)
	{
		base.Add(localValue);
		localValue.Geometry = Geometry;
	}

	protected override List<CulvertBarrel> LoadFeaturesInternal()
	{
		List<CulvertBarrel> loadFeaturesInternal = base.LoadFeaturesInternal();
		for (int i = 0; i < loadFeaturesInternal.Count; i++)
		{
			loadFeaturesInternal[i].Geometry = Geometry;
		}
		return loadFeaturesInternal;
	}

	public void ClipInto(Polygon polygon, CulvertBarrelLayer target, GeometryClipOptions? opts = null)
	{
		if (!opts.HasValue)
		{
			opts = GeometryClipOptions.Default();
		}
		GeometryClipOptions geometryClipOptions = opts.Value;
		for (int i = 0; i < Count; i++)
		{
			CulvertBarrel culvertBarrel = Features[i];
			Polyline polyline = culvertBarrel.Polyline;
			if (polyline != null && polyline.Count >= 2 && (geometryClipOptions.FullyContainedGeometry ? polygon.Contains(polyline) : polygon.ContainsOrIntersects(polyline)))
			{
				target.Add((CulvertBarrel)culvertBarrel.Clone());
			}
		}
	}

	public OperationResult IsReadyToSolve()
	{
		if (Count == 0)
		{
			return true;
		}
		OperationResult operationResult = new OperationResult(success: true);
		for (int i = 0; i < Count; i++)
		{
			OperationResult operationResult2 = Features[i].IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	public OperationResult CanCompute()
	{
		OperationResult operationResult = true;
		foreach (CulvertBarrel feature in Features)
		{
			OperationResult operationResult2 = feature.IsValid();
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
		}
		return operationResult;
	}

	public OperationResult PrepareForCompute()
	{
		OperationResult operationResult = IsReadyToSolve();
		if (!operationResult)
		{
			return operationResult;
		}
		return TryComputeBarrelPropertyTables();
	}

	public OperationResult TryComputeBarrelPropertyTables()
	{
		Mesh mesh = Geometry.Mesh;
		if (mesh == null)
		{
			return OperationResult.Fail("Mesh has not been computed.");
		}
		MeshPropertyTables hydraulicPropertyTables = Geometry.HydraulicPropertyTables;
		if (hydraulicPropertyTables == null)
		{
			return OperationResult.Fail("2D hydraulic property tables have not been computed yet (needed for culvert barrel inverts).");
		}
		OperationResult operationResult = true;
		for (int i = 0; i < Count; i++)
		{
			CulvertBarrel culvertBarrel = Features[i];
			OperationResult operationResult2 = TryComputeTables(i, mesh, hydraulicPropertyTables, out var tables);
			if (!operationResult2)
			{
				operationResult.AddFailure(operationResult2);
			}
			else
			{
				culvertBarrel.Tables = tables;
			}
		}
		return operationResult;
	}

	private OperationResult TryComputeTables(int fid, Mesh mesh, MeshPropertyTables props, out BarrelPropertyTables tables)
	{
		CulvertBarrel culvertBarrel = Features[fid];
		Polyline polyline = culvertBarrel.Polyline;
		if (!Polyline.IsValidPolyline(polyline))
		{
			tables = null;
			return OperationResult.Fail(FeatureMessageCodes.GetBadOrMissingFeatureData(culvertBarrel.Name, fid));
		}
		if (double.IsNaN(culvertBarrel.UpstreamInvert) || double.IsNaN(culvertBarrel.DownstreamInvert))
		{
			tables = null;
			return OperationResult.Fail(new Message("Barrel data for {BarrelName} incomplete, upstream/downstream invert invalid.", culvertBarrel.Name));
		}
		Point point = polyline.First();
		Point point2 = polyline.Last();
		int[] cells = mesh.FindCells(new Point[2] { point, point2 });
		tables = new BarrelPropertyTables();
		tables.UpstreamCellIndex = cells[0];
		tables.DownstreamCellIndex = cells[1];
		OperationResult operationResult = tables.IsValid();
		if (!operationResult)
		{
			return operationResult;
		}
		double cellMinElevation = props.CellMinElevation(tables.UpstreamCellIndex);
		double cellMinElevation2 = props.CellMinElevation(tables.DownstreamCellIndex);
		OperationResult operationResult2 = true;
		if (cellMinElevation > culvertBarrel.UpstreamInvert)
		{
			operationResult2.AddFailure(MessageCodes.GetBarrelUpstreamInvertBelowCell(culvertBarrel.Name, fid, cellMinElevation, culvertBarrel.UpstreamInvert));
		}
		if (cellMinElevation2 > culvertBarrel.DownstreamInvert)
		{
			operationResult2.AddFailure(MessageCodes.GetBarrelDownstreamInvertBelowCell(culvertBarrel.Name, fid, cellMinElevation2, culvertBarrel.DownstreamInvert));
		}
		return operationResult2;
	}
}
