using System;
using System.Collections.Specialized;
using Geospatial.Rendering.Layers;

namespace Visual.Mapping.Observables.Selection;

public class SelectedSetFeatureSelection : IFeatureSelection
{
	public SelectedSet SelectedFeatureIDs { get; }

	public event NotifyCollectionChangedEventHandler SelectionChanged;

	public SelectedSetFeatureSelection(SelectedSet selectedSet)
	{
		ArgumentNullException.ThrowIfNull(selectedSet, "selectedSet");
		SelectedFeatureIDs = selectedSet;
		SelectedFeatureIDs.ElementAdded += SelectedSetElementAdded;
		SelectedFeatureIDs.ElementRemoved += SelectedSetElementRemoved;
		SelectedFeatureIDs.Cleared += SelectedSetCleared;
	}

	public void Select(int featureId)
	{
		SelectedFeatureIDs.Add(featureId);
	}

	public void Unselect(int featureId)
	{
		SelectedFeatureIDs.Remove(featureId);
	}

	public bool IsSelected(int featureId)
	{
		return SelectedFeatureIDs.Contains(featureId);
	}

	public int FeatureToIndex(object feature)
	{
		throw new NotSupportedException("SelectedSetFeatureSelection addresses features by id only.");
	}

	public void NotifySelectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		SelectionChanged?.Invoke(sender, e);
	}

	private void SelectedSetElementAdded(SelectedSet owner, int id)
	{
		NotifySelectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, id));
	}

	private void SelectedSetElementRemoved(SelectedSet owner, int id)
	{
		NotifySelectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, id));
	}

	private void SelectedSetCleared(SelectedSet owner)
	{
		NotifySelectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
