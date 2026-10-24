using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Hydraulics;
using Ras.Layers.Classic;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility.Logging;
using Utility.DataStructures;
using Utility.Validation;

namespace Ras.Observables;

public class ObservableChannelModTerrainInput : ObservableFeatureLayer<ChannelModTerrainInput, ChannelProfile, ObservableChannelProfile>, IObservableTerrainInput, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
{
	public delegate void ProfileChangedEvent(ObservableChannelProfile profile);

	private IPolylineCollection _plWrapper;
	private readonly HashSet<ObservableChannelProfile> _observedProfiles = new HashSet<ObservableChannelProfile>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableChannelModLayer TopologyLayer
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<ObservableChannelModLayer>.Default.Equals(field, value))
			{
				OnPropertyChanging("TopologyLayer");
				field = value;
				OnPropertyChanged("TopologyLayer");
			}
		}
	}

	public ITerrainInput TerrainInput => Layer;

	public double TinMaxSegmentLength
	{
		get
		{
			return Layer.TinMaxSegmentLength;
		}
		set
		{
			SetProperty(Layer.TinMaxSegmentLength, value, Layer, (ChannelModTerrainInput lyr, double tinMaxSegmentLength) =>
			{
				lyr.TinMaxSegmentLength = tinMaxSegmentLength;
			}, "TinMaxSegmentLength");
		}
	}

	public string Name
	{
		get
		{
			return Layer.Name;
		}
		set
		{
			SetProperty(Layer.Name, value, Layer, (ChannelModTerrainInput lyr, string textValue) =>
			{
				lyr.Name = textValue;
			}, "Name");
		}
	}

	public RasterModType ModificationType
	{
		get
		{
			return Layer.ModificationType;
		}
		set
		{
			SetProperty(Layer.ModificationType, value, Layer, (ChannelModTerrainInput lyr, RasterModType modificationType) =>
			{
				lyr.ModificationType = modificationType;
			}, "ModificationType");
		}
	}

	public bool HasErrors => false;

	public event ProfileChangedEvent DataChanged;

	public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public ObservableChannelModTerrainInput(ChannelModTerrainInput lyr)
		: base(lyr, (Func<ChannelProfile, ObservableChannelProfile>)MakeObservableProfile)
	{
		TopologyLayer = new ObservableChannelModLayer(this);
	}

	protected override void OnFeaturesListBuilt(INotifyList ftList)
	{
		base.OnFeaturesListBuilt(ftList);
		RefreshObservedProfiles(ftList);
		ftList.CollectionChanged += (_, _) => RefreshObservedProfiles(ftList);
	}

	private void RefreshObservedProfiles(INotifyList features)
	{
		HashSet<ObservableChannelProfile> current = features.Cast<ObservableChannelProfile>().ToHashSet();
		foreach (ObservableChannelProfile old in _observedProfiles.Except(current).ToArray())
		{
			old.DataChanged -= ProfileDataChanged;
			_observedProfiles.Remove(old);
		}
		foreach (ObservableChannelProfile added in current.Except(_observedProfiles))
		{
			added.DataChanged += ProfileDataChanged;
			_observedProfiles.Add(added);
		}
	}

	private void ProfileDataChanged(ObservableChannelProfile profile) => DataChanged?.Invoke(profile);

	private static ObservableChannelProfile MakeObservableProfile(ChannelProfile profile)
	{
		return new ObservableChannelProfile(profile);
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new ObservablePolylineFtLayerWrapped<ChannelModTerrainInput, ChannelProfile, ObservableChannelProfile>(this, (ChannelProfile prof) => prof.Line, (Polyline pl) => new ChannelProfile
			{
				Line = pl,
				Profile = new StationElevationProfile(Array.Empty<double>(), Array.Empty<double>())
			});
		}
		return _plWrapper;
	}

	public OperationResult PrepareToImport1DGeometry(ClassicGeometry geo, out Topology cloned)
	{
		try
		{
			List<Polyline> polylines = new List<Polyline>();
			foreach (Bankline feature2 in geo.BanklineLayer.Features)
			{
				polylines.Add(feature2.Polyline);
			}
			foreach (River feature3 in geo.RiverLayer.Features)
			{
				polylines.Add(feature3.Polyline);
			}
			foreach (Edgeline feature4 in geo.RiverEdgeLineLayer.Features)
			{
				polylines.Add(feature4.Polyline);
			}
			foreach (CrossSection feature5 in geo.CrossSectionLayer.Features)
			{
				polylines.Add(feature5.Polyline);
				ChannelProfile feature = new ChannelProfile
				{
					Line = feature5.Polyline,
					Profile = feature5.OverrideStationElevation,
					Name = $"{feature5.RiverName} - {feature5.ReachName} - {feature5.RiverStation}"
				};
				AddFeature(feature);
			}
			TopologyHelpers.TopologyImportOptions opts = new TopologyHelpers.TopologyImportOptions();
			Topology topology = Layer.Topology;
			if (!topology.Lock.TryEnterReadLock(5000))
			{
				cloned = null;
				return OperationResult.Fail(GeneralMessageCodes.GetTimeoutOccurred("Acquiring Topology Read Lock"));
			}
			cloned = topology.Clone();
			cloned.ImportPolylinesOrPolygons_Tin(polylines, opts);
			cloned.Complete();
			cloned.RemoveExternalArcs();
			ChannelMod.EnsureTopologyColumns(cloned);
			ChannelMod.AssignArcSources(cloned, geo);
			topology.Lock.ExitReadLock();
			return true;
		}
		catch (Exception ex)
		{
			cloned = null;
			return OperationResult.Fail("Importing 1D Geometry", ex);
		}
	}

	public void ValidateProperty(object localValue, [CallerMemberName] string propertyName = null)
	{
		throw new NotImplementedException();
	}

	public IEnumerable GetErrors(string propertyName)
	{
		throw new NotImplementedException();
	}
}
