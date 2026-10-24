using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace Geospatial.Rasters.Classifications;

public class ParameterResampler : IMultiBandResampler<float>, ISurface<float>, IBlame
{
	private struct PriorityPolygon(Polygon poly, ParameterSet variables, IClassification classification)
	{
		public readonly Polygon Polygon = poly;

		public readonly ParameterSet Variables = variables;

		public readonly Lazy<Tin> DevTin = new Lazy<Tin>(() => poly.DevTin, isThreadSafe: true);

		public readonly Lazy<List<Dictionary<int, float>>> VariableIntMap = new Lazy<List<Dictionary<int, float>>>(() =>
		{
			List<Dictionary<int, float>> floatByIntList = new List<Dictionary<int, float>>(variables.Count);
			for (int i = 0; i < variables.Count; i++)
			{
				floatByIntList.Add(variables[i].GetIntegerLookup(classification));
			}
			return floatByIntList;
		}, isThreadSafe: true);
	}

	private readonly IClassificationResampler _baseRasterizer;

	private readonly ParameterSet _baseVariables;

	private readonly Lazy<List<Dictionary<int, float>>> _baseVariableIntMap;

	private readonly List<PriorityPolygon> _priorityPolygons;

	private int[,] _polyVarMapHL;

	private int[,] _polyVarMapLH;

	public Extent ValidExtent => _baseRasterizer.ValidExtent;

	public float? NoDataValue => -9999f;

	public int ProfileCount => _baseVariables.Count;

	public ParameterSet BaseVariables => _baseVariables;

	public List<string> ProfileNames => _baseVariables.Select((Parameter bv) => bv.VariableName).ToList();

	private IList<PriorityPolygon> PolygonsHighestToLowest => _priorityPolygons;

	private IList<PriorityPolygon> PolygonsLowestToHighest => _priorityPolygons.ReverseIndexing();

	public IClassification Classifier => _baseRasterizer.Classifier;

	public bool CanResampleInParallel => false;

	public ParameterResampler(IClassificationResampler baseRasterizer, Parameter baseVariable)
	{
		_baseRasterizer = baseRasterizer ?? throw new ArgumentNullException();
		if (baseVariable == null)
		{
			throw new ArgumentNullException();
		}
		Parameter parameter = baseVariable.Clone();
		parameter.StripNoData();
		_baseVariables = new ParameterSet { parameter };
		_baseVariableIntMap = MakeBaseVariableIntMap(_baseVariables, baseRasterizer.Classifier);
		_priorityPolygons = new List<PriorityPolygon>();
	}

	public ParameterResampler(IClassificationResampler baseRasterizer, ParameterSet baseVariables)
	{
		_baseRasterizer = baseRasterizer ?? throw new ArgumentNullException();
		if (baseVariables == null)
		{
			throw new ArgumentNullException("baseVariables");
		}
		_baseVariables = baseVariables.GetSanitized();
		_baseVariableIntMap = MakeBaseVariableIntMap(_baseVariables, baseRasterizer.Classifier);
		_priorityPolygons = new List<PriorityPolygon>();
	}

	public ParameterResampler(IClassificationResampler baseRasterizer, ParameterSet baseVariables, List<ParameterizedPolygon> polygonOverrides)
	{
		_baseRasterizer = baseRasterizer ?? throw new ArgumentNullException();
		if (baseVariables == null)
		{
			throw new ArgumentNullException("baseVariables");
		}
		_baseVariables = baseVariables.GetSanitized();
		_baseVariableIntMap = MakeBaseVariableIntMap(_baseVariables, baseRasterizer.Classifier);
		_priorityPolygons = new List<PriorityPolygon>();
		for (int i = 0; i < polygonOverrides.Count; i++)
		{
			ParameterizedPolygon parameterizedPolygon = polygonOverrides[i];
			_priorityPolygons.Add(new PriorityPolygon(parameterizedPolygon.Polygon, parameterizedPolygon.ParameterSet.GetSanitized(), baseRasterizer.Classifier));
		}
	}

