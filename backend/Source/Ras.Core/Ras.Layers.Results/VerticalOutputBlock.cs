using System;
using System.Collections.Generic;
using Geospatial;
using Ras.Output;

namespace Ras.Layers.Results;

public class VerticalOutputBlock : IVerticalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	private PostProcessor _pp;

	private LegacyOutputBlock _ob;

	public string Name => _ob.Name;

	public IList<string> ProfileNames => _ob.ProfileNames;

	public Geometry Geometry => _ob.Geometry;

	public IReadOnlyList<DateTime> Times => _ob.Times;

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
		}
		remove
		{
		}
	}

	public VerticalOutputBlock(PostProcessor pp, LegacyOutputBlock ob)
	{
		_pp = pp;
		_ob = ob;
	}

	public bool TryReadMeshValues(OutputVariable variable, int meshIndex, IList<int> elementIndexes, Memory<float>[] values)
	{
		throw new NotImplementedException();
	}

	public bool TryReadMeshValues(OutputVariable variable, int meshIndex, Memory<int> elementIndexes, Memory<float>[] values)
	{
		throw new NotImplementedException();
	}
}
