using System;
using System.Threading.Tasks;

namespace Utility;

public class WeightedTask : Task
{
	private readonly int _weight;

	public int Weight => _weight;

	public WeightedTask(Action action, int weight)
		: base(action)
	{
		if (weight < 1)
		{
			throw new ArgumentOutOfRangeException("weight");
		}
		_weight = weight;
	}
}
