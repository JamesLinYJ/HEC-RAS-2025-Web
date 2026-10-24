using System.Collections;
using System.Collections.Generic;
using Ras.Layers.BoundaryConditions;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public class BCLinesWrapper : IReadOnlyList<BCLineRow>, IEnumerable<BCLineRow>, IEnumerable, IReadOnlyCollection<BCLineRow>
{
	private readonly BoundaryConditionLineLayer _layer;

	public string Name => "BC Lines";

	public BCLineRow this[int index]
	{
		get
		{
			if (_layer == null || index < 0 || index >= _layer.Count)
			{
				return default;
			}
			return BCLineRow.FromBCLine(_layer[index]);
		}
	}

	public int Count => _layer?.Count ?? 0;

	public BCLinesWrapper(BoundaryConditionLineLayer layer)
	{
		_layer = layer;
	}

	public IEnumerator<BCLineRow> GetEnumerator()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return this[i];
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
