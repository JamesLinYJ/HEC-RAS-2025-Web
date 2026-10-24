using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.Unsafe;

namespace Geospatial.PairedData;

[DebuggerDisplay("{HydraulicallyCorrectedProperties.Length} Points")]
public struct HydraulicProfile : IDataHash
{
	private struct InterpolateProperties
	{
		public double AboveFrac;

		public double BelowFrac;

		public double ElevDelta;

		public InterpolateProperties(double elev, HydraulicProperties belowHP, HydraulicProperties aboveHP)
		{
			ElevDelta = elev - belowHP.Elevation;
			AboveFrac = ElevDelta / (aboveHP.Elevation - belowHP.Elevation);
			BelowFrac = 1.0 - AboveFrac;
		}

		public double InterpolateValue(double belowValue, double aboveValue)
		{
			return belowValue * BelowFrac + aboveValue * AboveFrac;
		}
	}

	public static readonly double AssumedNValueForConveyance = 0.03;

	public Memory<HydraulicProperties> RawGeometryProperties = default;

	public Memory<HydraulicProperties> HydraulicallyCorrectedProperties = default;

	public HydraulicProfile()
	{
	}

	public static HydraulicProfile Build(ProfileBuilder profile, double areaElevFilterTol, double kTolRatio, double laminarDepth, HydraulicProfileBuilderHelper helper = null)
	{
		if (helper == null)
		{
			helper = new HydraulicProfileBuilderHelper();
		}
		HydraulicProfile result = new HydraulicProfile();
		int pointCount = profile.PointCount;
		double profileTerm = profile.Y.Min();
		double profileTerm2 = profile.Y.Max() - profileTerm;
		double profilePointCountTerm = profile.X[pointCount - 1] - profile.X[0];
		if (profileTerm2 <= 0.0)
		{
			if (laminarDepth == 0.0)
			{
				result.HydraulicallyCorrectedProperties = new HydraulicProperties[1];
				result.HydraulicallyCorrectedProperties.Span[0] = new HydraulicProperties(profileTerm, 0.0, profilePointCountTerm, profilePointCountTerm, profilePointCountTerm * 0.5);
			}
			else
			{
				result.HydraulicallyCorrectedProperties = new HydraulicProperties[2];
				Span<HydraulicProperties> hydraulicallyCorrectedProperties = result.HydraulicallyCorrectedProperties.Span;
				hydraulicallyCorrectedProperties[0] = new HydraulicProperties(profileTerm, 0.0, profilePointCountTerm, profilePointCountTerm, profilePointCountTerm * 0.5);
				hydraulicallyCorrectedProperties[1] = new HydraulicProperties(profileTerm + laminarDepth, profilePointCountTerm * laminarDepth, profilePointCountTerm, profilePointCountTerm, profilePointCountTerm * 0.5);
			}
			result.RawGeometryProperties = result.HydraulicallyCorrectedProperties;
			return result;
		}
		ComputeSampleElevations(profile, profileTerm, laminarDepth, areaElevFilterTol, helper);
		result.RawGeometryProperties = ComputeProfileHydraulicProperties(profile, helper).AsMemory();
		if (kTolRatio <= 0.0 || double.IsNaN(kTolRatio))
		{
			result.HydraulicallyCorrectedProperties = result.RawGeometryProperties;
			return result;
		}
		List<HydraulicProperties> preserveKBuffer = helper.PreserveKBuffer;
		preserveKBuffer.Clear();
		Span<HydraulicProperties> rawGeometryProperties = result.RawGeometryProperties.Span;
		preserveKBuffer.Add(rawGeometryProperties[0]);
		if (laminarDepth > 0.0)
		{
			preserveKBuffer.Add(rawGeometryProperties[1]);
			for (int i = 2; i < rawGeometryProperties.Length; i++)
			{
				EnsureHPInterpolationTolerance(areaElevFilterTol, kTolRatio, rawGeometryProperties[i - 1], rawGeometryProperties[i], preserveKBuffer);
			}
		}
		else
		{
			for (int j = 1; j < rawGeometryProperties.Length; j++)
			{
				EnsureHPInterpolationTolerance(areaElevFilterTol, kTolRatio, rawGeometryProperties[j - 1], rawGeometryProperties[j], preserveKBuffer);
			}
		}
		List<HydraulicProperties> modHPBuffer = helper.ModHPBuffer;
		modHPBuffer.Clear();
		modHPBuffer.Add(preserveKBuffer[0]);
		for (int k = 1; k < preserveKBuffer.Count; k++)
		{
			HydraulicProperties preserveKBuffer2 = preserveKBuffer[k];
			HydraulicProperties hydraulicProperties = modHPBuffer.Last();
			if (!(preserveKBuffer2.Conveyance(AssumedNValueForConveyance) > hydraulicProperties.ProjectedConveyance(preserveKBuffer2.Elevation, AssumedNValueForConveyance)))
			{
				continue;
			}
			if (hydraulicProperties.Elevation != preserveKBuffer[k - 1].Elevation)
			{
				HydraulicProperties hydraulicProperties2 = preserveKBuffer[k - 1];
				HydraulicProperties hp = preserveKBuffer[k];
				HydraulicProperties hydraulicProperties3 = hydraulicProperties2.MidPoint(hp, HydraulicProperties.AreaInterpolation.Trapezoidal);
				for (int l = 1; l <= 8; l++)
				{
					if (hp.Elevation - hydraulicProperties2.Elevation < areaElevFilterTol)
					{
						break;
					}
					if (hydraulicProperties3.Conveyance(AssumedNValueForConveyance) < hydraulicProperties.ProjectedConveyance(hydraulicProperties3.Elevation, AssumedNValueForConveyance))
					{
						hydraulicProperties2 = hydraulicProperties3;
					}
					else
					{
						hp = hydraulicProperties3;
					}
					hydraulicProperties3 = hydraulicProperties2.MidPoint(hp, HydraulicProperties.AreaInterpolation.Trapezoidal);
				}
				modHPBuffer.Add(hydraulicProperties3);
			}
			modHPBuffer.Add(preserveKBuffer2);
		}
		FilterOnConveyance(kTolRatio, modHPBuffer);
		if (modHPBuffer.Last().TopWidth < preserveKBuffer.Last().TopWidth)
		{
			double unity = 1.0;
			double computed = unity * profilePointCountTerm;
			HydraulicProperties hydraulicProperties4 = preserveKBuffer.Last();
			HydraulicProperties hydraulicProperties5;
			do
			{
				hydraulicProperties5 = hydraulicProperties4;
				hydraulicProperties4.Elevation += unity;
				hydraulicProperties4.Area += computed;
			}
			while (!(hydraulicProperties4.Conveyance(AssumedNValueForConveyance) > modHPBuffer.Last().ProjectedConveyance(hydraulicProperties4.Elevation, AssumedNValueForConveyance)));
			HydraulicProperties hydraulicProperties6 = hydraulicProperties5.MidPoint(hydraulicProperties4, HydraulicProperties.AreaInterpolation.Trapezoidal);
			for (int m = 1; m <= 8; m++)
			{
				if (hydraulicProperties4.Elevation - hydraulicProperties5.Elevation < areaElevFilterTol)
				{
					break;
				}
				if (hydraulicProperties6.Conveyance(AssumedNValueForConveyance) < modHPBuffer.Last().ProjectedConveyance(hydraulicProperties6.Elevation, AssumedNValueForConveyance))
				{
					hydraulicProperties5 = hydraulicProperties6;
				}
				else
				{
					hydraulicProperties4 = hydraulicProperties6;
				}
				hydraulicProperties6 = hydraulicProperties5.MidPoint(hydraulicProperties4, HydraulicProperties.AreaInterpolation.Trapezoidal);
			}
			modHPBuffer.Add(hydraulicProperties6);
		}
		result.HydraulicallyCorrectedProperties = modHPBuffer.ToArray();
		return result;
	}

