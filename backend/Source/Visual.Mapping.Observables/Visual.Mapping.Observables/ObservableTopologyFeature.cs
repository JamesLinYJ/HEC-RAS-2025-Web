using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public abstract class ObservableTopologyFeature : ObservableObject
{
	private int _fid;

	private IEnumerable<ObservableTagDefinition> _tagsToBuildAgainst;

	private AdvancedCollection<ObservableTag> _tags;

	protected TableRow _tableRow;

	protected ObservableTable _table;

	public Dictionary<string, string> PropertyNameColumnNameMap => NotifyPropertyChangedForTableColumnAttribute.GetMapForType(GetType());

	public int Fid
	{
		get
		{
			return _fid;
		}
		protected set
		{
			SetProperty(ref _fid, value, "Fid");
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<Message> ErrorsOnLatestTopologyComplete
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ObservableCollection<Message>>.Default.Equals(field, value))
			{
				OnPropertyChanging("ErrorsOnLatestTopologyComplete");
				field = value;
				OnPropertyChanged("ErrorsOnLatestTopologyComplete");
			}
		}
	} = new ObservableCollection<Message>();

	public IReadOnlyAdvancedCollection<ObservableTag> Tags => _tags;

	public TableRow TableRow => _tableRow;

	public virtual ObservableBaseTopology Topology { get; }

	protected void BuildTags(IEnumerable<ObservableTagDefinition> definitions)
	{
		_tagsToBuildAgainst = definitions;
		AdvancedCollection<ObservableTag> advancedCollection = new AdvancedCollection<ObservableTag>();
		if (definitions == null)
		{
			_tags = advancedCollection;
			return;
		}
		TableRow row = TableRow;
		foreach (ObservableTagDefinition tagDef in definitions)
		{
			object rowTagDefTerm = row.Value(tagDef.Key);
			if (!tagDef.IsDefaultValue(rowTagDefTerm))
			{
				ObservableTag observableTag = new ObservableTag(this, tagDef, () => row.Value(tagDef.Key));
				observableTag.RefreshValue();
				advancedCollection.Add(observableTag);
			}
		}
		_tags = advancedCollection;
	}

	public void AddTag(ObservableTag tag)
	{
		if (!_tags.Where((ObservableTag other) => tag.Definition.Equals(other.Definition)).Any())
		{
			_tags.Add(tag);
		}
	}

	public void RemoveTag(ObservableTag tag)
	{
		_tags.Remove(tag);
		tag.Dispose();
		_table.SetCellValue(Fid, tag.Definition.Key, null);
	}

	public void ForceRemoveTag(string columnName)
	{
		ObservableTag observableTag = _tags?.FirstOrDefault((ObservableTag t) => t.Definition.Key.RoughlyEquals(columnName));
		if (observableTag != null)
		{
			_tags.Remove(observableTag);
			observableTag.Dispose();
		}
	}

	internal void DisposeTags()
	{
		DisposeTagCollection(_tags);
	}

	protected static void DisposeTagCollection(AdvancedCollection<ObservableTag> tags)
	{
		if (tags == null)
		{
			return;
		}
		foreach (ObservableTag tag in tags)
		{
			tag?.Dispose();
		}
		tags.Clear();
	}

	public ObservableTopologyFeature(ObservableBaseTopology topo, int fid, ObservableTable table)
	{
		Topology = topo;
		Fid = fid;
		_table = table;
		_tableRow = table.Rows[fid];
	}

	internal void HandleCellValueChanged(int colIdx, object localValue2)
	{
		if (_table == null || _table.Rows.Count <= Fid)
		{
			return;
		}
		_tableRow = _table.Rows[Fid];
		string colName = _table.Columns[colIdx].Name;
		if (PropertyNameColumnNameMap.TryGetValue(colName, out var localValue))
		{
			OnPropertyChanged(localValue);
		}
		ObservableTag observableTag = Tags.Where((ObservableTag tag) => tag.Definition.Key.RoughlyEquals(colName)).FirstOrDefault();
		if (observableTag != null)
		{
			if (observableTag.Definition.IsDefaultValue(localValue2))
			{
				RemoveTag(observableTag);
			}
			else
			{
				observableTag.RefreshValue();
			}
		}
		else
		{
			if (localValue2 == null)
			{
				return;
			}
			ObservableTagDefinition tagDef = _tagsToBuildAgainst.Where((ObservableTagDefinition observableTagDefinition) => observableTagDefinition.Key.RoughlyEquals(colName)).FirstOrDefault();
			if (tagDef != null)
			{
				AddTag(new ObservableTag(this, tagDef, () => _tableRow.Value(tagDef.Key)));
			}
		}
	}

	public void UpdateFid(int fid)
	{
		Fid = fid;
	}

	public void IncrementFid()
	{
		Fid++;
	}

	public void DecrementFid()
	{
		Fid--;
	}
}
