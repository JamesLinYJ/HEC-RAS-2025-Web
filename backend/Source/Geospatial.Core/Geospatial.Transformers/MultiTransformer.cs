using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.Extensions;
using Utility.Memory;

namespace Geospatial.Transformers;

public class MultiTransformer : ISpatialTransformer
{
	private List<ISpatialTransformer> _steps;

	public bool IsReversible => _steps.All((ISpatialTransformer step) => step.IsReversible);

	public MultiTransformer(IList<ISpatialTransformer> steps)
	{
		if (steps == null)
		{
			throw new ArgumentNullException("steps");
		}
		_steps = steps.ToList();
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		TransformInternal(points, transformed, _steps);
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		List<ISpatialTransformer> cloneReversed = _steps.CloneReversed();
		for (int i = 0; i < cloneReversed.Count; i++)
		{
			cloneReversed[i] = cloneReversed[i].GetInvertedTransformer();
		}
		TransformInternal(points, transformed, cloneReversed);
	}

	private static void TransformInternal(Memory<Point> from, Memory<Point> to, IList<ISpatialTransformer> steps)
	{
		int count = steps.Count;
		if (count == 0)
		{
			from.CopyTo(to);
			return;
		}
		steps[0].Transform(from, to);
		if (count == 1)
		{
			return;
		}
		Memory<Point> mem;
		using (SharedMemoryPool<Point>.Rent(from.Length, out mem))
		{
			for (int i = 1; i < count; i++)
			{
				if (i % 2 == 0)
				{
					steps[i].Transform(mem, to);
				}
				else
				{
					steps[i].Transform(to, mem);
				}
			}
			if (count % 2 == 0)
			{
				mem.CopyTo(to);
			}
		}
	}
}
