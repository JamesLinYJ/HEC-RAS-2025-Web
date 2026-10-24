using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Utility.DataStructures;

public class TranslatedList<Tin, Tout> : IReadOnlyList<Tout>, IEnumerable<Tout>, IEnumerable, IReadOnlyCollection<Tout>
{
	private readonly Func<IReadOnlyList<Tin>> _getInput;

	private readonly IReadOnlyList<Tin> _input;

	private readonly Func<Tin, Tout> _selector;

	public IReadOnlyList<Tin> Input => _input ?? _getInput();

	public Tout this[int index] => _selector(Input[index]);

	public int Count => Input.Count;

	public TranslatedList(IEnumerable<Tin> input, Func<Tin, Tout> selector)
	{
		if (input is IReadOnlyList<Tin> input2)
		{
			_input = input2;
		}
		else
		{
			_input = input.ToList().AsReadOnly();
		}
		_selector = selector;
	}

	public TranslatedList(IList<Tin> input, Func<Tin, Tout> selector)
	{
		_input = input.AsReadOnly();
		_selector = selector;
	}

	public TranslatedList(Func<IReadOnlyList<Tin>> getInput, Func<Tin, Tout> selector)
	{
		_getInput = getInput;
		_selector = selector;
	}

	public IEnumerator<Tout> GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumeratorInternal();
	}

	private IEnumerator<Tout> GetEnumeratorInternal()
	{
		for (int i = 0; i < Count; i++)
		{
			yield return this[i];
		}
	}
}
