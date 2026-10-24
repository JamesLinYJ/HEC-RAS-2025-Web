using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers.Classifiers;
using Utility.DataStructures;

namespace Ras.Observables;

public class ObservablePolygonClassificationInput : ObservableFeatureLayer<PolygonClassificationInput, PolygonClassifierMod, ObservablePolygonClassifierMod>, IObservableClassificationInput
{
	public const string DefaultName = "Classification Polygons";

	private PolygonClassificationInput _input;

	private ObservableClassificationLayer _classificationLayer;

	private IPolygonCollection _plWrapper;

	public IClassificationInput ClassificationInput => _input;

	public string Name
	{
		get
		{
			return ClassificationInput.Name;
		}
		set
		{
			SetProperty(ClassificationInput.Name, value, ClassificationInput, (IClassificationInput model, string newVal) =>
			{
				model.Name = newVal;
			}, "Name");
		}
	}

	public ObservableClassificationLayer ClassificationLayer => _classificationLayer;

	public event IObservableClassificationInput.ClassMapRebuiltEvent ClassMapRebuilt;

	public ObservablePolygonClassificationInput(ObservableClassificationLayer layer, PolygonClassificationInput input)
		: base(input, (Func<PolygonClassifierMod, ObservablePolygonClassifierMod>)((PolygonClassifierMod mod) => new ObservablePolygonClassifierMod(mod)))
	{
		_input = input;
		_classificationLayer = layer;
	}

	private void Features_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		throw new NotImplementedException();
	}

	public ObservablePolygonClassificationInput(ObservableClassificationLayer obsClassLayer, string name)
		: this(obsClassLayer, new PolygonClassificationInput(obsClassLayer.ClassificationLayer, name))
	{
	}

	public IPolygonCollection AsPolygonCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new ObservablePolygonFtLayerWrapped<PolygonClassificationInput, PolygonClassifierMod, ObservablePolygonClassifierMod>(this, (PolygonClassifierMod riv) => riv.Polygon, Layer.CreateDefaultMod);
		}
		return _plWrapper;
	}

	public void Add(Polygon pg, string classifier)
	{
		AddFeature(new PolygonClassifierMod(pg, classifier));
	}

	protected override void OnFeaturesListBuilt(INotifyList ftList)
	{
		base.OnFeaturesListBuilt(ftList);
		ftList.CollectionChanged += FeaturesListCollectionChanged;
		ExplicitCollectionChanged += FeaturesExplicitCollectionChanged;
		foreach (ObservablePolygonClassifierMod ft in ftList)
		{
			InitializeInput(ft);
		}
	}

	private void FeaturesListCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (ObservablePolygonClassifierMod newItem in e.NewItems)
			{
				InitializeInput(newItem);
			}
		}
		if (e.OldItems == null)
		{
			return;
		}
		foreach (ObservablePolygonClassifierMod oldItem in e.OldItems)
		{
			UninitializeInput(oldItem);
		}
	}

	private void FeaturesExplicitCollectionChanged(object sender, ExplicitCollectionChangedEventArgs<ObservablePolygonClassifierMod> args)
	{
		List<ObservablePolygonClassifierMod> newItems = args.NewItems;
		if (newItems == null || newItems.Count == 0)
		{
			List<ObservablePolygonClassifierMod> oldItems = args.OldItems;
			if (oldItems == null || oldItems.Count == 0)
			{
				return;
			}
		}
		ClassMapRebuilt?.Invoke(this);
	}

	private void InitializeInput(ObservablePolygonClassifierMod mod)
	{
		mod.ClassificationChanged += ModClassificationChanged;
	}

	private void UninitializeInput(ObservablePolygonClassifierMod mod)
	{
		mod.ClassificationChanged -= ModClassificationChanged;
	}

	private void ModClassificationChanged(ObservablePolygonClassifierMod sender, string newVal)
	{
		ClassMapRebuilt?.Invoke(this);
	}
}