	private static Lazy<List<Dictionary<int, float>>> MakeBaseVariableIntMap(ParameterSet variables, IClassification classification)
	{
		return new Lazy<List<Dictionary<int, float>>>(() =>
		{
			List<Dictionary<int, float>> floatByIntList = new List<Dictionary<int, float>>(variables.Count);
			for (int i = 0; i < variables.Count; i++)
			{
				floatByIntList.Add(variables[i].GetIntegerLookup(classification));
			}
			return floatByIntList;
		}, isThreadSafe: true);
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] result)
	{
		throw new NotImplementedException();
	}

	public float[,] SamplePoints(Memory<Point> points, out int[] classifications)
	{
		float[,] floatValues = new float[ProfileCount, points.Length];
		Span<Point> pointsSpan = points.Span;
		classifications = new int[points.Length];
		_baseRasterizer.SamplePoints(points, classifications);
		IList<PriorityPolygon> polygonsHighestToLowest = PolygonsHighestToLowest;
		int[,] polygonVariableMap = GetPolygonVariableMap(highToLow: true);
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point point = pointsSpan[i];
			int key = classifications[i];
			for (int j = 0; j < ProfileCount; j++)
			{
				bool condition = false;
				for (int k = 0; k < polygonsHighestToLowest.Count; k++)
				{
					int polygonVariableMapTerm = polygonVariableMap[j, k];
					if (polygonVariableMapTerm != -1)
					{
						PriorityPolygon priorityPolygon = polygonsHighestToLowest[k];
						if (priorityPolygon.VariableIntMap.Value[polygonVariableMapTerm].TryGetValue(key, out var localValue) && localValue != -9999f && priorityPolygon.DevTin.Value.ContainsPoint(point))
						{
							floatValues[j, i] = localValue;
							condition = true;
							break;
						}
					}
				}
				if (!condition)
				{
					if (_baseVariableIntMap.Value[j].TryGetValue(key, out var localValue2))
					{
						floatValues[j, i] = localValue2;
					}
					else
					{
						floatValues[j, i] = -9999f;
					}
				}
			}
		}
		return floatValues;
	}

	public void Blame(Point pt, ProgressReporter reporter)
	{
		reporter.ReportMessage("ParameterResampler: Querying " + pt.ToString());
		(_baseRasterizer as IBlame)?.Blame(pt, reporter.WithMessageModifier("   ", string.Empty));
		int samplePoint = _baseRasterizer.SamplePoint(pt);
		IList<PriorityPolygon> polygonsHighestToLowest = PolygonsHighestToLowest;
		int[,] polygonVariableMap = GetPolygonVariableMap(highToLow: true);
		reporter.ReportMessage("Identified classification: " + _baseRasterizer.Classifier.GetName(samplePoint).InSingleQuotes() + " " + samplePoint.ToString().InParentheses() + ", Processing Variables...");
		for (int i = 0; i < ProfileCount; i++)
		{
			reporter.ReportMessage("Processing " + _baseVariables[i].VariableName + "...");
			bool condition = false;
			for (int j = 0; j < polygonsHighestToLowest.Count; j++)
			{
				int polygonVariableMapTerm = polygonVariableMap[i, j];
				if (polygonVariableMapTerm == -1)
				{
					reporter.ReportMessage("Override Polygon" + j.ToString().InSquareBrackets() + " does not override this variable, skipping intersection check.");
					continue;
				}
				PriorityPolygon priorityPolygon = polygonsHighestToLowest[j];
				Dictionary<int, float> floatByInt = priorityPolygon.VariableIntMap.Value[polygonVariableMapTerm];
				if (priorityPolygon.DevTin.Value.ContainsPoint(pt))
				{
					if (floatByInt.TryGetValue(samplePoint, out var localValue) && localValue != -9999f)
					{
						reporter.ReportMessage("Override Polygon" + j.ToString().InSquareBrackets() + " contains this point: " + _baseVariables[i].VariableName + "=" + localValue);
						condition = true;
						break;
					}
					reporter.ReportMessage("Override Polygon" + j.ToString().InSquareBrackets() + " contains this point, but does not override this parameter. Continuing...");
				}
			}
			if (!condition)
			{
				reporter.ReportMessage("No override polygon found that has a value for this parameter.");
				if (_baseVariableIntMap.Value[i].TryGetValue(samplePoint, out var localValue2))
				{
					reporter.ReportMessage("Base Map: " + _baseVariables[i].VariableName + " = " + localValue2);
				}
				else
				{
					reporter.ReportMessage("Base Map does not override this variable either, returning NoData.");
				}
			}
		}
		reporter.ReportMessage(string.Empty);
		reporter.ReportMessage("* Please note that 'Base Map' could be a rollup/merging of several other Layers, but they've been merged for this sampler.");
	}

	public Dictionary<string, float[]> SamplePointsMapped(Memory<Point> points)
	{
		int[] classifications;
		return SamplePointsMapped(points, out classifications);
	}

	public Dictionary<string, float[]> SamplePointsMapped(Memory<Point> points, out int[] classifications)
	{
		float[,] src = SamplePoints(points, out classifications);
		Dictionary<string, float[]> floatValuesByString = new Dictionary<string, float[]>(ProfileCount, StringComparer.OrdinalIgnoreCase);
		List<string> profileNames = ProfileNames;
		int length = points.Length;
		for (int i = 0; i < ProfileCount; i++)
		{
			float[] floatValues = new float[length];
			Buffer.BlockCopy(src, i * length * 4, floatValues, 0, length * 4);
			floatValuesByString[profileNames[i]] = floatValues;
		}
		return floatValuesByString;
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<float> result)
	{
		float[,] samplePoints = SamplePoints(points, out var _);
		Span<float> floatSpan = result.Span;
		for (int i = 0; i < points.Length; i++)
		{
			floatSpan[i] = samplePoints[profileIndex, i];
		}
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<float> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		int cellCount = args.Raster.CellCount;
		int[] classes = new int[cellCount];
		_baseRasterizer.Resample(args, classes);
		Dictionary<int, float> baseLookup = _baseVariableIntMap.Value[profileIndex];
		Parallel.SmartFor(cellCount, (int start, int end) =>
		{
			Span<float> outDataSpan = outData.Span;
			for (int i = start; i <= end; i++)
			{
				_ = classes[i];
				if (baseLookup.TryGetValue(classes[i], out var outDataSpan2))
				{
					outDataSpan[i] = outDataSpan2;
				}
			}
		});
		IList<PriorityPolygon> polygonsLowestToHighest = PolygonsLowestToHighest;
		int[,] polygonVariableMap = GetPolygonVariableMap(highToLow: false);
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(args.Raster);
		for (int intervalIndex = 0; intervalIndex < polygonsLowestToHighest.Count; intervalIndex++)
		{
			int polygonVariableMapProfileIndexTerm = polygonVariableMap[profileIndex, intervalIndex];
			if (polygonVariableMapProfileIndexTerm == -1)
			{
				continue;
			}
			PriorityPolygon priorityPolygon = polygonsLowestToHighest[intervalIndex];
			Dictionary<int, float> classMap = priorityPolygon.VariableIntMap.Value[polygonVariableMapProfileIndexTerm];
			polygonRasterizer.Rasterize(priorityPolygon.Polygon, (int idx) =>
			{
				int key = classes[idx];
				if (classMap.TryGetValue(key, out var localValue) && localValue != -9999f)
				{
					outData.Span[idx] = localValue;
				}
			});
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<float>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void Resample(ResampleArgs args, ref float[,] outData)
	{
		Resample(args, ref outData, out var _);
	}

	public bool SamplePolygon(Polygon pgon, int rasterizerCellCount, out Dictionary<int, float> classifierFractions, out float[] perProfileWeightedAverage)
	{
		RasterDefinition rasterDefinition = RasterDefinition.ComputeBoundingRaster(pgon.Extent, rasterizerCellCount);
		bool[] boolValues = new bool[rasterDefinition.CellCount];
		if (!pgon.Rasterize(rasterDefinition, boolValues))
		{
			classifierFractions = null;
			perProfileWeightedAverage = null;
			return false;
		}
		int ordinal = 0;
		for (int i = 0; i < boolValues.Length; i++)
		{
			if (boolValues[i])
			{
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			classifierFractions = null;
			perProfileWeightedAverage = null;
			return false;
		}
		double computed = 1.0 / (double)ordinal;
		float[,] outData = null;
		Resample(new ResampleArgs(rasterDefinition), ref outData, out var classes);
		double[] doubleValues = new double[ProfileCount];
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		for (int j = 0; j < boolValues.Length; j++)
		{
			if (!boolValues[j])
			{
				continue;
			}
			for (int k = 0; k < ProfileCount; k++)
			{
				float outDataTerm = outData[k, j];
				if (outDataTerm != -9999f)
				{
					doubleValues[k] += (double)outDataTerm * computed;
				}
			}
			int key = classes[j];
			if (intByInt.TryGetValue(key, out var localValue))
			{
				intByInt[key] = localValue + 1;
			}
			else
			{
				intByInt[key] = 1;
			}
		}
		perProfileWeightedAverage = new float[ProfileCount];
		for (int l = 0; l < ProfileCount; l++)
		{
			perProfileWeightedAverage[l] = (float)doubleValues[l];
		}
		classifierFractions = new Dictionary<int, float>();
		foreach (KeyValuePair<int, int> localValue2 in intByInt)
		{
			classifierFractions.Add(localValue2.Key, (float)localValue2.Value / (float)ordinal);
		}
		return true;
	}

	public void Resample(ResampleArgs args, ref float[,] outData, out int[] classes)
	{
		args.EnsureTimeSeriesBuffer(ref outData, ProfileCount, NoDataValue.Value);
		int cellCount = args.Raster.CellCount;
		classes = new int[cellCount];
		int[] clsTmp = classes;
		_baseRasterizer.Resample(args, classes);
		IList<PriorityPolygon> polygonsLowestToHighest = PolygonsLowestToHighest;
		int[,] polygonVariableMap = GetPolygonVariableMap(highToLow: false);
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(args.Raster);
		for (int pfIdx = 0; pfIdx < ProfileCount; pfIdx++)
		{
			Dictionary<int, float> baseLookup = _baseVariableIntMap.Value[pfIdx];
			float[,] od = outData;
			Parallel.SmartFor(cellCount, (int start, int end) =>
			{
				for (int i = start; i <= end; i++)
				{
					int key = clsTmp[i];
					if (baseLookup.TryGetValue(key, out var od2))
					{
						od[pfIdx, i] = od2;
					}
				}
			});
			for (int intervalIndex = 0; intervalIndex < polygonsLowestToHighest.Count; intervalIndex++)
			{
				int polygonVariableMapPfIdxTerm = polygonVariableMap[pfIdx, intervalIndex];
				if (polygonVariableMapPfIdxTerm == -1)
				{
					continue;
				}
				PriorityPolygon priorityPolygon = polygonsLowestToHighest[intervalIndex];
				Dictionary<int, float> classMap = priorityPolygon.VariableIntMap.Value[polygonVariableMapPfIdxTerm];
				polygonRasterizer.Rasterize(priorityPolygon.Polygon, (int idx) =>
				{
					int key = clsTmp[idx];
					if (classMap.TryGetValue(key, out var od3))
					{
						od[pfIdx, idx] = od3;
					}
				});
			}
		}
	}

	private int[,] GetPolygonVariableMap(bool highToLow)
	{
		if (highToLow)
		{
			if (_polyVarMapHL == null)
			{
				_polyVarMapHL = MakePolygonVariableMap(PolygonsHighestToLowest);
			}
			return _polyVarMapHL;
		}
		if (_polyVarMapLH == null)
		{
			_polyVarMapLH = MakePolygonVariableMap(PolygonsLowestToHighest);
		}
		return _polyVarMapLH;
	}

	private int[,] MakePolygonVariableMap(IList<PriorityPolygon> polys)
	{
		int[,] polygonVariableMap = new int[_baseVariables.Count, _priorityPolygons.Count];
		for (int i = 0; i < _baseVariables.Count; i++)
		{
			Parameter baseVar = _baseVariables[i];
			for (int j = 0; j < polys.Count; j++)
			{
				polygonVariableMap[i, j] = polys[j].Variables.FirstIndexOf((Parameter cv) => cv.VariableName.Equals(baseVar.VariableName, StringComparison.OrdinalIgnoreCase));
			}
		}
		return polygonVariableMap;
	}
}
