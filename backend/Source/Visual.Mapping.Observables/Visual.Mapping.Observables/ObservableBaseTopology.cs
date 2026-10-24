using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors.Topology;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Logging;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public abstract class ObservableBaseTopology : ObservableObject
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableBaseTopology");

	private BaseTopology _topology;

	protected readonly object _loadLock = new object();

	protected AdvancedCollection<ObservableTagDefinition> _arcTagDefinitions;

	public virtual BaseTopology Topology => _topology;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public OperationResult LatestBuildResult
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<OperationResult>.Default.Equals(field, value))
			{
				OnPropertyChanging("LatestBuildResult");
				field = value;
				OnPropertyChanged("LatestBuildResult");
			}
		}
	}

	public abstract IObservableFeatureSet<ObservableBaseArc> Arcs { get; }

	public ObservableFeatureSet<ObservableNode> Nodes { get; }

	public IReadOnlyAdvancedCollection<ObservableTagDefinition> ArcTagDefinitions => LazyThreadsafe<AdvancedCollection<ObservableTagDefinition>>.AllocateOnce(ref _arcTagDefinitions, _loadLock, this, (ObservableBaseTopology self) => TagHelpers.BuildTagDefinitions(self, self.ArcMetadata));

	public ObservableTable ArcMetadata { get; protected set; }

	public ObservableTable NodeMetadata { get; protected set; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<ObservableTag> RecentArcTags
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<ObservableTag>>.Default.Equals(field, value))
			{
				OnPropertyChanging("RecentArcTags");
				field = value;
				OnPropertyChanged("RecentArcTags");
			}
		}
	} = new AdvancedCollection<ObservableTag>();

	public ObservableBaseTopology(BaseTopology topology)
	{
		_topology = topology;
		Nodes = new ObservableFeatureSet<ObservableNode>(() => Topology.Nodes.Count, (int fid) => new ObservableNode(this, fid));
		ArcMetadata = new ObservableTable(topology.ArcMetadata);
		NodeMetadata = new ObservableTable(topology.NodeMetadata);
		ArcMetadata.ColumnAdded += ArcMetadataColumnAdded;
		ArcMetadata.ColumnRemoved += ArcMetadataColumnRemoved;
		ArcMetadata.ColumnRenamed += ArcMetadataColumnRenamed;
		ArcMetadata.CellValueChanged += OnArcCellValueChanged;
		NodeMetadata.CellValueChanged += OnNodeCellValueChanged;
	}

	public abstract OperationResult Complete(TopologyBuilderParams builderParams = null);

	public virtual bool IsReservedColumn(string columnName)
	{
		return false;
	}

	protected void ArcMetadataColumnAdded(ObservableTable sender, int idx)
	{
		_ = ArcTagDefinitions;
		string colName = sender.Columns[idx].Name;
		if (!_arcTagDefinitions.Where((ObservableTagDefinition tagDef) => tagDef.Key.RoughlyEquals(colName)).Any() && TagHelpers.TryBuildTagDefinition(this, sender, idx, out var created))
		{
			_arcTagDefinitions.Add(created);
		}
	}

	protected void ArcMetadataColumnRemoved(ObservableTable sender, int removedIdx, string columnName)
	{
		if (_arcTagDefinitions == null)
		{
			return;
		}
		ObservableTagDefinition observableTagDefinition = _arcTagDefinitions.FirstOrDefault((ObservableTagDefinition td) => td.Key.RoughlyEquals(columnName));
		if (observableTagDefinition == null)
		{
			return;
		}
		foreach (ObservableTopologyFeature featuresWithTag in observableTagDefinition.FeaturesWithTag.ToList())
		{
			featuresWithTag.ForceRemoveTag(columnName);
		}
		_arcTagDefinitions.Remove(observableTagDefinition);
	}

	protected void ArcMetadataColumnRenamed(ObservableTable sender, int colIdx, string oldName, string newName)
	{
		if (_arcTagDefinitions != null)
		{
			ObservableTagDefinition observableTagDefinition = _arcTagDefinitions.FirstOrDefault((ObservableTagDefinition td) => td.Key.RoughlyEquals(oldName));
			if (observableTagDefinition != null)
			{
				observableTagDefinition.Key = newName;
			}
		}
	}

	protected void OnArcCellValueChanged(ObservableTable sender, int rowIdx, int colIdx, object localValue)
	{
		Arcs.HandleCellValueChanged(rowIdx, colIdx, localValue);
	}

	protected void OnNodeCellValueChanged(ObservableTable sender, int rowIdx, int colIdx, object localValue)
	{
		Nodes.HandleCellValueChanged(rowIdx, colIdx, localValue);
	}

	public void AlertNewArc(int fid)
	{
		Arcs.AlertNew(fid);
	}

	public void AlertArcInserted(int index)
	{
		Arcs.AlertInserted(index);
	}

	public void AlertArcAtDeleted(int index)
	{
		Arcs.AlertAtDeleted(index);
	}

	public void AlertNewNode(int fid)
	{
		Nodes.AlertNew(fid);
	}

	public void AlertNodeInserted(int index)
	{
		Nodes.AlertInserted(index);
	}

	public void AlertNodeDeleted(ObservableNode removedNode)
	{
		Nodes.AlertDeleted(removedNode);
	}

	public void AlertNodeAtDeleted(int index)
	{
		Nodes.AlertAtDeleted(index);
	}

	public ObservableTagDefinition CreateNewArcTag(string tagName, TagType tagType)
	{
		return CreateNewTag(ArcMetadata, ArcTagDefinitions, tagName, tagType);
	}

	protected static ObservableTagDefinition CreateNewTag(ObservableTable table, IReadOnlyAdvancedCollection<ObservableTagDefinition> tagDefs, string tagName, TagType tagType)
	{
		table.TryEnsureColumn(tagName, tagType.AsType(), out var _);
		return tagDefs.Where((ObservableTagDefinition def) => def.Key.RoughlyEquals(tagName)).FirstOrDefault();
	}

	public void DebugAssertModelTopologyEquivalentToObservableTopology()
	{
		BaseTopology topology = Topology;
		foreach (ObservableBaseArc arc in Arcs)
		{
			int fid = arc.Fid;
			int indexOf = topology.Arcs.IndexOf(arc.Arc);
			int indexOf2 = Arcs.IndexOf(arc);
			bool fidIndexOfTerm = fid != indexOf;
			bool fidIndexOf2Term = fid != indexOf2;
			if (fidIndexOfTerm | fidIndexOf2Term)
			{
				_logger.LogError("ObservableTopology arcs are MISMATCHED with model layer arcs.");
			}
		}
		foreach (ObservableNode node in Nodes)
		{
			int fid2 = node.Fid;
			int indexOf3 = topology.Nodes.IndexOf(node.Node);
			int indexOf4 = Nodes.IndexOf(node);
			bool fid2IndexOf3Term = fid2 != indexOf3;
			bool fid2IndexOf4Term = fid2 != indexOf4;
			if (fid2IndexOf3Term | fid2IndexOf4Term)
			{
				_logger.LogError("ObservableTopology nodes are MISMATCHED with model layer nodes.");
			}
		}
	}
}
