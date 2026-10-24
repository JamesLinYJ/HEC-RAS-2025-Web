using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.IO.Vectors;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Ras.Layers.Classic;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class ChannelModTerrainInput : TerrainRasterModificationLayer<ChannelProfile>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ChannelModTerrainInput");

	private const string TopologyGroupName = "Topology";

	private const string TinSegmentLengthName = "TinSegmentLength";

	private IResample<float> _resampler;

	public long _lastPreparedVersion = -1L;

	private double _tinMaxSegmentLength = 500.0;

	private long _tinSegLengthVersion = 1L;

	private RasterModType _modificationType;

	private object _prepareLock = new object();

	public override string TypeName => "Channel";

	public double TinMaxSegmentLength
	{
		get
		{
			return _tinMaxSegmentLength;
		}
		set
		{
			if (_tinMaxSegmentLength != value)
			{
				_tinMaxSegmentLength = value;
				_tinSegLengthVersion++;
			}
		}
	}

	public TopologyLayer TopologyLayer { get; private set; }

	public Topology Topology => TopologyLayer.Topology;

	[HashContributor]
	[Metadata("ModificationType")]
	public RasterModType ModificationType
	{
		get
		{
			return _modificationType;
		}
		set
		{
			if (_modificationType != value)
			{
				_modificationType = value;
				PropagateModificationType();
			}
		}
	}

	public new long VersionXY => base.VersionXY + Topology.VersionXY + _tinSegLengthVersion;

	private static string TopologyGroup(string layerGroup)
	{
		return H5Reader.PathCombine(layerGroup, "Topology");
	}

	private void PropagateModificationType()
	{
		if (!(_resampler is UnionResampler<float> unionResampler))
		{
			return;
		}
		foreach (IResample<float> resampler in unionResampler.Resamplers)
		{
			if (resampler is ChannelSectionResampler channelSectionResampler)
			{
				channelSectionResampler.ModificationType = _modificationType;
			}
		}
	}

	internal ChannelModTerrainInput()
	{
		TopologyLayer = new TopologyLayer((TopologyLayer _) => Name);
	}

	public ChannelModTerrainInput(Terrain terrain, string name)
		: base(terrain)
	{
		Name = name;
		TopologyLayer = new TopologyLayer((TopologyLayer _) => name);
		ChannelMod.EnsureTopologyColumns(Topology);
	}

	public void Prepare(OperationResult result = null)
	{
		try
		{
			_resampler = ChannelMod.CreateChannelResampler(Topology, Features, TinMaxSegmentLength, ModificationType, result);
			_lastPreparedVersion = VersionXY;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Building the channel resampler for '{Name}' failed; the layer will produce no surface.", Name);
			_resampler = null;
		}
	}

	public void Import1DGeometry(ClassicGeometry geo)
	{
		ArgumentNullException.ThrowIfNull(geo);
		Import1DGeometryTopology(geo);
		AddRange(Import1DGeometryProfiles(geo));
	}

	/// <summary>Creates the source profiles without changing the channel collection.</summary>
	public static ChannelProfile[] Import1DGeometryProfiles(ClassicGeometry geo)
	{
		ArgumentNullException.ThrowIfNull(geo);
		return geo.CrossSectionLayer.Features.Select(feature => new ChannelProfile
		{
			Line = feature.Polyline,
			Profile = feature.OverrideStationElevation,
			Name = $"{feature.RiverName} - {feature.ReachName} - {feature.RiverStation}"
		}).ToArray();
	}

	/// <summary>Imports graph topology only. Parent draft replay uses this after
	/// restoring the profiles from its ChannelMod feature snapshot.</summary>
	public void Import1DGeometryTopology(ClassicGeometry geo)
	{
		ArgumentNullException.ThrowIfNull(geo);
		List<Polyline> polylines = new List<Polyline>();
		polylines.AddRange(geo.BanklineLayer.Features.Select(feature => feature.Polyline));
		polylines.AddRange(geo.RiverLayer.Features.Select(feature => feature.Polyline));
		polylines.AddRange(geo.RiverEdgeLineLayer.Features.Select(feature => feature.Polyline));
		polylines.AddRange(geo.CrossSectionLayer.Features.Select(feature => feature.Polyline));

		Topology imported = Topology.Clone();
		imported.ImportPolylinesOrPolygons_Tin(polylines, new TopologyHelpers.TopologyImportOptions());
		imported.Complete();
		imported.RemoveExternalArcs();
		ChannelMod.EnsureTopologyColumns(imported);
		ChannelMod.AssignArcSources(imported, geo);
		TopologyLayer.Topology = imported;
	}

	public override IResample<float> GetResampler()
	{
		lock (_prepareLock)
		{
			if (_lastPreparedVersion != VersionXY)
			{
				Prepare();
			}
		}
		return _resampler;
	}

	protected override void SaveFeaturesInternal(H5Writer h5w, string hdfGroup, List<ChannelProfile> features)
	{
		base.SaveFeaturesInternal(h5w, hdfGroup, features);
		string topologyGroup = TopologyGroup(hdfGroup);
		TopologyReader.Write(h5w, topologyGroup, Topology);
		h5w.WriteAttribute(topologyGroup, "TinSegmentLength", TinMaxSegmentLength);
	}

	protected override void LoadMetadataInternal(H5Reader hr)
	{
		base.LoadMetadataInternal(hr);
		if (Name == null)
		{
			Name = "Channel Vector Mod Layer";
		}
		string topologyGroup = TopologyGroup(HDF5Group);
		if (!hr.ExistsAsGroup(topologyGroup))
		{
			topologyGroup = "Topology";
		}
		TopologyLayer.Topology = TopologyReader.Read(hr, topologyGroup);
		if (hr.TryReadAttributeScalar(topologyGroup, "TinSegmentLength", out double tinMaxSegmentLength))
		{
			TinMaxSegmentLength = tinMaxSegmentLength;
		}
	}

	protected override int GetFeatureHash()
	{
		int featureHash = base.GetFeatureHash();
		int dataHash = TopologyLayer.GetDataHash();
		int metadataHash = TopologyLayer.GetMetadataHash();
		return StableHash.CombineIntHashes(featureHash, dataHash, metadataHash);
	}
}
