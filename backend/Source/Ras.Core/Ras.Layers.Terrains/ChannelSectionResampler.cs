using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Utility.Memory;

namespace Ras.Layers.Terrains;

public class ChannelSectionResampler : IResample<float>, ISurface<float>
{
	public struct InputCrossSection
	{
		public IReadOnlyList<Polyline> Lines;

		public StationValueProfile<double> Profile;
	}

	private struct EvaluatedCrossSection
	{
		private StationElevationProfile _mToValue;

		public Polyline Line { get; private set; }

		public IReadOnlyList<double> M { get; private set; }

		public EvaluatedCrossSection(InputCrossSection crossSection, IReadOnlyList<double> mValues)
		{
			List<Point> points = new List<Point>();
			List<double> doubleList = new List<double>();
			for (int i = 0; i < mValues.Count - 1; i++)
			{
				Polyline polyline = crossSection.Lines[i];
				for (int j = 0; j < polyline.PointCount - 1; j++)
				{
					double distanceOfPointIdxOverLength = ChannelSectionHelpers.GetDistanceOfPointIdx(polyline, j) / polyline.Length;
					double interpolatedValue = mValues[i] + distanceOfPointIdxOverLength * (mValues[i + 1] - mValues[i]);
					points.Add(polyline.Points[j]);
					doubleList.Add(interpolatedValue);
				}
			}
			Polyline polyline2 = crossSection.Lines[crossSection.Lines.Count - 1];
			points.Add(polyline2.Points[polyline2.PointCount - 1]);
			doubleList.Add(mValues[mValues.Count - 1]);
			Line = new Polyline(points);
			M = doubleList;
			double[] doubleValues = new double[mValues.Count];
			double linesCrossSectionTerm = (doubleValues[0] = 0.0);
			for (int k = 0; k < crossSection.Lines.Count; k++)
			{
				linesCrossSectionTerm = (doubleValues[k + 1] = linesCrossSectionTerm + crossSection.Lines[k].Length);
			}
			for (int l = 0; l < mValues.Count; l++)
			{
				doubleValues[l] /= linesCrossSectionTerm;
			}
			StationElevationProfile stationElevationProfile = new StationElevationProfile(doubleValues, mValues);
			StationValueProfile<double> profile = crossSection.Profile;
			double[] doubleValuesArray = new double[profile.Count];
			for (int m = 0; m < profile.Count; m++)
			{
				doubleValuesArray[m] = stationElevationProfile.Query((profile.Stations[m] - profile.MinStation) / (profile.MaxStation - profile.MinStation));
			}
			_mToValue = new StationElevationProfile(doubleValuesArray, profile.Values);
		}

		public float GetHeightAtStation(float m)
		{
			return (float)_mToValue.Query(m);
		}
	}

	private struct EvaluatedRiverLine
	{
		public Polyline Line { get; private set; }

		public double[] Z { get; private set; }

		public EvaluatedRiverLine(Polyline line)
		{
			Line = line;
			Z = new double[Line.PointCount];
			_ = Line.Length;
			for (int i = 0; i < Line.PointCount; i++)
			{
				Z[i] = ChannelSectionHelpers.GetDistanceOfPointIdx(Line, i) / Line.Length;
			}
		}
	}

	private Tin _tin;

	public TinResampler _zTinResampler;

	public TinResampler _mTinResampler;

	public TinResampler _maskTinResampler;

	private EvaluatedCrossSection _upstreamCrossSection;

	private EvaluatedCrossSection _downstreamCrossSection;

	private IReadOnlyList<EvaluatedRiverLine> _riverLines;

	private IReadOnlyList<double> _upstreamM;

	private IReadOnlyList<double> _downstreamM;

	private IReadOnlyList<double> _riverM;

	public Extent ValidExtent => _tin.Extent;

	public float? NoDataValue => -9999f;

	public RasterModType ModificationType { get; set; }

