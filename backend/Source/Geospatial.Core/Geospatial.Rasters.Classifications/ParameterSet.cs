using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rasters.Classifications;

public class ParameterSet : IList<Parameter>, ICollection<Parameter>, IEnumerable<Parameter>, IEnumerable
{
	public List<Parameter> Parameters { get; private set; }

	public int Count => Parameters.Count;

	public bool IsReadOnly => false;

	public Parameter this[int index]
	{
		get
		{
			return Parameters[index];
		}
		set
		{
			Parameters[index] = value;
		}
	}

	public ParameterSet()
	{
		Parameters = new List<Parameter>();
	}

	public ParameterSet(IList<Parameter> parameters)
	{
		Parameters = parameters.ToList();
	}

	public void Add(Parameter p)
	{
		Parameters.Add(p);
	}

	public static ParameterSet Merge(ParameterSet higherPriority, ParameterSet lowerPriority)
	{
		ParameterSet parameterSet = new ParameterSet();
		for (int i = 0; i < higherPriority.Count; i++)
		{
			Parameter higher = higherPriority[i];
			Parameter parameter = lowerPriority.FirstOrDefault((Parameter cvL) => Classification.NameMatches(higher.VariableName, cvL.VariableName));
			if (parameter == null)
			{
				parameterSet.Add(higher.Clone());
			}
			else
			{
				parameterSet.Add(parameter.OverrideWith(higher));
			}
		}
		for (int intervalIndex = 0; intervalIndex < lowerPriority.Count; intervalIndex++)
		{
			Parameter lower = lowerPriority[intervalIndex];
			if (higherPriority.FirstOrDefault((Parameter cvH) => Classification.NameMatches(lower.VariableName, cvH.VariableName)) == null)
			{
				parameterSet.Add(lower.Clone());
			}
		}
		return parameterSet;
	}

	public ParameterSet MergeWith(ParameterSet higherPriority)
	{
		return Merge(higherPriority, this);
	}

	public static ParameterSet Override(ParameterSet higherPriority, ParameterSet lowerPriority)
	{
		ParameterSet parameterSet = new ParameterSet();
		for (int i = 0; i < lowerPriority.Count; i++)
		{
			Parameter lower = lowerPriority[i];
			Parameter parameter = higherPriority.FirstOrDefault((Parameter cvH) => Classification.NameMatches(lower.VariableName, cvH.VariableName));
			if (parameter == null)
			{
				parameterSet.Add(lower.Clone());
			}
			else
			{
				parameterSet.Add(lower.OverrideWith(parameter));
			}
		}
		return parameterSet;
	}

	public ParameterSet OverrideWith(ParameterSet higherPriority)
	{
		return Override(higherPriority, this);
	}

	public ParameterSet GetSanitized()
	{
		ParameterSet parameterSet = new ParameterSet();
		parameterSet.Parameters.AddRange(Parameters.Select((Parameter bv) =>
		{
			Parameter parameter = bv.Clone();
			parameter.StripNoData();
			return parameter;
		}));
		return parameterSet;
	}

	public int IndexOf(Parameter parameter)
	{
		return ((IList<Parameter>)Parameters).IndexOf(parameter);
	}

	public void Insert(int index, Parameter parameter)
	{
		((IList<Parameter>)Parameters).Insert(index, parameter);
	}

	public void RemoveAt(int index)
	{
		((IList<Parameter>)Parameters).RemoveAt(index);
	}

	public void Clear()
	{
		((ICollection<Parameter>)Parameters).Clear();
	}

	public bool Contains(Parameter parameter)
	{
		return ((ICollection<Parameter>)Parameters).Contains(parameter);
	}

	public void CopyTo(Parameter[] parameters, int arrayIndex)
	{
		((ICollection<Parameter>)Parameters).CopyTo(parameters, arrayIndex);
	}

	public bool Remove(Parameter parameter)
	{
		return ((ICollection<Parameter>)Parameters).Remove(parameter);
	}

	public IEnumerator<Parameter> GetEnumerator()
	{
		return ((IEnumerable<Parameter>)Parameters).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)Parameters).GetEnumerator();
	}
}
