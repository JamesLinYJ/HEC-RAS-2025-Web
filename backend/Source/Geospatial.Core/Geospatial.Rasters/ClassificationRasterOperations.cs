using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;

namespace Geospatial.Rasters;

public static class ClassificationRasterOperations
{
	public static Dictionary<int, double> SampleClassifications(IResample<int> resampler, Polygon polygon, RasterDefinition rd)
	{
		int[] data = resampler.Resample(new ResampleArgs(rd));
		Dictionary<int, int> dic = new Dictionary<int, int>();
		new PolygonRasterizer(rd).Rasterize(polygon, (int px) =>
		{
			int key = data[px];
			dic.TryGetValue(key, out var localValue);
			dic[key] = localValue + 1;
		});
		int sum = dic.Values.Sum();
		Dictionary<int, double> doubleByInt = new Dictionary<int, double>();
		foreach (KeyValuePair<int, int> dic2 in dic)
		{
			doubleByInt[dic2.Key] = (double)dic2.Value / (double)sum;
		}
		return doubleByInt;
	}
}