	public ChannelSectionResampler(InputCrossSection inUpstreamCrossSection, InputCrossSection inDownstreamCrossSection, IReadOnlyList<Polyline> inRiverLines, IReadOnlyList<double> upstreamM, IReadOnlyList<double> downstreamM, IReadOnlyList<double> riverM, TinResampler maskTinResampler, double tinSegLength, RasterModType modificationType = RasterModType.SetValue)
	{
		_maskTinResampler = maskTinResampler;
		ModificationType = modificationType;
		_upstreamM = upstreamM;
		_downstreamM = downstreamM;
		_riverM = riverM;
		EvaluatedRiverLine[] riverLines = new EvaluatedRiverLine[inRiverLines.Count];
		for (int i = 0; i < inRiverLines.Count; i++)
		{
			riverLines[i] = new EvaluatedRiverLine(inRiverLines[i]);
		}
		_riverLines = riverLines;
		_upstreamCrossSection = new EvaluatedCrossSection(inUpstreamCrossSection, _upstreamM);
		_downstreamCrossSection = new EvaluatedCrossSection(inDownstreamCrossSection, _downstreamM);
		_tin = GetZMTin(_upstreamCrossSection, _downstreamCrossSection, _riverLines, _upstreamM, _downstreamM, _riverM, tinSegLength);
		_zTinResampler = new TinResampler(_tin, renderZ: true);
		_mTinResampler = new TinResampler(_tin, renderZ: false);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(outData.Length);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(outData.Length);
		using IMemoryOwner<float> memoryOwner3 = SharedMemoryPool<float>.Rent(outData.Length);
		Memory<float> outData2 = memoryOwner.Memory.Slice(0, outData.Length);
		Span<float> outData2Span = outData2.Span;
		Memory<float> outData3 = memoryOwner2.Memory.Slice(0, outData.Length);
		Span<float> outData3Span = outData3.Span;
		Memory<float> outData4 = memoryOwner3.Memory.Slice(0, outData.Length);
		Span<float> outData4Span = outData4.Span;
		if (NoDataValue.HasValue)
		{
			outData2Span.Fill(NoDataValue.Value);
			outData3Span.Fill(NoDataValue.Value);
		}
		_mTinResampler.Resample(args, outData3);
		_zTinResampler.Resample(args, outData2);
		if (_maskTinResampler != null)
		{
			_maskTinResampler.Resample(args, outData4);
		}
		else
		{
			outData4Span.Fill(float.NaN);
		}
		Span<float> outDataSpan = outData.Span;
		float noDataValueTerm = NoDataValue.Value;
		for (int i = 0; i < outData3.Length; i++)
		{
			if ((double)outData4Span[i] != 1.0 && outData3Span[i] != NoDataValue && outData2Span[i] != NoDataValue)
			{
				float heightAtStation = _upstreamCrossSection.GetHeightAtStation(outData3Span[i]);
				float heightAtStation2 = _downstreamCrossSection.GetHeightAtStation(outData3Span[i]);
				float elev = heightAtStation + (heightAtStation2 - heightAtStation) * outData2Span[i];
				outDataSpan[i] = CombineSample(outDataSpan[i], elev, noDataValueTerm);
			}
		}
	}

