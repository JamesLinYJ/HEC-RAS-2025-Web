using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class RasterCalculatorInput
{
	public string VariableName { get; }

	public IResample<float> StaticInput { get; }

	public IMultiBandResampler<float> DynamicInput { get; }

	public bool IsDynamic => DynamicInput != null;

	public Extent ValidExtent
	{
		get
		{
			if (!IsDynamic)
			{
				return StaticInput.ValidExtent;
			}
			return DynamicInput.ValidExtent;
		}
	}

	public float? NoDataValue
	{
		get
		{
			if (!IsDynamic)
			{
				return StaticInput.NoDataValue;
			}
			return DynamicInput.NoDataValue;
		}
	}

	private RasterCalculatorInput(string variableName, IResample<float> staticInput, IMultiBandResampler<float> dynamicInput)
	{
		VariableName = variableName;
		StaticInput = staticInput;
		DynamicInput = dynamicInput;
	}

	public static RasterCalculatorInput FromStatic(string name, IResample<float> resampler)
	{
		ArgumentNullException.ThrowIfNull(name, "name");
		ArgumentNullException.ThrowIfNull(resampler, "resampler");
		return new RasterCalculatorInput(name, resampler, null);
	}

	public static RasterCalculatorInput FromDynamic(string name, IMultiBandResampler<float> resampler)
	{
		ArgumentNullException.ThrowIfNull(name, "name");
		ArgumentNullException.ThrowIfNull(resampler, "resampler");
		return new RasterCalculatorInput(name, null, resampler);
	}
}