	private static void ComputeSampleElevations(ProfileBuilder profile, double minElev, double laminarDepth, double elevTol, HydraulicProfileBuilderHelper helper)
	{
		List<double> sampleElevations = helper.SampleElevations;
		sampleElevations.Clear();
		sampleElevations.Add(minElev);
		if (laminarDepth > 0.0)
		{
			sampleElevations.Add(minElev + laminarDepth);
		}
		for (int i = 0; i < profile.PointCount; i++)
		{
			if (!((laminarDepth > 0.0) & (profile.Y[i] < minElev + laminarDepth)))
			{
				if (i > 0 && Math.Abs(profile.Y[i] - profile.Y[i - 1]) <= elevTol)
				{
					sampleElevations.Add(Math.Max(profile.Y[i], profile.Y[i - 1]) + elevTol);
				}
				else
				{
					sampleElevations.Add(profile.Y[i]);
				}
			}
		}
		sampleElevations.Sort();
		List<double> goodSampleElevations = helper.GoodSampleElevations;
		goodSampleElevations.Clear();
		goodSampleElevations.Add(sampleElevations.First());
		double scaledElevTol = elevTol * 0.99999;
		for (int j = 1; j < sampleElevations.Count; j++)
		{
			if (sampleElevations[j] > goodSampleElevations.Last() + scaledElevTol)
			{
				goodSampleElevations.Add(sampleElevations[j]);
			}
		}
		if (goodSampleElevations.Last() < sampleElevations.Last())
		{
			goodSampleElevations.Add(sampleElevations.Last() + elevTol);
		}
	}