	private float CombineSample(float existing, float elev, float ndv)
	{
		switch (ModificationType)
		{
		case RasterModType.SetValue:
			return elev;
		case RasterModType.TakeHigher:
			if (existing == ndv || elev > existing)
			{
				return elev;
			}
			return existing;
		case RasterModType.TakeLower:
			if (existing == ndv || elev < existing)
			{
				return elev;
			}
			return existing;
		case RasterModType.AddValue:
			if (existing == ndv)
			{
				return existing;
			}
			return existing + elev;
		default:
			throw new NotImplementedException();
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(outData.Length);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(outData.Length);
		Memory<float> outData2 = memoryOwner.Memory.Slice(0, outData.Length);
		Span<float> outData2Span = outData2.Span;
		Memory<float> outData3 = memoryOwner2.Memory.Slice(0, outData.Length);
		Span<float> outData3Span = outData3.Span;
		if (NoDataValue.HasValue)
		{
			outData2Span.Fill(NoDataValue.Value);
			outData3Span.Fill(NoDataValue.Value);
		}
		_mTinResampler.SamplePoints(points, outData3, sampleParams);
		_zTinResampler.SamplePoints(points, outData2, sampleParams);
		Span<float> outDataSpan = outData.Span;
		float noDataValueTerm = NoDataValue.Value;
		for (int i = 0; i < outData3Span.Length; i++)
		{
			if (outData3Span[i] != NoDataValue && outData2Span[i] != NoDataValue)
			{
				float heightAtStation = _upstreamCrossSection.GetHeightAtStation(outData3Span[i]);
				float heightAtStation2 = _downstreamCrossSection.GetHeightAtStation(outData3Span[i]);
				float elev = heightAtStation + (heightAtStation2 - heightAtStation) * outData2Span[i];
				outDataSpan[i] = CombineSample(outDataSpan[i], elev, noDataValueTerm);
			}
		}
	}

	private static Tin GetZMTin(EvaluatedCrossSection upstreamLine, EvaluatedCrossSection downstreamLine, IReadOnlyList<EvaluatedRiverLine> riverLines, IReadOnlyList<double> upstreamM, IReadOnlyList<double> downstreamM, IReadOnlyList<double> riverM, double maxEdgeLength)
	{
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
		delaunayTinFactory.MaxEdgeLength = maxEdgeLength;
		delaunayTinFactory.KeepDeletedTriangles = true;
		List<Point> points = new List<Point>();
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		for (int pointCountOffset = riverLines[0].Line.PointCount - 2; pointCountOffset >= 1; pointCountOffset--)
		{
			points.Add(riverLines[0].Line.Points[pointCountOffset]);
			doubleList.Add(riverLines[0].Z[pointCountOffset]);
			doubleListList.Add(riverM[0]);
		}
		for (int i = 0; i < upstreamLine.Line.Count; i++)
		{
			points.Add(upstreamLine.Line.Points[i]);
			doubleList.Add(0.0);
			doubleListList.Add(upstreamLine.M[i]);
		}
		for (int j = 1; j < riverLines[riverLines.Count - 1].Line.PointCount - 1; j++)
		{
			points.Add(riverLines[riverLines.Count - 1].Line.Points[j]);
			doubleList.Add(riverLines[riverLines.Count - 1].Z[j]);
			doubleListList.Add(riverM[riverM.Count - 1]);
		}
		for (int pointCountOffset2 = downstreamLine.Line.PointCount - 1; pointCountOffset2 >= 0; pointCountOffset2--)
		{
			points.Add(downstreamLine.Line.Points[pointCountOffset2]);
			doubleList.Add(1.0);
			doubleListList.Add(downstreamLine.M[pointCountOffset2]);
		}
		delaunayTinFactory.AddPerimeter(new Polygon(points, doubleList, doubleListList));
		for (int k = 1; k < riverLines.Count - 1; k++)
		{
			EvaluatedRiverLine evaluatedRiverLine = riverLines[k];
			double[] doubleValues = new double[evaluatedRiverLine.Line.PointCount];
			Array.Fill(doubleValues, riverM[k]);
			delaunayTinFactory.AddBreakline(new Polyline(evaluatedRiverLine.Line.Points.ToArray(), evaluatedRiverLine.Z, doubleValues));
		}
		Tin tin = delaunayTinFactory.ComputeTIN();
		TinLaplacian tinLaplacian = new TinLaplacian(tin);
		double[] doubleValuesArray = tin.Z.ToArray();
		double[] doubleValues2 = tin.M.ToArray();
		tinLaplacian.SolveLaplacian(doubleValuesArray);
		tinLaplacian.SolveLaplacian(doubleValues2);
		for (int l = 0; l < tin.Z.Count; l++)
		{
			tin.Z[l] = doubleValuesArray[l];
		}
		for (int m = 0; m < tin.M.Count; m++)
		{
			tin.M[m] = doubleValues2[m];
		}
		return tin;
	}
}
