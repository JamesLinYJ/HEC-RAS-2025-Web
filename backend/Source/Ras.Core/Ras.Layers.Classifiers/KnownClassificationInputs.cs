using System;

namespace Ras.Layers.Classifiers;

public static class KnownClassificationInputs
{
	public static void TypeSwitch(IClassificationInput input, Action<RasterClassificationInput> isRaster, Action<PolygonClassificationInput> isPolygon, Action<ConstantClassificationInput> isConstant, Action typeNotFound)
	{
		if (input is RasterClassificationInput rasterClassificationInput)
		{
			isRaster(rasterClassificationInput);
		}
		else if (input is PolygonClassificationInput polygonClassificationInput)
		{
			isPolygon(polygonClassificationInput);
		}
		else if (input is ConstantClassificationInput constantClassificationInput)
		{
			isConstant(constantClassificationInput);
		}
		else
		{
			typeNotFound();
		}
	}

	public static T TypeSwitch<T>(IClassificationInput input, Func<RasterClassificationInput, T> isRaster, Func<PolygonClassificationInput, T> isPolygon, Func<ConstantClassificationInput, T> isConstant, Func<T> typeNotFound)
	{
		if (input is RasterClassificationInput rasterClassificationInput)
		{
			return isRaster(rasterClassificationInput);
		}
		if (input is PolygonClassificationInput polygonClassificationInput)
		{
			return isPolygon(polygonClassificationInput);
		}
		if (input is ConstantClassificationInput constantClassificationInput)
		{
			return isConstant(constantClassificationInput);
		}
		return typeNotFound();
	}
}