	private static HydraulicProperties[] ComputeProfileHydraulicProperties(ProfileBuilder profile, HydraulicProfileBuilderHelper helper)
	{
		Span<double> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(helper.GoodSampleElevations).Span;
		HydraulicProperties[] hydraulicProperties = new HydraulicProperties[listBackingBuffer.Length];
		for (int i = 0; i < listBackingBuffer.Length; i++)
		{
			hydraulicProperties[i].Elevation = listBackingBuffer[i];
		}
		for (int j = 1; j < profile.PointCount; j++)
		{
			Point point = new Point(profile.X[j - 1], profile.Y[j - 1]);
			Point other = new Point(profile.X[j], profile.Y[j]);
			double x = point.X;
			double x2 = other.X;
			double x2Term = x2 - x;
			double computed = x + x2Term * 0.5;
			if (point.Y == other.Y)
			{
				double y = point.Y;
				for (int k = 0; k < hydraulicProperties.Length; k++)
				{
					double elevation = hydraulicProperties[k].Elevation;
					if (!(elevation < y) && (elevation > y || k == 0))
					{
						double elevationTerm = (elevation - y) * x2Term;
						double computed2 = elevationTerm * computed;
						hydraulicProperties[k].Area += elevationTerm;
						hydraulicProperties[k].TopWidth += x2Term;
						hydraulicProperties[k].WettedPerimeter += x2Term;
						hydraulicProperties[k].CentroidX += computed2;
					}
				}
				continue;
			}
			double y2;
			double y3;
			bool condition;
			if (point.Y < other.Y)
			{
				y2 = point.Y;
				y3 = other.Y;
				condition = true;
			}
			else
			{
				y2 = other.Y;
				y3 = point.Y;
				condition = false;
			}
			double distanceTo = point.DistanceTo(other);
			double y3AboveY2 = y3 - y2;
			double y3Y2Term = x2Term * (y3 - y2) * 0.5;
			for (int l = 0; l < hydraulicProperties.Length; l++)
			{
				double elevation2 = hydraulicProperties[l].Elevation;
				if (elevation2 <= y2)
				{
					continue;
				}
				if (elevation2 >= y3)
				{
					double elevation2Y3Term = (elevation2 - y3) * x2Term;
					hydraulicProperties[l].Area += y3Y2Term + elevation2Y3Term;
					hydraulicProperties[l].TopWidth += x2Term;
					hydraulicProperties[l].WettedPerimeter += distanceTo;
					if (condition)
					{
						hydraulicProperties[l].CentroidX += elevation2Y3Term * computed + y3Y2Term * (x + x2Term / 3.0);
					}
					else
					{
						hydraulicProperties[l].CentroidX += elevation2Y3Term * computed + y3Y2Term * (x2 - x2Term / 3.0);
					}
					continue;
				}
				double elevation2AboveY2 = elevation2 - y2;
				double elevation2AboveY2OverY3AboveY2 = elevation2AboveY2 / y3AboveY2;
				double computed3 = x2Term * elevation2AboveY2OverY3AboveY2;
				double computed4 = computed3 * elevation2AboveY2 * 0.5;
				hydraulicProperties[l].Area += computed4;
				hydraulicProperties[l].TopWidth += computed3;
				hydraulicProperties[l].WettedPerimeter += elevation2AboveY2OverY3AboveY2 * distanceTo;
				if (condition)
				{
					hydraulicProperties[l].CentroidX += computed4 * (x + computed3 / 3.0);
				}
				else
				{
					hydraulicProperties[l].CentroidX += computed4 * (x2 - computed3 / 3.0);
				}
			}
		}
		double centroidX = 0.0;
		for (int m = hydraulicProperties.Length - 1; m >= 0; m += -1)
		{
			if (hydraulicProperties[m].Area > 0.0)
			{
				hydraulicProperties[m].CentroidX = hydraulicProperties[m].CentroidX / hydraulicProperties[m].Area;
				centroidX = hydraulicProperties[m].CentroidX;
			}
			else
			{
				hydraulicProperties[m].CentroidX = centroidX;
			}
		}
		return hydraulicProperties;
	}

