using System;
using Utility.DataStructures;
using Utility.Memory;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public static class TagHelpers
{
	public static AdvancedCollection<ObservableTagDefinition> BuildTagDefinitions(ObservableBaseTopology owner, ObservableTable table)
	{
		AdvancedCollection<ObservableTagDefinition> advancedCollection = new AdvancedCollection<ObservableTagDefinition>();
		for (int i = 0; i < table.Columns.Count; i++)
		{
			if (TryBuildTagDefinition(owner, table, i, out var created))
			{
				advancedCollection.Add(created);
			}
		}
		return advancedCollection;
	}

	public static bool TryBuildTagDefinition(ObservableBaseTopology owner, ObservableTable table, int colIdx, out ObservableTagDefinition created)
	{
		TableColumn tableColumn = table.Columns[colIdx];
		Type type = tableColumn.Type;
		string name = tableColumn.Name;
		if (owner.IsReservedColumn(name))
		{
			created = null;
			return false;
		}
		created = new ObservableTagDefinition(TagTypeHelper.AsTagType(type), name);
		return true;
	}
}
