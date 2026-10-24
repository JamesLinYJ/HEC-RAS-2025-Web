using System;
using System.Reflection;
using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class TopologyStep
{
	public virtual string name => null;

	public static TopologyStep NullStep => new NullStep();

	public virtual bool IsNull()
	{
		return false;
	}

	public virtual bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		return true;
	}

	public static bool TrySetParameter(Type myType, ReadOnlyMemory<char> key, ReadOnlyMemory<char> charSpan, TopologyStep step = null)
	{
		FieldInfo field = myType.GetField(key.ToString(), BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
		if (field == null)
		{
			return false;
		}
		if (field.FieldType == typeof(double))
		{
			if (!double.TryParse(charSpan.Span, out var result))
			{
				return false;
			}
			if (step != null)
			{
				field.SetValue(step, result);
			}
		}
		else if (field.FieldType == typeof(int))
		{
			if (!int.TryParse(charSpan.Span, out var result2))
			{
				return false;
			}
			if (step != null)
			{
				field.SetValue(step, result2);
			}
		}
		else
		{
			if (!(field.FieldType == typeof(bool)))
			{
				throw new NotImplementedException();
			}
			ReadOnlySpan<char> charSpan2 = charSpan.Span;
			bool condition;
			if (MemoryExtensions.Equals(charSpan2, "true", StringComparison.OrdinalIgnoreCase) || MemoryExtensions.Equals(charSpan2, "t", StringComparison.OrdinalIgnoreCase) || MemoryExtensions.Equals(charSpan2, "1", StringComparison.OrdinalIgnoreCase) || MemoryExtensions.Equals(charSpan2, "yes", StringComparison.OrdinalIgnoreCase))
			{
				condition = true;
			}
			else
			{
				if (!MemoryExtensions.Equals(charSpan2, "false", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan2, "f", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan2, "0", StringComparison.OrdinalIgnoreCase) && !MemoryExtensions.Equals(charSpan2, "no", StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				condition = false;
			}
			if (step != null)
			{
				field.SetValue(step, condition);
			}
		}
		return true;
	}
}
