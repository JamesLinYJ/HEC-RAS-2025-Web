using System;
using Utility;
using Utility.DataStructures;

namespace Geospatial.Rasters;

public class Translator<TIn, TOut>
{
	private readonly Action<IArray<TIn>, IArray<TOut>> _translateMass;

	public Translator(Action<IArray<TIn>, IArray<TOut>> translate)
	{
		_translateMass = translate;
	}

	public Translator(Func<TIn, TOut> translate)
	{
		_translateMass = (IArray<TIn> arrIn, IArray<TOut> arrOut) =>
		{
			for (int i = 0; i < arrIn.Count; i++)
			{
				arrOut[i] = translate(arrIn[i]);
			}
		};
	}

	public TOut Translate(TIn tIn)
	{
		TIn[] lst = new TIn[1] { tIn };
		TOut[] tOuts = new TOut[1];
		_translateMass(lst.Wrapped(), tOuts.Wrapped());
		return tOuts[0];
	}

	public void Translate(TIn[] inValues, ref TOut[] outValues)
	{
		Buffers.EnsureArraySize(ref outValues, inValues.Length);
		_translateMass(inValues.Wrapped(), outValues.Wrapped());
	}

	public void Translate(TIn[,] inValues, ref TOut[,] outValues)
	{
		Buffers.EnsureArraySize(ref outValues, inValues.GetLength(0), inValues.GetLength(1));
		_translateMass(inValues.Wrapped(), outValues.Wrapped());
	}
}
