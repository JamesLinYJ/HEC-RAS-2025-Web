using System;
using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{Name}: {CellCount} Cells")]
public class FlowArea : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FlowArea");

	private Polygon _perimeter;

	private VersionTracker _perimeterTracker;

	private PointCollection _cellCenters;

	private VersionTracker _cellCentersTracker;

	private Mesh _mesh = Mesh.NullMesh();

	private VersionTracker _meshTracker;

	private long _cachedMeshHashVersion = -1L;

	private int _cachedMeshHash;

	private double _targetedCellSpacing = double.NaN;

	public PointCollection CellCenters
	{
		get
		{
			return _cellCenters;
		}
		set
		{
			_cellCentersTracker.Assign(ref _cellCenters, value);
		}
	}

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Polygon")]
	public Polygon Perimeter
	{
		get
		{
			return _perimeter;
		}
		set
		{
			_perimeterTracker.Assign(ref _perimeter, value);
		}
	}

	public long InputsVersion => _perimeterTracker.GetVersion(_perimeter) + _cellCentersTracker.GetVersion(_cellCenters);

	[HashContributor]
	[DatasetName("$Name")]
	[SaveIf("CanSave")]
	public Mesh Mesh
	{
		get
		{
			return _mesh;
		}
		set
		{
			_meshTracker.Assign(ref _mesh, value);
		}
	}

	public double TargetedCellSpacing
	{
		get
		{
			if (double.IsNaN(_targetedCellSpacing))
			{
				_targetedCellSpacing = ((Mesh == null || Mesh.Perimeter == null) ? double.NaN : Math.Sqrt(Math.Abs(Mesh.Perimeter.Area) / (double)Mesh.CellCount));
			}
			return _targetedCellSpacing;
		}
		set
		{
			_targetedCellSpacing = value;
		}
	}

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	public long VersionXY => _meshTracker.GetVersion(_mesh) + InputsVersion;

	[HashContributor]
	[Metadata("Cell Count")]
	public int CellCount => Mesh?.Cells.Length ?? 0;

	public MeshPropertyTables PropertyTables { get; set; }

	public MeshPropertyTableSignature PropertyTableSignature { get; set; }

	[Metadata("Property Tables Pinned", Optional = true)]
	public bool PropertyTablesPinned { get; set; }

	public bool HasMeshErrors { get; set; }

	public bool CanSave => !HasMeshErrors;

	public HydraulicMeshComputer HydraulicMesh => Mesh.MetadataCache;

	public Extent Extent => Mesh?.Extent ?? Extent.NullExtent;

	public FlowArea(Mesh mesh, string name)
	{
		Mesh = mesh;
		Name = name;
		Perimeter = mesh.Perimeter;
	}

	public FlowArea()
	{
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public int GetMeshDataHash(bool isResult)
	{
		if (isResult && PropertyTableSignature != null)
		{
			return PropertyTableSignature.MeshHash;
		}
		long version = _meshTracker.GetVersion(_mesh);
		if (version == _cachedMeshHashVersion)
		{
			return _cachedMeshHash;
		}
		_cachedMeshHash = Mesh.GetDataHash();
		_cachedMeshHashVersion = version;
		return _cachedMeshHash;
	}

	public FlowArea Clone()
	{
		FlowArea flowArea = new FlowArea();
		flowArea._perimeter = _perimeter.Clone();
		flowArea._cellCenters = _cellCenters?.Clone();
		flowArea._mesh = _mesh.Clone();
		flowArea.PropertyTables = PropertyTables.Clone(flowArea._mesh);
		flowArea.Name = Name;
		flowArea.HasMeshErrors = HasMeshErrors;
		flowArea.PropertyTablesPinned = PropertyTablesPinned;
		return flowArea;
	}
}
