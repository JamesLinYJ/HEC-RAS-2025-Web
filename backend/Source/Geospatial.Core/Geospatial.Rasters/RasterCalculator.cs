using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility;
using Utility.Logging;

namespace Geospatial.Rasters;

public class RasterCalculator
{
	private static readonly TimeSpan TimeMatchTolerance = TimeSpan.FromSeconds(1L);

	private readonly RasterCalculatorInput[] _inputs;

	private readonly Delegate _compiledExpression;

	private readonly Dictionary<string, float> _constants;

	private readonly IReadOnlyList<DateTime> _outputTimes;

	private readonly int[][] _profileMaps;

	public string Expression { get; }

	public bool HasDynamicInputs { get; }

	public int ProfileCount { get; }

	public string KeyFrameVariableName { get; }

	public Extent ValidExtent { get; }

	public IReadOnlyList<RasterCalculatorInput> Inputs => _inputs;

	public CalculatorNdvBehavior NoDataBehavior { get; set; } = CalculatorNdvBehavior.WriteNDV;

	public RasterCalculator(string expression, params RasterCalculatorInput[] inputs)
		: this(expression, inputs, null)
	{
	}

	public RasterCalculator(string expression, IEnumerable<RasterCalculatorInput> inputs, IEnumerable<KeyValuePair<string, float>> constants = null)
	{
		RasterCalculatorInput[] inputsArray = inputs.ToArray();
		_constants = BuildConstants(constants);
		if (inputsArray.Length == 0)
		{
			throw new ArgumentException("At least one input is required.", "inputs");
		}
		if (!TryValidateNames(inputsArray, out var error))
		{
			throw new ArgumentException(error, "inputs");
		}
		try
		{
			_compiledExpression = Compile(expression, inputsArray, _constants);
		}
		catch (Exception ex)
		{
			throw new ArgumentException("Failed to compile expression: " + ex.Message, "expression", ex);
		}
		if (!TryComputeExtent(inputsArray, out var extent))
		{
			throw new ArgumentException("Input extents do not overlap.", "inputs");
		}
		if (!TryBuildTemporalAlignment(inputsArray, null, out var profileCount, out var outputTimes, out var profileMaps, out var error2))
		{
			throw new ArgumentException(error2, "inputs");
		}
		Expression = expression;
		ValidExtent = extent;
		_inputs = inputsArray;
		ProfileCount = profileCount;
		HasDynamicInputs = outputTimes != null;
		_outputTimes = outputTimes;
		_profileMaps = profileMaps;
		KeyFrameVariableName = ResolveKeyFrameName(inputsArray, null);
	}

	private RasterCalculator(string expression, RasterCalculatorInput[] inputs, Dictionary<string, float> constants, Delegate compiled, Extent extent, int profileCount, IReadOnlyList<DateTime> outputTimes, int[][] profileMaps, string? keyFrameVariableName)
	{
		Expression = expression;
		_inputs = inputs;
		_constants = constants ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		_compiledExpression = compiled;
		ValidExtent = extent;
		ProfileCount = profileCount;
		HasDynamicInputs = outputTimes != null;
		_outputTimes = outputTimes;
		_profileMaps = profileMaps;
		KeyFrameVariableName = keyFrameVariableName;
	}

	public static OperationResult TryCreate(string expression, IEnumerable<RasterCalculatorInput> inputs, out RasterCalculator calculator)
	{
		return TryCreate(expression, inputs, null, null, out calculator);
	}

	public static OperationResult TryCreate(string expression, IEnumerable<RasterCalculatorInput> inputs, IEnumerable<KeyValuePair<string, float>> constants, out RasterCalculator calculator)
	{
		return TryCreate(expression, inputs, constants, null, out calculator);
	}

