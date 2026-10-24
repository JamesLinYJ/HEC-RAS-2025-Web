using System;
using Utility.Hashing;

namespace Ras.Layers.Payloads;

public class RatingCurvePayload : PayloadBase
{
	[HashContributor]
	public Memory<double> InputValues { get; set; }

	[HashContributor]
	public string IndependentVariable { get; set; }

	[HashContributor]
	public Memory<double> OutputValues { get; set; }

	public int Count => InputValues.Length;

	public RatingCurvePayload(string inputVariable, string outputVariable, Memory<double> inputValues, Memory<double> outputValues)
		: base(outputVariable)
	{
		IndependentVariable = inputVariable;
		InputValues = inputValues;
		OutputValues = outputValues;
	}
}
