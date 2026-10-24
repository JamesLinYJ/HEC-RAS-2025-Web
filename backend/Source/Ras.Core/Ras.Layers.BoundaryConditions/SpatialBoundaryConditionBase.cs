using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Geospatial;
using Geospatial.GDALAssist;
using Geospatial.IO.MetImport;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using H5Assist;
using Ras.Engine;
using Ras.Layers.Meteorology;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

[DebuggerDisplay("{ToString(),nq}")]
public abstract class SpatialBoundaryConditionBase : IObjectLog, IDataHash, ISpatialVersion
{
	public static class MessageCodes
	{
		public static readonly MessageClass DatasetProjectionNotFound = new MessageClass("SpatialBC_ProjectionNotFound", MessageSeverity.Warning, "{Identifier} could not identify a projection for {ObjectType}. Assuming native projection.");

		public static readonly MessageClass BadRasterParameter = new MessageClass("SpatialBC_BadRasterDefinition", MessageSeverity.Warning, "{Identifier} has bad parameters for rasterizing gauged data.");

		public static Message GetDatasetProjectionNotFound(string identifier, string objectType)
		{
			return DatasetProjectionNotFound.Create(identifier, objectType);
		}

		public static Message GetBadRasterParameter(string identifier)
		{
			return DatasetProjectionNotFound.Create(identifier);
		}
	}

	public const string GaugeDatasetName = "Gauges";

	public const string DefaultImportedRasterDatasetName = "Imported Raster";

	protected long _versionXy;

	private Freezer _freezer;

	private readonly List<Message> _messages = new List<Message>();

	protected List<MetGaugedData> _gauges = new List<MetGaugedData>();

	private SpatialDataType _spatialDataType;

	public abstract string LayerTypeName { get; }

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _versionXy;
		}
	}

	public BoundaryCondition BoundaryCondition { get; }

	public bool IsResult => BoundaryCondition.IsResult;

	public bool IsReadOnly => IsResult;

	[Metadata("Description", Optional = true)]
	public string Description { get; set; }

	[HashContributor]
	[Metadata("Enabled")]
	public bool IsEnabled { get; set; }

	[HashContributor]
	[Metadata("Ratio", SetValueToDefaultIfKeyMissing = false)]
	public float Ratio { get; set; } = 1f;

	public virtual bool CanRatioData { get; } = true;

	[HashContributor]
	[Metadata("Spatial Data Type")]
	public SpatialDataType SpatialDataType
	{
		get
		{
			return _spatialDataType;
		}
		set
		{
			_spatialDataType = value;
			Interlocked.Increment(ref _versionXy);
		}
	}

	[HashContributor]
	public List<MetGaugedData> Gauges => _gauges;

	[HashContributor]
	[Metadata("Raster")]
	public RasterDefinition Raster { get; set; }

	[HashContributor]
	[Metadata("Interpolation Method")]
	public MetInterpolationType InterpolationMethod { get; set; }

	[HashContributor]
	[Metadata("Grid Data Location")]
	public DataLocation GridDataLocation { get; set; }

	[HashContributor]
	[Metadata("Grid External Connection", Optional = true)]
	public string ExternalGriddedConnection { get; set; }

	[HashContributor]
	[Metadata("Grid External Path", Optional = true)]
	public string ExternalGriddedPath { get; set; }

	public string HDFGroupName { get; private set; }

	public string ImportedRasterDataset => HDFGroupName + "/Imported Raster";

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public SpatialBoundaryConditionBase(BoundaryCondition bc, string h5GroupName)
	{
		BoundaryCondition = bc;
		HDFGroupName = h5GroupName;
	}

	public void NotifyRasterDataChange()
	{
		Interlocked.Increment(ref _versionXy);
	}

	public void Save(H5Writer h5w)
	{
		bool hasImportedRaster = h5w.ExistsAsDataset(ImportedRasterDataset)
			|| this is SpatialVectorBoundaryConditionBase vector && (h5w.ExistsAsDataset(vector.ImportedRasterDatasetA) || h5w.ExistsAsDataset(vector.ImportedRasterDatasetB));
		if (IsDefault() && !hasImportedRaster)
		{
			h5w.Delete(HDFGroupName);
			return;
		}
		GeospatialH5Metadata.Write(this, h5w, HDFGroupName);
		FeatureIO.Write(h5w, HDFGroupName, _gauges, "Gauges");
	}

	public void Load(H5Reader hr)
	{
		Ratio = 1f;
		if (!GeospatialH5Metadata.Read(this, hr, HDFGroupName))
		{
			if (_gauges == null || _gauges.Count > 0)
			{
				_gauges = new List<MetGaugedData>();
			}
			SetDefault();
		}
		else
		{
			_gauges = FeatureIO.Read<MetGaugedData>(hr, HDFGroupName, "Gauges");
			BoundaryCondition.MetStations.ReconcileLoadedGauges(_gauges);
		}
	}

	public abstract void CopyDataToInternal(H5Writer hw);

	public void SetDefault()
	{
		SpatialDataType = SpatialDataType.Constant;
		IsEnabled = false;
		Ratio = 1f;
		Raster = default;
		GridDataLocation = DataLocation.Local;
		Description = "";
		ExternalGriddedConnection = "";
		ExternalGriddedPath = "";
		SetDefaultInternal();
	}

	protected abstract void SetDefaultInternal();

	public bool IsDefault()
	{
		if (_gauges.Count == 0 && SpatialDataType == SpatialDataType.Constant && !IsEnabled && (Ratio == 1f || Ratio == 0f) && GridDataLocation == DataLocation.Local && Raster.EqualRasterDefinition(default) && string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(ExternalGriddedConnection) && string.IsNullOrEmpty(ExternalGriddedPath))
		{
			return IsDefaultInternal();
		}
		return false;
	}

	protected abstract bool IsDefaultInternal();

	public void SetUseImportedRaster()
	{
		IsEnabled = true;
		SpatialDataType = SpatialDataType.Gridded;
		GridDataLocation = DataLocation.Local;
	}

	public virtual bool IsReallyEnabledForCompute()
	{
		return IsEnabled;
	}

	public abstract OperationResult PrepareForCompute();

	public abstract OperationResult InitializeComputeDriver(ComputeControl computeControl, BoundaryConditionCollection bcs);

	public Projection TryGetProjection()
	{
		if (BoundaryCondition == null)
		{
			return null;
		}
		if (BoundaryCondition.Project == null)
		{
			BoundaryCondition.LoadProjectMetadataFromH5Header(out var _, out var projection, out var _);
			return projection;
		}
		return BoundaryCondition.Project.Projection;
	}

	internal void NotifyMetStationRemoved(MeteorologicStation station)
	{
		for (int i = 0; i < _gauges.Count; i++)
		{
			MetGaugedData metGaugedData = _gauges[i];
			if (metGaugedData.Station == station)
			{
				metGaugedData.Station = null;
				metGaugedData.StationName = null;
			}
		}
	}

	public IEnumerable<Message> GetMessages()
	{
		return _messages;
	}

	protected void AddMessage(Message msg)
	{
		_messages.Add(msg);
	}

	protected void AddMessages(OperationResult res)
	{
		_messages.AddRange(res.Messages);
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public abstract void WriteGriddedSelection(IGriddedDataSelection selection);
}
