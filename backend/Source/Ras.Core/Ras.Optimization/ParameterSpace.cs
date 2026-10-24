using System;
using System.Collections.Generic;
using System.Linq;

namespace Ras.Optimization;

public sealed class ParameterSpace
{
	private readonly IReadOnlyList<ICalibrationParameter> _parameters;

	public IReadOnlyList<ICalibrationParameter> Parameters => _parameters;

	public int Dimension => _parameters.Count;

	public double[] Min { get; }

	public double[] Max { get; }

	public double[] Initial { get; }

	public ParameterScale[] Scales { get; }

	public bool AffectsManning { get; }

	public ParameterSpace(IReadOnlyList<ICalibrationParameter> parameters)
	{
		if (parameters == null || parameters.Count == 0)
		{
			throw new ArgumentException("At least one parameter is required.", "parameters");
		}
		ValidateOrderingAndNames(parameters);
		_parameters = parameters;
		Min = parameters.Select((ICalibrationParameter p) => p.Min).ToArray();
		Max = parameters.Select((ICalibrationParameter p) => p.Max).ToArray();
		Initial = parameters.Select((ICalibrationParameter p) => p.Initial).ToArray();
		Scales = parameters.Select((ICalibrationParameter p) => p.Scale).ToArray();
		AffectsManning = parameters.Any((ICalibrationParameter p) => p.AffectsManning);
	}

	private static void ValidateOrderingAndNames(IReadOnlyList<ICalibrationParameter> parameters)
	{
		HashSet<string> stringSet = new HashSet<string>();
		HashSet<string> stringSetSet = new HashSet<string>();
		for (int i = 0; i < parameters.Count; i++)
		{
			ICalibrationParameter calibrationParameter = parameters[i];
			if (calibrationParameter == null)
			{
				throw new ArgumentException($"Parameter {i} is null.", "parameters");
			}
			if (!stringSet.Add(calibrationParameter.Name))
			{
				throw new ArgumentException("Duplicate parameter name '" + calibrationParameter.Name + "'.", "parameters");
			}
			if (calibrationParameter.Scale == ParameterScale.Log && calibrationParameter.Min <= 0.0)
			{
				throw new ArgumentException($"Parameter '{calibrationParameter.Name}' is log-scaled but Min ({calibrationParameter.Min}) is not positive.", "parameters");
			}
			foreach (string requiresKey in calibrationParameter.RequiresKeys)
			{
				if (!stringSetSet.Contains(requiresKey))
				{
					throw new ArgumentException($"Parameter '{calibrationParameter.Name}' requires '{requiresKey}', which no earlier parameter produces. " + "Reorder the list so the producing parameter comes first.", "parameters");
				}
			}
			foreach (string producesKey in calibrationParameter.ProducesKeys)
			{
				stringSetSet.Add(producesKey);
			}
		}
	}

	public void Apply(double[] physical, CalibrationContext ctx)
	{
		if (physical == null)
		{
			throw new ArgumentNullException("physical");
		}
		if (physical.Length != Dimension)
		{
			throw new ArgumentException($"Vector length {physical.Length} does not match dimension {Dimension}.");
		}
		for (int i = 0; i < _parameters.Count; i++)
		{
			_parameters[i].Apply(physical[i], ctx);
		}
	}
}
