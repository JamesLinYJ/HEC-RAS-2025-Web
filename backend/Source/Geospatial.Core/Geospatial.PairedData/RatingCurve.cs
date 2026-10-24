using System;
using System.Collections.Generic;
using Utility.Hashing;

namespace Geospatial.PairedData;

public class RatingCurve : IDataHash
{
	private List<double> _input;

	private List<double> _output;

	public RatingCurve()
	{
		_input = new List<double>();
		_output = new List<double>();
	}

	public void Add(double x, double y)
	{
		_input.Add(x);
		_output.Add(y);
	}

	public int GetDataHash()
	{
		throw new NotImplementedException();
	}
}
