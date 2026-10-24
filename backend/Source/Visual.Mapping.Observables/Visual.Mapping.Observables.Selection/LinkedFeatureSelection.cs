using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Layers;
using Utility;
using Utility.DataStructures;
using Visual.Observables.Linking;

namespace Visual.Mapping.Observables.Selection;

public class LinkedFeatureSelection<T> : ObservableObject, ILinkedFeatureSelection<T>, ILinkedFeatureSelection, IFeatureSelection, IFeatureSelection<T>, IFeatureSelectionView<T> where T : class
{
	private SelectedSet _selectedSet;

	private AdvancedCollection<T> _selectedFeatures;

	private T _lastSelectedFeature;

	private Func<T, int> _featureToIndex;

	private Func<int, T> _indexToFeature;

	private ISynchronizationContext _uiThreadSyncContext;

	IReadOnlyNotifyCollection<T> IFeatureSelectionView<T>.SelectedFeatures => SelectedFeatures;

	public INotifyList SelectedItems => SelectedFeatures;

	public Linkage Linkage { get; } = new Linkage();

	public int Count => SelectedFeatures.Count;

	public SelectedSet SelectedFeatureIDs => _selectedSet;

	public AdvancedCollection<T> SelectedFeatures
	{
		get
		{
			EnsureSelectedFeaturesInit();
			return _selectedFeatures;
		}
		private set
		{
			SetProperty(ref _selectedFeatures, value, "SelectedFeatures");
		}
	}

	public T LastSelectedFeature
	{
		get
		{
			EnsureSelectedFeaturesInit();
			return _lastSelectedFeature;
		}
		set
		{
			SetProperty(ref _lastSelectedFeature, value, "LastSelectedFeature");
		}
	}

	public Queue<SelectionCommand> WaitingSelectionCommands { get; } = new Queue<SelectionCommand>();

	public event NotifyCollectionChangedEventHandler SelectionChanged;

	public LinkedFeatureSelection(SelectedSet selectedSet, Func<T, int> featureToIndex, Func<int, T> indexToFeature, ISynchronizationContext uiThreadSyncContext = null)
	{
		_uiThreadSyncContext = uiThreadSyncContext;
		_selectedSet = selectedSet;
		_featureToIndex = featureToIndex;
		_indexToFeature = indexToFeature;
		EnsureSelectedFeaturesInit();
	}

	public int FeatureToIndex(T feature)
	{
		return _featureToIndex(feature);
	}

	public T IndexToFeature(int featureIndex)
	{
		return _indexToFeature(featureIndex);
	}

	private void EnsureSelectedFeaturesInit()
	{
		if (_selectedFeatures == null)
		{
			SelectedFeatures = new AdvancedCollection<T>();
			((IFeatureSelection<T>)this).InitializeFeatureSelection(_uiThreadSyncContext);
		}
	}

	public void NotifySelectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		SelectionChanged?.Invoke(sender, e);
	}
}
