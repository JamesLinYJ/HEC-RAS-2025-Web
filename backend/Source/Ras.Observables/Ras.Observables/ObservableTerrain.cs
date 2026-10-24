using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility;
using Utility.DataStructures;
using Utility.Filtering;
using Utility.Logging;
using Utility.Threading;
using Visual.Observables;
using Visual.Observables.Filtering;

namespace Ras.Observables;

public class ObservableTerrain : ObservableCoreLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableTerrain");

	private AdvancedCollection<IObservableTerrainInput> _terrainInputs;

	private AdvancedCollection<IObservableTerrainInput> _terrainInputsPriorityOrder;

	private readonly object _vectorModTerrainInputsLoadLock = new object();

	private ObservableFilterResults<IObservableTerrainInput> _vectorModTerrainInputs;
	private AdvancedCollection<BatchJob> _currentlyImportingTerrains = new AdvancedCollection<BatchJob>();

	public static Type Type => typeof(ObservableTerrain);

	public new Terrain Layer => base.Layer as Terrain;

	public Terrain Terrain => Layer;

	public IReadOnlyAdvancedCollection<IObservableTerrainInput> TerrainInputs => _terrainInputs;

	public IReadOnlyAdvancedCollection<IObservableTerrainInput> TerrainInputsPriorityOrder => _terrainInputsPriorityOrder;

	public IReadOnlyAdvancedCollection<IObservableTerrainInput> VectorModTerrainInputs => LazyThreadsafe<ObservableFilterResults<IObservableTerrainInput>>.AllocateOnce(ref _vectorModTerrainInputs, _vectorModTerrainInputsLoadLock, () =>
	{
		FunctionalFilter<IObservableTerrainInput> functionalFilter = new FunctionalFilter<IObservableTerrainInput>((IObservableTerrainInput i) => (i is ObservableChannelModTerrainInput || i is ObservablePolygonRasterModTerrainInput || i is ObservableLeveeModTerrainInput || i is ObservableEllipseRasterModificationLayer) ? true : false);
		return new ObservableFilterResults<IObservableTerrainInput>(TerrainInputs, functionalFilter);
	}).FilteredItemsSource;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<BatchJob> CurrentlyImportingTerrains
	{
		get
		{
			return _currentlyImportingTerrains;
		}
		[MemberNotNull("_currentlyImportingTerrains")]
		set
		{
			if (!EqualityComparer<AdvancedCollection<BatchJob>>.Default.Equals(_currentlyImportingTerrains, value))
			{
				OnPropertyChanging("CurrentlyImportingTerrains");
				_currentlyImportingTerrains = value;
				OnPropertyChanged("CurrentlyImportingTerrains");
			}
		}
	}

	public ObservableTerrain(ObservableProject proj, Terrain terr)
		: base(proj, terr)
	{
		InitializeInputs();
		RebuildTerrainInputs();
	}

	private void InitializeInputs()
	{
		_terrainInputs = new AdvancedCollection<IObservableTerrainInput>();
		_terrainInputs.AddAll(from t in Layer.Layers.Select(CreateObservableTerrainInput)
			where t != null
			select t);
		_terrainInputs.CollectionChanged += _terrainInputs_CollectionChanged;
		_terrainInputsPriorityOrder = new AdvancedCollection<IObservableTerrainInput>(TerrainInputs.Reverse());
		foreach (IObservableTerrainInput terrainInput in TerrainInputs)
		{
			InitializeEventListeners(terrainInput);
		}
	}

	private IObservableTerrainInput CreateObservableTerrainInput(ITerrainInput input)
	{
		if (!(input is PolygonRasterModTerrainInput lyr))
		{
			if (!(input is RasterTerrainInput rasterTerrainInput))
			{
				if (!(input is ConstantTerrainInput input2))
				{
					if (!(input is FunctionalTerrainInput input3))
					{
						if (!(input is ChannelModTerrainInput lyr2))
						{
							if (!(input is LeveeModTerrainInput lyr3))
							{
								if (input is DebugTerrainInput input4)
								{
									return new ObservableDebugTerrainInput(input4);
								}
								throw new NotImplementedException("Missing input type.");
							}
							return new ObservableLeveeModTerrainInput(this, lyr3);
						}
						return new ObservableChannelModTerrainInput(lyr2);
					}
					return new ObservableFunctionalTerrainInput(input3);
				}
				return new ObservableConstantTerrainInput(input2);
			}
			return new ObservableRasterTerrainInput(this, rasterTerrainInput);
		}
		return new ObservablePolygonRasterModTerrainInput(this, lyr);
	}

	private void _terrainInputs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add)
		{
			if (e.NewItems != null && e.NewStartingIndex >= 0)
			{
				for (int i = 0; i < e.NewItems.Count; i++)
				{
					int index = _terrainInputs.Count - 1 - (e.NewStartingIndex + i);
					_terrainInputsPriorityOrder.Insert(index, (IObservableTerrainInput)e.NewItems[i]);
				}
			}
		}
		else if (e.Action == NotifyCollectionChangedAction.Remove)
		{
			if (e.OldItems == null || e.OldStartingIndex < 0)
			{
				return;
			}
			int countDifference = _terrainInputs.Count + e.OldItems.Count;
			for (int countOffset = e.OldItems.Count - 1; countOffset >= 0; countOffset--)
			{
				int oldStartingIndexTerm = countDifference - 1 - (e.OldStartingIndex + countOffset);
				if (oldStartingIndexTerm >= 0 && oldStartingIndexTerm < _terrainInputsPriorityOrder.Count)
				{
					_terrainInputsPriorityOrder.RemoveAt(oldStartingIndexTerm);
				}
			}
		}
		else if (e.Action == NotifyCollectionChangedAction.Replace)
		{
			if (e.OldItems == null || e.NewItems == null || e.OldStartingIndex < 0)
			{
				return;
			}
			int terrainInputsPriorityOrderTerm = _terrainInputsPriorityOrder.Count - 1 - e.OldStartingIndex;
			for (int j = 0; j < e.OldItems.Count && j < e.NewItems.Count; j++)
			{
				if (terrainInputsPriorityOrderTerm - j >= 0 && terrainInputsPriorityOrderTerm - j < _terrainInputsPriorityOrder.Count)
				{
					_terrainInputsPriorityOrder[terrainInputsPriorityOrderTerm - j] = (IObservableTerrainInput)e.NewItems[j];
				}
			}
		}
		else
		{
			if (e.Action == NotifyCollectionChangedAction.Move)
			{
				if (e.OldStartingIndex < 0 || e.NewStartingIndex < 0 || e.OldItems == null)
				{
					return;
				}
				int terrainInputsPriorityOrderTerm2 = _terrainInputsPriorityOrder.Count - 1 - e.OldStartingIndex;
				int count = e.OldItems.Count;
				List<IObservableTerrainInput> iObservableTerrainInputs = new List<IObservableTerrainInput>();
				for (int k = 0; k < count; k++)
				{
					if (terrainInputsPriorityOrderTerm2 - k >= 0 && terrainInputsPriorityOrderTerm2 - k < _terrainInputsPriorityOrder.Count)
					{
						iObservableTerrainInputs.Add(_terrainInputsPriorityOrder[terrainInputsPriorityOrderTerm2 - k]);
						_terrainInputsPriorityOrder.RemoveAt(terrainInputsPriorityOrderTerm2 - k);
					}
				}
				int index2 = _terrainInputsPriorityOrder.Count - e.NewStartingIndex;
				{
					foreach (IObservableTerrainInput iObservableTerrainInput in iObservableTerrainInputs)
					{
						_terrainInputsPriorityOrder.Insert(index2, iObservableTerrainInput);
					}
					return;
				}
			}
			_terrainInputsPriorityOrder.Clear();
			_terrainInputsPriorityOrder.AddAll(TerrainInputs.Reverse());
		}
	}

	private void InitializeEventListeners(IObservableTerrainInput input)
	{
	}

	private void RebuildTerrainInputs()
	{
	}

	public bool TryReorderTerrainInput(int fromIndex, int toIndex)
	{
		if (fromIndex >= Layer.LayerCount || toIndex >= Layer.LayerCount)
		{
			throw new ArgumentException("Bad index reordering");
		}
		if (fromIndex == toIndex)
		{
			return true;
		}
		if (IsWriteLockHeld)
		{
			Reorder(fromIndex, toIndex);
			Layer.Save();
		}
		else
		{
			using LockWrapper lockWrapper = TryGetWriterLock("Reordering Terrain inputs");
			if (lockWrapper == null)
			{
				return false;
			}
			Reorder(fromIndex, toIndex);
			Layer.Save();
		}
		RebuildTerrainInputs();
		return true;
	}

	public bool TryAddTerrainInput(IObservableTerrainInput input)
	{
		if (IsWriteLockHeld)
		{
			_terrainInputs.Add(input);
			Layer.Add(input.TerrainInput);
			Layer.Save();
		}
		else
		{
			using LockWrapper lockWrapper = TryGetWriterLock("Adding Terrain input");
			if (lockWrapper == null)
			{
				return false;
			}
			_terrainInputs.Add(input);
			Layer.Add(input.TerrainInput);
			Layer.Save();
		}
		RebuildTerrainInputs();
		return true;
	}

	public bool TryRemoveTerrainInput(IObservableTerrainInput input)
	{
		if (IsWriteLockHeld)
		{
			_terrainInputs.Remove(input);
			Layer.Remove(input.TerrainInput);
			Layer.Save();
		}
		else
		{
			using LockWrapper lockWrapper = TryGetWriterLock("Removing Terrain input");
			if (lockWrapper == null)
			{
				return false;
			}
			_terrainInputs.Remove(input);
			Layer.Remove(input.TerrainInput);
			Layer.Save();
		}
		RebuildTerrainInputs();
		return true;
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveTerrain(this);
		Layer.DisposeAndDeleteAllBackingFiles();
	}

	public void Reorder(int origIndex, int newIndex)
	{
		Layer.Reorder(origIndex, newIndex);
		_terrainInputs.Move(origIndex, newIndex);
	}

	public void AlertNewMod(int layerIdx, int featureIdx)
	{
		IObservableTerrainInput observableTerrainInput = _terrainInputs[layerIdx];
		if (!(observableTerrainInput is ObservablePolygonRasterModTerrainInput observablePolygonRasterModTerrainInput))
		{
			if (!(observableTerrainInput is ObservableEllipseRasterModificationLayer observableEllipseRasterModificationLayer))
			{
				if (observableTerrainInput is ObservableRasterTerrainInput)
				{
					throw new InvalidOperationException("Added Vector Mod to raster input layer");
				}
				throw new NotImplementedException();
			}
			observableEllipseRasterModificationLayer.AlertNewFeature(new ObservableEllipseRasterMod(observableEllipseRasterModificationLayer.Layer[featureIdx]));
		}
		else
		{
			observablePolygonRasterModTerrainInput.AlertNewFeature(new ObservablePolygonRasterMod(observablePolygonRasterModTerrainInput.Layer[featureIdx]));
		}
	}

	public void AlertNewMods(int layerIdx, IEnumerable<int> featureIdxs)
	{
		foreach (int featureIdx in featureIdxs)
		{
			AlertNewMod(layerIdx, featureIdx);
		}
	}

	public void AlertModRemoved(int layerIdx, int featureIdx)
	{
		((_terrainInputs[layerIdx] as IObservableFeatureLayer) ?? throw new NotSupportedException("can't remove a feature from a non feature layer")).AlertFeatureAtDeleted(featureIdx);
	}

	public void AlertModsRemoved(int layerIdx, IEnumerable<int> featureIdxs)
	{
		foreach (int featureIdx in featureIdxs)
		{
			AlertModRemoved(layerIdx, featureIdx);
		}
	}

	public void AlertNewLayer(int layerIdx)
	{
		ITerrainInput input = Layer.Layers[layerIdx];
		IObservableTerrainInput observableTerrainInput = CreateObservableTerrainInput(input);
		if (observableTerrainInput != null)
		{
			_terrainInputs.Add(observableTerrainInput);
		}
	}

	public void AlertLayerRemoved(int layerIdx)
	{
		_terrainInputs.RemoveAt(layerIdx);
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		if (!TryRenameByDeleteAndSaveAsH5Internal(inputName, Terrain.MakeFilename, out errorText))
		{
			return false;
		}
		foreach (ObservableGeometry geometry in Project.Geometries)
		{
			if (geometry.AssociatedTerrain == this)
			{
				Project.Project.GetGeometryAssociation(geometry.Geometry).AlertTerrainRenamed(inputName);
			}
		}
		return true;
	}
}