	public static OperationResult TryCreate(string expression, IEnumerable<RasterCalculatorInput> inputs, IEnumerable<KeyValuePair<string, float>> constants, string? keyFrameVariableName, out RasterCalculator calculator)
	{
		calculator = null;
		RasterCalculatorInput[] inputsArray = inputs.ToArray();
		Dictionary<string, float> constants2 = BuildConstants(constants);
		if (inputsArray.Length == 0)
		{
			return OperationResult.Fail("At least one input is required.");
		}
		if (!TryValidateNames(inputsArray, out var error))
		{
			return OperationResult.Fail(error);
		}
		Delegate compiled;
		try
		{
			compiled = Compile(expression, inputsArray, constants2);
		}
		catch (Exception ex)
		{
			return OperationResult.Fail("Failed to compile expression: " + ex.Message);
		}
		if (!TryComputeExtent(inputsArray, out var extent))
		{
			return OperationResult.Fail("Input extents do not overlap.");
		}
		if (!TryBuildTemporalAlignment(inputsArray, keyFrameVariableName, out var profileCount, out var outputTimes, out var profileMaps, out var error2))
		{
			return OperationResult.Fail(error2);
		}
		string? keyFrameVariableName2 = ResolveKeyFrameName(inputsArray, keyFrameVariableName);
		calculator = new RasterCalculator(expression, inputsArray, constants2, compiled, extent, profileCount, outputTimes, profileMaps, keyFrameVariableName2);
		return OperationResult.Success();
	}

	private static Dictionary<string, float> BuildConstants(IEnumerable<KeyValuePair<string, float>> constants)
	{
		Dictionary<string, float> ordinalIgnoreCase = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		if (constants != null)
		{
			foreach (KeyValuePair<string, float> constant in constants)
			{
				ordinalIgnoreCase[constant.Key] = constant.Value;
			}
		}
		return ordinalIgnoreCase;
	}

	private static bool TryValidateNames(RasterCalculatorInput[] inputs, out string error)
	{
		List<string> stringList = inputs.Select((RasterCalculatorInput i) => i.VariableName).ToList();
		if (stringList.Distinct(StringComparer.OrdinalIgnoreCase).Count() != stringList.Count)
		{
			error = "Duplicate variable names are not allowed.";
			return false;
		}
		error = null;
		return true;
	}

	private static Delegate Compile(string expression, RasterCalculatorInput[] inputs, Dictionary<string, float> constants)
	{
		string[] variableNames = inputs.Select((RasterCalculatorInput i) => i.VariableName).ToArray();
		return new ExpressionCompiler<float>(expression, variableNames, constants).CompileLambda();
	}

	private static bool TryComputeExtent(RasterCalculatorInput[] inputs, out Extent extent)
	{
		extent = inputs.Select((RasterCalculatorInput i) => i.ValidExtent).Aggregate((Extent a, Extent b) => a.Intersection(b));
		return extent.IsValid();
	}

	private static string ResolveKeyFrameName(RasterCalculatorInput[] inputs, string? keyFrameVariableName)
	{
		string error;
		return ResolveKeyFrame(inputs, keyFrameVariableName, out error)?.VariableName;
	}

	private static RasterCalculatorInput ResolveKeyFrame(RasterCalculatorInput[] inputs, string? keyFrameVariableName, out string error)
	{
		error = null;
		List<RasterCalculatorInput> rasterCalculatorInputs = inputs.Where((RasterCalculatorInput i) => i.IsDynamic).ToList();
		if (rasterCalculatorInputs.Count == 0)
		{
			return null;
		}
		if (string.IsNullOrWhiteSpace(keyFrameVariableName))
		{
			return rasterCalculatorInputs[0];
		}
		RasterCalculatorInput rasterCalculatorInput = rasterCalculatorInputs.FirstOrDefault((RasterCalculatorInput i) => string.Equals(i.VariableName, keyFrameVariableName, StringComparison.OrdinalIgnoreCase));
		if (rasterCalculatorInput == null)
		{
			error = "Key-frame input '" + keyFrameVariableName + "' is not a temporal (dynamic) input.";
			return null;
		}
		return rasterCalculatorInput;
	}

