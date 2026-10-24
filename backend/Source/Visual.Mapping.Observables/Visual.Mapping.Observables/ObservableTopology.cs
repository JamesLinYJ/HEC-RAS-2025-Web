using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors.Topology;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public abstract class ObservableTopology : ObservableBaseTopology
{
	protected AdvancedCollection<ObservableTagDefinition> _controlPointTagDefinitions;

	public override Topology Topology => base.Topology as Topology;

	public abstract IObservableFeatureSet<ObservableBaseControlPoint> ControlPoints { get; }

	public IReadOnlyAdvancedCollection<ObservableTagDefinition> ControlPointTagDefinitions => LazyThreadsafe<AdvancedCollection<ObservableTagDefinition>>.AllocateOnce(ref _controlPointTagDefinitions, _loadLock, this, (ObservableTopology self) => TagHelpers.BuildTagDefinitions(self, self.ControlPointMetadata));

	public ObservableTable ControlPointMetadata { get; private set; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<ObservableTag> RecentControlPointTags
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<ObservableTag>>.Default.Equals(field, value))
			{
				OnPropertyChanging("RecentControlPointTags");
				field = value;
				OnPropertyChanged("RecentControlPointTags");
			}
		}
	} = new AdvancedCollection<ObservableTag>();

	public ObservableTopology(Topology topology)
		: base(topology)
	{
		ControlPointMetadata = new ObservableTable(topology.ControlPointMetadata);
		ControlPointMetadata.ColumnAdded += ControlPointMetadataColumnAdded;
		ControlPointMetadata.ColumnRemoved += ControlPointMetadataColumnRemoved;
		ControlPointMetadata.ColumnRenamed += ControlPointMetadataColumnRenamed;
		ControlPointMetadata.CellValueChanged += OnControlPointCellValueChanged;
	}

	private void ControlPointMetadataColumnAdded(ObservableTable sender, int idx)
	{
		_ = ControlPointTagDefinitions;
		string colName = sender.Columns[idx].Name;
		if (!_controlPointTagDefinitions.Where((ObservableTagDefinition tagDef) => tagDef.Key.RoughlyEquals(colName)).Any() && TagHelpers.TryBuildTagDefinition(this, sender, idx, out var created))
		{
			_controlPointTagDefinitions.Add(created);
		}
	}

	private void ControlPointMetadataColumnRemoved(ObservableTable sender, int removedIdx, string columnName)
	{
		if (_controlPointTagDefinitions == null)
		{
			return;
		}
		ObservableTagDefinition observableTagDefinition = _controlPointTagDefinitions.FirstOrDefault((ObservableTagDefinition td) => td.Key.RoughlyEquals(columnName));
		if (observableTagDefinition == null)
		{
			return;
		}
		foreach (ObservableTopologyFeature featuresWithTag in observableTagDefinition.FeaturesWithTag.ToList())
		{
			featuresWithTag.ForceRemoveTag(columnName);
		}
		_controlPointTagDefinitions.Remove(observableTagDefinition);
	}

	private void ControlPointMetadataColumnRenamed(ObservableTable sender, int colIdx, string oldName, string newName)
	{
		if (_controlPointTagDefinitions != null)
		{
			ObservableTagDefinition observableTagDefinition = _controlPointTagDefinitions.FirstOrDefault((ObservableTagDefinition td) => td.Key.RoughlyEquals(oldName));
			if (observableTagDefinition != null)
			{
				observableTagDefinition.Key = newName;
			}
		}
	}

	private void OnControlPointCellValueChanged(ObservableTable sender, int rowIdx, int colIdx, object localValue)
	{
		ControlPoints.HandleCellValueChanged(rowIdx, colIdx, localValue);
	}

	public void AlertNewControlPointAppended(ObservableBaseControlPoint controlPoint)
	{
		ControlPoints.AlertNew(controlPoint.Fid);
	}

	public void AlertNewControlPointsAppended(int appendedCount)
	{
		ControlPoints.AlertRangeAppended(appendedCount);
	}

	public void AlertNewControlPointsInserted(IEnumerable<int> indices)
	{
		ControlPoints.AlertRangeInserted(indices);
	}

	public void AlertControlPointDeleted(ObservableBaseControlPoint controlPoint)
	{
		ControlPoints.AlertAtDeleted(controlPoint.Fid);
	}

	public void AlertControlPointAtDeleted(int index)
	{
		ControlPoints.AlertAtDeleted(index);
	}

	public void AlertControlPointsAtDeleted(IEnumerable<int> indices)
	{
		ControlPoints.AlertRangeAtDeleted(indices);
	}

	public void DeleteControlPoint(int featureIdx)
	{
		AlertControlPointAtDeleted(featureIdx);
		Topology.RemoveRegionControlPoint(featureIdx);
	}

	public ObservableTagDefinition CreateNewControlPointTag(string tagName, TagType tagType)
	{
		return ObservableBaseTopology.CreateNewTag(ControlPointMetadata, ControlPointTagDefinitions, tagName, tagType);
	}

	public virtual void CloneIntoBackingTopology(Topology source, Topology destination)
	{
		source.Clone(destination);
	}

	public void HotSwapTopology_WriteLockHeld(Topology newTopo)
	{
		CloneIntoBackingTopology(newTopo, Topology);
		if (ArcMetadata != null)
		{
			ArcMetadata.ColumnAdded -= ArcMetadataColumnAdded;
			ArcMetadata.ColumnRemoved -= ArcMetadataColumnRemoved;
			ArcMetadata.ColumnRenamed -= ArcMetadataColumnRenamed;
			ArcMetadata.CellValueChanged -= OnArcCellValueChanged;
		}
		if (ControlPointMetadata != null)
		{
			ControlPointMetadata.ColumnAdded -= ControlPointMetadataColumnAdded;
			ControlPointMetadata.ColumnRemoved -= ControlPointMetadataColumnRemoved;
			ControlPointMetadata.ColumnRenamed -= ControlPointMetadataColumnRenamed;
			ControlPointMetadata.CellValueChanged -= OnControlPointCellValueChanged;
		}
		if (NodeMetadata != null)
		{
			NodeMetadata.CellValueChanged -= OnNodeCellValueChanged;
		}
		ArcMetadata = new ObservableTable(Topology.ArcMetadata);
		ControlPointMetadata = new ObservableTable(Topology.ControlPointMetadata);
		NodeMetadata = new ObservableTable(Topology.NodeMetadata);
		ArcMetadata.ColumnAdded += ArcMetadataColumnAdded;
		ArcMetadata.ColumnRemoved += ArcMetadataColumnRemoved;
		ArcMetadata.ColumnRenamed += ArcMetadataColumnRenamed;
		ArcMetadata.CellValueChanged += OnArcCellValueChanged;
		ControlPointMetadata.ColumnAdded += ControlPointMetadataColumnAdded;
		ControlPointMetadata.ColumnRemoved += ControlPointMetadataColumnRemoved;
		ControlPointMetadata.ColumnRenamed += ControlPointMetadataColumnRenamed;
		ControlPointMetadata.CellValueChanged += OnControlPointCellValueChanged;
		NodeMetadata.CellValueChanged += OnNodeCellValueChanged;
		if (_arcTagDefinitions != null)
		{
			_arcTagDefinitions.SetAll(TagHelpers.BuildTagDefinitions(this, ArcMetadata), fullRefresh: true);
		}
		if (_controlPointTagDefinitions != null)
		{
			_controlPointTagDefinitions.SetAll(TagHelpers.BuildTagDefinitions(this, ControlPointMetadata), fullRefresh: true);
		}
		Arcs.DisposeAllTags();
		ControlPoints.DisposeAllTags();
		ControlPoints.RebuildInPlace();
		Nodes.RebuildInPlace();
		Arcs.RebuildInPlace();
	}
}
