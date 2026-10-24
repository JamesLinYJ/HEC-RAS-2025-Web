using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Geospatial.Rendering.Layers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;
using Visual.Observables.Notifications;

namespace Visual.Mapping.Observables.Selection;

public interface IFeatureSelection
{
	SelectedSet SelectedFeatureIDs { get; }

	event NotifyCollectionChangedEventHandler SelectionChanged;

	void Select(int featureId);

	void Unselect(int featureId);

	bool IsSelected(int featureId);

	int FeatureToIndex(object feature);

	void NotifySelectionChanged(object sender, NotifyCollectionChangedEventArgs e);

	void UnselectAll()
	{
		foreach (int element in new List<int>(SelectedFeatureIDs.Elements))
		{
			Unselect(element);
		}
	}
}
public interface IFeatureSelection<T> : IFeatureSelection where T : class
{
	private static readonly ILogger _logger = LogHelper.GetLogger("IFeatureSelection");

	AdvancedCollection<T> SelectedFeatures { get; }

	T LastSelectedFeature { get; set; }

	Queue<SelectionCommand> WaitingSelectionCommands { get; }

	int FeatureToIndex(T feature);

	T IndexToFeature(int featureIndex);

	void ProcessWaitingSelectionCommands()
	{
		try
		{
			if (WaitingSelectionCommands.Count == 0)
			{
				return;
			}
			List<int> newItemScratchSpace = ILinkedFeatureSelectionHelpers.NewItemScratchSpace;
			newItemScratchSpace.Clear();
			List<int> oldItemScratchSpace = ILinkedFeatureSelectionHelpers.OldItemScratchSpace;
			oldItemScratchSpace.Clear();
			bool condition = false;
			while (WaitingSelectionCommands.Count != 0)
			{
				SelectionCommand selectionCommand = WaitingSelectionCommands.Dequeue();
				if (selectionCommand.Type == SelectionCommandType.Add)
				{
					if (oldItemScratchSpace.Contains(selectionCommand.FeatureID))
					{
						oldItemScratchSpace.Remove(selectionCommand.FeatureID);
					}
					else
					{
						newItemScratchSpace.Add(selectionCommand.FeatureID);
					}
				}
				else if (selectionCommand.Type == SelectionCommandType.Remove)
				{
					if (newItemScratchSpace.Contains(selectionCommand.FeatureID))
					{
						newItemScratchSpace.Remove(selectionCommand.FeatureID);
					}
					else
					{
						oldItemScratchSpace.Add(selectionCommand.FeatureID);
					}
				}
				else if (selectionCommand.Type == SelectionCommandType.Clear)
				{
					oldItemScratchSpace.Clear();
					newItemScratchSpace.Clear();
					condition = true;
				}
			}
			if (condition)
			{
				if (newItemScratchSpace.Count == 0)
				{
					SelectedFeaturesCleared(SelectedFeatureIDs);
					SetLastSelectedFeature(null);
				}
				else
				{
					SetFeatureSelection(newItemScratchSpace.Select(IndexToFeature));
					SetLastSelectedFeature(IndexToFeature(newItemScratchSpace[newItemScratchSpace.Count - 1]));
				}
				return;
			}
			if (newItemScratchSpace.Count == 0 && oldItemScratchSpace.Count == SelectedFeatures.Count)
			{
				SelectedFeaturesCleared(SelectedFeatureIDs);
				SetLastSelectedFeature(null);
				return;
			}
			if (newItemScratchSpace.Count > 0)
			{
				SelectFeatures(newItemScratchSpace.Select(IndexToFeature));
			}
			if (oldItemScratchSpace.Count > 0)
			{
				UnselectFeatures(oldItemScratchSpace.Select(IndexToFeature));
			}
			if (newItemScratchSpace.Count == 0 && oldItemScratchSpace.Count != 0)
			{
				SetLastSelectedFeature(null);
			}
			else if (newItemScratchSpace.Count > 0)
			{
				SetLastSelectedFeature(IndexToFeature(newItemScratchSpace[newItemScratchSpace.Count - 1]));
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to update feature selection, attempting to recover.");
			ToastNotifiers.Default.SendError("Failed to update feature selection because of an unexpected error, recovering.");
			try
			{
				if (SelectedFeatureIDs.IsEmpty)
				{
					SelectedFeaturesCleared(SelectedFeatureIDs);
					SetLastSelectedFeature(null);
				}
				else
				{
					SetFeatureSelection(SelectedFeatureIDs.Select(IndexToFeature));
					SetLastSelectedFeature(IndexToFeature(SelectedFeatureIDs.Elements.Last()));
				}
			}
			catch (Exception exception2)
			{
				_logger.LogError(exception2, "Failed to recover feature selection after already failing to update feature selection. Selection now broken.");
				ToastNotifiers.Default.SendError("Failed to recover from selection resetting, please restart the application.");
			}
		}
	}

	void InitializeFeatureSelection(ISynchronizationContext uiThreadSyncContext)
	{
		if (uiThreadSyncContext == null)
		{
			throw new ArgumentException("A valid SynchronizationContext is required.");
		}
		SelectedFeatures.CollectionChanged += SelectedFeaturesChanged;
		SelectedFeatureIDs.ElementAdded += (SelectedSet owner, int fid) =>
		{
			WaitingSelectionCommands.Enqueue(new SelectionCommand(fid, SelectionCommandType.Add));
			uiThreadSyncContext.Sync(ProcessWaitingSelectionCommands);
		};
		SelectedFeatureIDs.ElementRemoved += (SelectedSet owner, int fid) =>
		{
			WaitingSelectionCommands.Enqueue(new SelectionCommand(fid, SelectionCommandType.Remove));
			uiThreadSyncContext.Sync(ProcessWaitingSelectionCommands);
		};
		SelectedFeatureIDs.Cleared += (SelectedSet owner) =>
		{
			WaitingSelectionCommands.Enqueue(new SelectionCommand(-1, SelectionCommandType.Clear));
			uiThreadSyncContext.Sync(ProcessWaitingSelectionCommands);
		};
		foreach (int element in SelectedFeatureIDs.Elements)
		{
			SelectedFeatures.Add(IndexToFeature(element));
		}
	}

	protected void SetLastSelectedFeature(T feature)
	{
		LastSelectedFeature = feature;
	}

	void SelectedFeaturesCleared(SelectedSet owner)
	{
		if (SelectedFeatures.Count != 0)
		{
			SelectedFeatures.Clear();
		}
	}

	void FeatureIdUnselected(SelectedSet owner, int id)
	{
		UnselectFeature(IndexToFeature(id));
		FinalizeUnselection(id);
	}

	void FeatureIDSelected(SelectedSet owner, int id)
	{
		SelectFeature(IndexToFeature(id));
		FinalizeSelection(id);
	}

	void SelectedFeaturesChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		NotifySelectionChanged(sender, e);
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			if (SelectedFeatures.Count == 0)
			{
				SelectedFeatureIDs.Clear();
				return;
			}
			HashSet<int> intSet = new HashSet<int>();
			foreach (T selectedFeature in SelectedFeatures)
			{
				intSet.Add(FeatureToIndex(selectedFeature));
			}
			if (intSet.SetEquals(SelectedFeatureIDs))
			{
				return;
			}
			SelectedFeatureIDs.Clear();
			{
				foreach (int ordinal2 in intSet)
				{
					SelectedFeatureIDs.Add(ordinal2);
				}
				return;
			}
		}
		if (e.NewItems != null)
		{
			if ((e.OldItems == null || e.OldItems.Count == 0) && SelectedFeatures.Count == SelectedFeatureIDs.Count)
			{
				return;
			}
			foreach (T newItem in e.NewItems)
			{
				if (newItem != null)
				{
					SelectFeature(FeatureToIndex(newItem));
				}
			}
			IList? newItems = e.NewItems;
			FinalizeSelection(newItems[newItems.Count - 1] as T);
		}
		if (e.OldItems == null || ((e.NewItems == null || e.NewItems.Count == 0) && SelectedFeatures.Count == SelectedFeatureIDs.Count))
		{
			return;
		}
		foreach (T oldItem in e.OldItems)
		{
			if (oldItem != null)
			{
				UnselectFeature(FeatureToIndex(oldItem));
				FinalizeUnselection(oldItem);
			}
		}
	}

	private IEnumerable<T> Unwrap(IList items)
	{
		foreach (object localValue in items)
		{
			if (localValue == null)
			{
				continue;
			}
			Type type = localValue.GetType();
			if (typeof(T) == type)
			{
				yield return (T)localValue;
			}
			else
			{
				if (!typeof(T).IsAssignableTo(typeof(IList)))
				{
					continue;
				}
				IList items2 = (IList)localValue;
				foreach (T unwrap in Unwrap(items2))
				{
					yield return unwrap;
				}
			}
		}
	}

	void Select(T feature)
	{
		SelectFeature(feature);
		SelectFeature(FeatureToIndex(feature));
		FinalizeSelection(feature);
	}

	private void FinalizeSelection(int featureId)
	{
		FinalizeSelection(IndexToFeature(featureId));
	}

	private void FinalizeSelection(T feature)
	{
		LastSelectedFeature = feature;
	}

	protected void SelectFeature(T feature)
	{
		if (!SelectedFeatures.Contains(feature))
		{
			SelectedFeatures.Add(feature);
		}
	}

	protected void SelectFeatures(IEnumerable<T> feature)
	{
		SelectedFeatures.AddAll(feature);
	}

	protected void SetFeatureSelection(IEnumerable<T> feature)
	{
		SelectedFeatures.SetAll(feature);
	}

	private void SelectFeature(int featureId)
	{
		if (featureId == -1)
		{
			throw new ArgumentException("Tried to select a feature with an invalid ID, this might be because the feature was selected before being added.");
		}
		if (!SelectedFeatureIDs.Contains(featureId))
		{
			SelectedFeatureIDs.Add(featureId);
		}
	}

	void Unselect(T feature)
	{
		UnselectFeature(feature);
		UnselectFeature(FeatureToIndex(feature));
		FinalizeUnselection(feature);
	}

	private void FinalizeUnselection(int featureId)
	{
		FinalizeUnselection(IndexToFeature(featureId));
	}

	private void FinalizeUnselection(T feature)
	{
		if (LastSelectedFeature == feature)
		{
			LastSelectedFeature = null;
		}
	}

	protected void UnselectFeature(T feature)
	{
		if (SelectedFeatures.Contains(feature))
		{
			SelectedFeatures.Remove(feature);
		}
	}

	protected void UnselectFeatures(IEnumerable<T> features)
	{
		SelectedFeatures.RemoveAll(features);
	}

	private void UnselectFeature(int featureId)
	{
		if (featureId != -1 && SelectedFeatureIDs.Contains(featureId))
		{
			SelectedFeatureIDs.Remove(featureId);
		}
	}

	bool IsSelected(T feature)
	{
		return SelectedFeatures.Contains(feature);
	}

	void IFeatureSelection.Select(int featureId)
	{
		Select(IndexToFeature(featureId));
	}

	void IFeatureSelection.Unselect(int featureId)
	{
		Unselect(IndexToFeature(featureId));
	}

	bool IFeatureSelection.IsSelected(int featureId)
	{
		return IsSelected(IndexToFeature(featureId));
	}

	int IFeatureSelection.FeatureToIndex(object feature)
	{
		if (!(feature is T feature2))
		{
			throw new ArgumentException("Context type " + feature.GetType().Name + " not convertible to instance of " + typeof(T).Name);
		}
		return FeatureToIndex(feature2);
	}
}