	private static bool TryBuildTemporalAlignment(RasterCalculatorInput[] inputs, string? keyFrameVariableName, out int profileCount, out IReadOnlyList<DateTime> outputTimes, out int[][] profileMaps, out string error)
	{
		profileCount = 1;
		outputTimes = null;
		profileMaps = null;
		RasterCalculatorInput rasterCalculatorInput = ResolveKeyFrame(inputs, keyFrameVariableName, out error);
		if (error != null)
		{
			return false;
		}
		if (rasterCalculatorInput == null)
		{
			return true;
		}
		if (!TryGetTimes(rasterCalculatorInput, out var times))
		{
			error = "Temporal input '" + rasterCalculatorInput.VariableName + "' does not expose timesteps.";
			return false;
		}
		profileCount = rasterCalculatorInput.DynamicInput.ProfileCount;
		if (times.Count != profileCount)
		{
			error = $"Temporal input '{rasterCalculatorInput.VariableName}' has {profileCount} profiles but {times.Count} timesteps.";
			return false;
		}
		outputTimes = times;
		profileMaps = new int[inputs.Length][];
		for (int i = 0; i < inputs.Length; i++)
		{
			RasterCalculatorInput rasterCalculatorInput2 = inputs[i];
			if (!rasterCalculatorInput2.IsDynamic)
			{
				profileMaps[i] = null;
			}
			else if (rasterCalculatorInput2 == rasterCalculatorInput)
			{
				int[] profileMapsArray = new int[profileCount];
				for (int j = 0; j < profileCount; j++)
				{
					profileMapsArray[j] = j;
				}
				profileMaps[i] = profileMapsArray;
			}
			else
			{
				if (!TryGetTimes(rasterCalculatorInput2, out var times2))
				{
					error = "Temporal input '" + rasterCalculatorInput2.VariableName + "' does not expose timesteps and cannot be aligned to the key frame.";
					return false;
				}
				profileMaps[i] = BuildTimeMatchMap(times, times2);
			}
		}
		return true;
	}

	private static bool TryGetTimes(RasterCalculatorInput input, out IReadOnlyList<DateTime> times)
	{
		if (input.DynamicInput is IDiscreteTimesteps { Times: not null } discreteTimesteps)
		{
			times = discreteTimesteps.Times;
			return true;
		}
		times = null;
		return false;
	}

	internal static int[] BuildTimeMatchMap(IReadOnlyList<DateTime> keyTimes, IReadOnlyList<DateTime> inputTimes)
	{
		int[] timeMatchMap = new int[keyTimes.Count];
		for (int i = 0; i < keyTimes.Count; i++)
		{
			DateTime dateTime = keyTimes[i];
			int timeMatchMap2 = -1;
			TimeSpan timeSpan = TimeMatchTolerance;
			for (int j = 0; j < inputTimes.Count; j++)
			{
				TimeSpan timeSpan2 = (inputTimes[j] - dateTime).Duration();
				if (timeSpan2 <= timeSpan)
				{
					timeSpan = timeSpan2;
					timeMatchMap2 = j;
				}
			}
			timeMatchMap[i] = timeMatchMap2;
		}
		return timeMatchMap;
	}

	public IResample<float> AsStaticResampler()
	{
		if (HasDynamicInputs)
		{
			throw new InvalidOperationException("Cannot create static resampler when dynamic inputs are present. Use AsDynamicResampler() instead.");
		}
		return new CalculatorStaticResampler(ValidExtent, _inputs, _compiledExpression, NoDataBehavior);
	}

	public IMultiBandResampler<float> AsDynamicResampler()
	{
		return new CalculatorDynamicResampler(ProfileCount, ValidExtent, _inputs, _compiledExpression, _outputTimes, _profileMaps, NoDataBehavior);
	}

	public ISurface<float> AsResampler()
	{
		if (HasDynamicInputs)
		{
			return AsDynamicResampler();
		}
		return AsStaticResampler();
	}
}
