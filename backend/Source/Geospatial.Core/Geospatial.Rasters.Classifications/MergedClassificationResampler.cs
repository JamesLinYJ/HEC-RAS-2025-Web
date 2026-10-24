using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.Vectors;
using Utility;
using Utility.Extensions;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Rasters.Classifications;

public class MergedClassificationResampler : IClassificationResampler, IResample<int>, ISurface<int>, IBlame
{
	private readonly IResample<int> _res1;

	private readonly IResample<int> _res2;

	private readonly MergedClassification _mergedClassifier;

	public Extent ValidExtent => _res1.ValidExtent.Union(_res2.ValidExtent);

	public int? NoDataValue => 0;

	public MergedClassification MergedClassifier => _mergedClassifier;

	public IClassification Classifier => _mergedClassifier;

	public IRasterSelector RasterSelector { get; set; } = new EqualDefinitionRasterSelector(10000);

	public MergedClassificationResampler(IResample<int> res1, IResample<int> res2, MergedClassification mergedClassifier)
	{
		_res1 = res1;
		_res2 = res2;
		_mergedClassifier = mergedClassifier;
	}

	public MergedClassificationResampler(IClassificationResampler res1, IClassificationResampler res2)
	{
		_res1 = res1;
		_res2 = res2;
		_mergedClassifier = new MergedClassification(res1.Classifier, res2.Classifier);
	}

	public void Resample(ResampleArgs args, Memory<int> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		int length = outData.Length;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(length);
		Memory<int> slice = memoryOwner.Memory.Slice(0, length);
		using IMemoryOwner<int> memoryOwner2 = SharedMemoryPool<int>.Rent(length);
		Memory<int> sliceSpan = memoryOwner2.Memory.Slice(0, length);
		_res1.Resample(args2, slice);
		_res2.Resample(args2, sliceSpan);
		ConvertValues(slice, sliceSpan, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<int> result, SamplingParams sampleParams = default(SamplingParams))
	{
		int length = points.Length;
		using IMemoryOwner<int> memoryOwner = SharedMemoryPool<int>.Rent(length);
		Memory<int> slice = memoryOwner.Memory.Slice(0, length);
		using IMemoryOwner<int> memoryOwner2 = SharedMemoryPool<int>.Rent(length);
		Memory<int> sliceSpan = memoryOwner2.Memory.Slice(0, length);
		_res1.SamplePoints(points, slice);
		_res2.SamplePoints(points, sliceSpan);
		ConvertValues(slice, sliceSpan, result);
	}

	public Dictionary<int, double> SampleClassifications(Polygon polygon)
	{
		return ClassificationRasterOperations.SampleClassifications(this, polygon, RasterSelector.GetAppropriateResampleRaster(polygon));
	}

	public List<Dictionary<int, double>> SampleClassifications(IList<Polygon> polygons)
	{
		List<Dictionary<int, double>> doubleByIntList = new List<Dictionary<int, double>>(polygons.Count);
		foreach (Polygon polygon in polygons)
		{
			doubleByIntList.Add(SampleClassifications(polygon));
		}
		return doubleByIntList;
	}

	private void ConvertValues(Memory<int> v1, Memory<int> v2, Memory<int> output)
	{
		Parallel.SmartFor(v1.Length, (int start, int fin) =>
		{
			Span<int> v1Span = v1.Span;
			Span<int> v2Span = v2.Span;
			Span<int> outputSpan = output.Span;
			for (int i = start; i <= fin; i++)
			{
				outputSpan[i] = _mergedClassifier.GetOutputID(v1Span[i], v2Span[i]);
			}
		});
	}

	public void Blame(Point pt, ProgressReporter reporter)
	{
		reporter.ReportMessage("MergedClassificationResampler: Querying " + pt.ToString());
		(_res1 as IBlame)?.Blame(pt, reporter.WithMessageModifier("   (Input A) ", string.Empty));
		(_res2 as IBlame)?.Blame(pt, reporter.WithMessageModifier("   (Input B) ", string.Empty));
		int samplePoint = _res1.SamplePoint(pt);
		int samplePoint2 = _res2.SamplePoint(pt);
		string name = _mergedClassifier.ClassificationA.GetName(samplePoint);
		string name2 = _mergedClassifier.ClassificationB.GetName(samplePoint2);
		reporter.ReportMessage("Merged Classifier sampled: A = " + name + " " + samplePoint.ToString().InParentheses() + ", B = " + name2 + " " + samplePoint2.ToString().InParentheses());
	}
}