	private static void EnsureHPInterpolationTolerance(double elevTol, double kTolRatio, HydraulicProperties minHP, HydraulicProperties maxHP, List<HydraulicProperties> hydrProp)
	{
		double conveyance = minHP.Conveyance(AssumedNValueForConveyance);
		double conveyance2 = maxHP.Conveyance(AssumedNValueForConveyance);
		double elevationDifference = maxHP.Elevation - minHP.Elevation;
		if (conveyance < conveyance2 && elevationDifference > elevTol)
		{
			double basek = (conveyance + conveyance2) * 0.5;
			HydraulicProperties maxHP2 = minHP.MidPoint(maxHP, HydraulicProperties.AreaInterpolation.Trapezoidal);
			if (ConveyanceTolRatio(basek, maxHP2.Conveyance(AssumedNValueForConveyance)) > kTolRatio)
			{
				EnsureHPInterpolationTolerance(elevTol, kTolRatio, minHP, maxHP2, hydrProp);
			}
		}
		hydrProp.Add(maxHP);
	}

	private static double ConveyanceTolRatio(double basek, double compareK)
	{
		return Math.Abs(basek - compareK) / basek;
	}

	private static void FilterOnConveyance(double KTolRatio, List<HydraulicProperties> HPs)
	{
		List<int> intList = new List<int>(HPs.Count);
		int ordinal = 0;
		int countOffset = HPs.Count - 1;
		intList.Add(ordinal);
		intList.Add(countOffset);
		FilterDouglasPeuckerRamer(ordinal, countOffset, KTolRatio, HPs, intList);
		if (intList.Count == HPs.Count)
		{
			return;
		}
		bool[] boolValues = new bool[HPs.Count];
		foreach (int ordinal2 in intList)
		{
			boolValues[ordinal2] = true;
		}
		int ordinal3 = 0;
		for (int i = 0; i < HPs.Count; i++)
		{
			if (boolValues[i])
			{
				if (ordinal3 > 0)
				{
					HPs[i - ordinal3] = HPs[i];
				}
			}
			else
			{
				ordinal3++;
			}
		}
		HPs.RemoveRange(HPs.Count - ordinal3 - 1, ordinal3);
	}

	private static void FilterDouglasPeuckerRamer(int first, int last, double KTolRatio, List<HydraulicProperties> HPs, List<int> keepIndex)
	{
		if (last - first <= 1)
		{
			return;
		}
		double conveyanceTolRatio = 0.0;
		int ordinal = 0;
		for (int i = first + 1; i < last; i++)
		{
			HydraulicProperties hydraulicProperties = HPs[i];
			double compareK = InterpolateConveyance(hydraulicProperties.Elevation, HPs[first], HPs[last]);
			double conveyanceTolRatio2 = ConveyanceTolRatio(hydraulicProperties.Conveyance(AssumedNValueForConveyance), compareK);
			if (conveyanceTolRatio2 > conveyanceTolRatio)
			{
				conveyanceTolRatio = conveyanceTolRatio2;
				ordinal = i;
			}
		}
		if (conveyanceTolRatio > KTolRatio)
		{
			keepIndex.Add(ordinal);
			FilterDouglasPeuckerRamer(first, ordinal, KTolRatio, HPs, keepIndex);
			FilterDouglasPeuckerRamer(ordinal, last, KTolRatio, HPs, keepIndex);
		}
	}

	public static double InterpolateConveyance(double elev, HydraulicProperties belowHP, HydraulicProperties aboveHP)
	{
		return new InterpolateProperties(elev, belowHP, aboveHP).InterpolateValue(belowHP.Conveyance(AssumedNValueForConveyance), aboveHP.Conveyance(AssumedNValueForConveyance));
	}

	public int GetDataHash()
	{
		return StableHash.Combine(StableHash.Combine(0, GetDataHash(RawGeometryProperties)), GetDataHash(HydraulicallyCorrectedProperties));
	}

	public static int GetDataHash(Memory<HydraulicProperties> props)
	{
		int combine = 0;
		Span<HydraulicProperties> propsSpan = props.Span;
		for (int i = 0; i < propsSpan.Length; i++)
		{
			combine = StableHash.Combine(combine, propsSpan[i].GetDataHash());
		}
		return combine;
	}
}
