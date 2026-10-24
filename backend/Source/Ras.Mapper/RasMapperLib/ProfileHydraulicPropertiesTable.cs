using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class ProfileHydraulicPropertiesTable
{
	public struct HydraulicProperties
	{
		public enum AreaInterpolation
		{
			Linear,
			Trapazoidal
		}

		public float Elevation;

		public float Area;

		public float TopWidth;

		public float WettedPerimeter;

		public float CentroidX;

		public HydraulicProperties(float elev, float a, float tw, float wp, float centX)
		{
			this = default;
			Elevation = elev;
			Area = a;
			TopWidth = tw;
			WettedPerimeter = wp;
			CentroidX = centX;
		}

		public void Clear()
		{
			Elevation = 0f;
			Area = 0f;
			WettedPerimeter = 0f;
			TopWidth = 0f;
			CentroidX = 0f;
		}

		public float HydraulicDepth()
		{
			if ((double)TopWidth == 0.0)
			{
				return 0f;
			}
			return Area / TopWidth;
		}

		public float HydraulicRadius()
		{
			if ((double)WettedPerimeter == 0.0)
			{
				return 0f;
			}
			return Area / WettedPerimeter;
		}

		public float Conveyance(float mannN = 0.03f)
		{
			return (float)((double)Area * Math.Pow(HydraulicRadius(), 2.0 / 3.0) / (double)mannN);
		}

		public float ProjectedConveyance(float elevAbove, float mannN = 0.03f)
		{
			float elevAboveAboveElevation = elevAbove - Elevation;
			float areaTopWidthTerm = Area + TopWidth * elevAboveAboveElevation;
			float wettedPerimeterTerm = (float)((double)WettedPerimeter + (double)elevAboveAboveElevation * 2.0);
			float computed = areaTopWidthTerm / wettedPerimeterTerm;
			return (float)((double)areaTopWidthTerm * Math.Pow(computed, 2.0 / 3.0) / (double)mannN);
		}

		public HydraulicProperties MidPoint(HydraulicProperties hp, AreaInterpolation areaInter)
		{
			HydraulicProperties result = default;
			result.Elevation = (float)((double)(Elevation + hp.Elevation) * 0.5);
			result.TopWidth = (float)((double)(TopWidth + hp.TopWidth) * 0.5);
			result.WettedPerimeter = (float)((double)(WettedPerimeter + hp.WettedPerimeter) * 0.5);
			result.CentroidX = (float)((double)(CentroidX + hp.CentroidX) * 0.5);
			if (areaInter == AreaInterpolation.Linear)
			{
				result.Area = (float)((double)(Area + hp.Area) * 0.5);
			}
			else if (Elevation < hp.Elevation)
			{
				float elevationDifference = result.Elevation - Elevation;
				result.Area = (float)((double)Area + (double)(TopWidth + result.TopWidth) * 0.5 * (double)elevationDifference);
			}
			else
			{
				float elevationDifference2 = result.Elevation - hp.Elevation;
				result.Area = (float)((double)hp.Area + (double)(hp.TopWidth + result.TopWidth) * 0.5 * (double)elevationDifference2);
			}
			return result;
		}

		public override string ToString()
		{
			return "el=" + Strings.Format(Elevation) + " a=" + Strings.Format(Area) + " tw=" + Strings.Format(TopWidth) + " wp=" + Strings.Format(WettedPerimeter) + " cx=" + Conversions.ToString(CentroidX);
		}
	}

	private struct InterpolateProperties
	{
		public float AboveFrac;

		public float BelowFrac;

		public float ElevDelta;

		public InterpolateProperties(float elev, HydraulicProperties belowHP, HydraulicProperties aboveHP)
		{
			this = default;
			ElevDelta = elev - belowHP.Elevation;
			AboveFrac = ElevDelta / (aboveHP.Elevation - belowHP.Elevation);
			BelowFrac = (float)(1.0 - (double)AboveFrac);
		}

		public float InterpolateValue(float belowValue, float aboveValue)
		{
			return belowValue * BelowFrac + aboveValue * AboveFrac;
		}
	}

	public class ElevationComparer : IComparer<HydraulicProperties>
	{
		public int Compare(HydraulicProperties a, HydraulicProperties b)
		{
			return a.Elevation.CompareTo(b.Elevation);
		}

		int IComparer<HydraulicProperties>.Compare(HydraulicProperties a, HydraulicProperties b)
		{
			//ILSpy generated this explicit interface implementation from .override directive in Compare
			return this.Compare(a, b);
		}
	}

	public class AreaComparer : IComparer<HydraulicProperties>
	{
		public int Compare(HydraulicProperties a, HydraulicProperties b)
		{
			return a.Area.CompareTo(b.Area);
		}

		int IComparer<HydraulicProperties>.Compare(HydraulicProperties a, HydraulicProperties b)
		{
			//ILSpy generated this explicit interface implementation from .override directive in Compare
			return this.Compare(a, b);
		}
	}

	public HydraulicProperties[] GeomProp;

	public HydraulicProperties[] HydrProp;

	public ProfileHydraulicPropertiesTable(Profile profile, float elevTol, float kTolRatio, float laminarDepth, bool showDebugPlots = false)
	{
		GeomProp = null;
		HydrProp = null;
		float minElevation = profile.MinElevation();
		float maxElevationProfileTerm = profile.MaxElevation() - minElevation;
		float profileTerm = profile.Last().X - profile.First().X;
		if ((double)maxElevationProfileTerm <= 0.0)
		{
			if (laminarDepth == 0f)
			{
				HydrProp = new HydraulicProperties[1];
				HydrProp[0] = new HydraulicProperties(minElevation, 0f, profileTerm, profileTerm, (float)((double)profileTerm * 0.5));
				GeomProp = HydrProp;
			}
			else
			{
				HydrProp = new HydraulicProperties[2];
				HydrProp[0] = new HydraulicProperties(minElevation, 0f, profileTerm, profileTerm, (float)((double)profileTerm * 0.5));
				HydrProp[1] = new HydraulicProperties(minElevation + laminarDepth, profileTerm * laminarDepth, profileTerm, profileTerm, (float)((double)profileTerm * 0.5));
			}
			GeomProp = HydrProp;
			return;
		}
		float[] elevations = ComputeSampleElevations(profile, minElevation, laminarDepth, elevTol);
		GeomProp = ComputeProfileHydraulicProperties(profile, elevations);
		if ((double)kTolRatio <= 0.0 || float.IsNaN(kTolRatio))
		{
			HydrProp = GeomProp;
			return;
		}
		if (showDebugPlots)
		{
			Plot(profile);
		}
		if (showDebugPlots)
		{
			Plot(GeomProp, "Geometry");
		}
		List<HydraulicProperties> hydraulicPropertiesList = new List<HydraulicProperties> { GeomProp.First() };
		checked
		{
			if (laminarDepth > 0f)
			{
				hydraulicPropertiesList.Add(GeomProp[1]);
				int count = GeomProp.Count() - 1;
				for (int i = 2; i <= count; i++)
				{
					EnsureHPInterpolationTolerance(elevTol, kTolRatio, GeomProp[i - 1], GeomProp[i], hydraulicPropertiesList);
				}
			}
			else
			{
				int count2 = GeomProp.Count() - 1;
				for (int j = 1; j <= count2; j++)
				{
					EnsureHPInterpolationTolerance(elevTol, kTolRatio, GeomProp[j - 1], GeomProp[j], hydraulicPropertiesList);
				}
			}
			if (showDebugPlots)
			{
				Plot(hydraulicPropertiesList.ToArray(), "Interpolation KRatio");
			}
			List<HydraulicProperties> hydraulicProperties7 = new List<HydraulicProperties> { hydraulicPropertiesList.First() };
			int count3 = hydraulicPropertiesList.Count - 1;
			for (int k = 1; k <= count3; k++)
			{
				HydraulicProperties hydraulicProperty = hydraulicPropertiesList[k];
				HydraulicProperties hydraulicProperties = hydraulicProperties7.Last();
				if (!(hydraulicProperty.Conveyance() > hydraulicProperties.ProjectedConveyance(hydraulicProperty.Elevation)))
				{
					continue;
				}
				if (hydraulicProperties.Elevation != hydraulicPropertiesList[k - 1].Elevation)
				{
					HydraulicProperties hydraulicProperties2 = hydraulicPropertiesList[k - 1];
					HydraulicProperties hp = hydraulicPropertiesList[k];
					HydraulicProperties hydraulicProperties3 = hydraulicProperties2.MidPoint(hp, HydraulicProperties.AreaInterpolation.Trapazoidal);
					int unity = 1;
					while (!(hp.Elevation - hydraulicProperties2.Elevation < elevTol))
					{
						if (hydraulicProperties3.Conveyance() < hydraulicProperties.ProjectedConveyance(hydraulicProperties3.Elevation))
						{
							hydraulicProperties2 = hydraulicProperties3;
						}
						else
						{
							hp = hydraulicProperties3;
						}
						hydraulicProperties3 = hydraulicProperties2.MidPoint(hp, HydraulicProperties.AreaInterpolation.Trapazoidal);
						unity++;
						if (unity > 8)
						{
							break;
						}
					}
					hydraulicProperties7.Add(hydraulicProperties3);
				}
				hydraulicProperties7.Add(hydraulicProperty);
			}
			FilterOnConveyance(kTolRatio, hydraulicProperties7);
			if (showDebugPlots)
			{
				Plot(hydraulicProperties7.ToArray(), "Filtered");
			}
			if (hydraulicProperties7.Last().TopWidth < hydraulicPropertiesList.Last().TopWidth)
			{
				float unity2 = 1f;
				float computed = unity2 * profileTerm;
				HydraulicProperties hydraulicProperties4 = hydraulicPropertiesList.Last();
				HydraulicProperties hydraulicProperties5;
				do
				{
					hydraulicProperties5 = hydraulicProperties4;
					hydraulicProperties4.Elevation += unity2;
					hydraulicProperties4.Area += computed;
				}
				while (!(hydraulicProperties4.Conveyance() > hydraulicProperties7.Last().ProjectedConveyance(hydraulicProperties4.Elevation)));
				HydraulicProperties hydraulicProperties6 = hydraulicProperties5.MidPoint(hydraulicProperties4, HydraulicProperties.AreaInterpolation.Trapazoidal);
				int unity3 = 1;
				while (!(hydraulicProperties4.Elevation - hydraulicProperties5.Elevation < elevTol))
				{
					if (hydraulicProperties6.Conveyance() < hydraulicProperties7.Last().ProjectedConveyance(hydraulicProperties6.Elevation))
					{
						hydraulicProperties5 = hydraulicProperties6;
					}
					else
					{
						hydraulicProperties4 = hydraulicProperties6;
					}
					hydraulicProperties6 = hydraulicProperties5.MidPoint(hydraulicProperties4, HydraulicProperties.AreaInterpolation.Trapazoidal);
					unity3++;
					if (unity3 > 8)
					{
						break;
					}
				}
				hydraulicProperties7.Add(hydraulicProperties6);
			}
			if (showDebugPlots)
			{
				Plot(hydraulicProperties7.ToArray(), "Modified for Hydraulics");
			}
			HydrProp = hydraulicProperties7.ToArray();
		}
	}

	private float[] ComputeSampleElevations(Profile profile, float minElev, float laminarDepth, float elevTol)
	{
		List<float> floatList = new List<float>();
		floatList.Add(minElev);
		if (laminarDepth > 0f)
		{
			floatList.Add(minElev + laminarDepth);
		}
		checked
		{
			int count = profile.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!((laminarDepth > 0f) & (profile[i].Y < minElev + laminarDepth)))
				{
					if (i > 0 && Math.Abs(profile[i].Y - profile[i - 1].Y) <= elevTol)
					{
						floatList.Add(Math.Max(profile[i].Y, profile[i - 1].Y) + elevTol);
					}
					else
					{
						floatList.Add(profile[i].Y);
					}
				}
			}
			floatList.Sort();
			List<float> floatListList = new List<float>(floatList.Count);
			floatListList.Add(floatList.First());
			float scaledElevTol = elevTol * 0.99999f;
			int count2 = floatList.Count - 1;
			for (int j = 1; j <= count2; j++)
			{
				if (floatList[j] > floatListList.Last() + scaledElevTol)
				{
					floatListList.Add(floatList[j]);
				}
			}
			if (floatListList.Last() < floatList.Last())
			{
				floatListList.Add(floatList.Last() + elevTol);
			}
			return floatListList.ToArray();
		}
	}

	private HydraulicProperties[] ComputeProfileHydraulicProperties(Profile profile, float[] elevations)
	{
		checked
		{
			HydraulicProperties[] hydraulicProperties = new HydraulicProperties[elevations.Count() - 1 + 1];
			int count = elevations.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				hydraulicProperties[i].Elevation = elevations[i];
			}
			int count2 = profile.Count - 1;
			for (int j = 1; j <= count2; j++)
			{
				Point2D point2D = profile[j - 1];
				Point2D point = profile[j];
				if (point2D.IsUndefined() || point.IsUndefined())
				{
					continue;
				}
				float x = point2D.X;
				float x2 = point.X;
				float x2Term = x2 - x;
				float computed = (float)((double)x + (double)x2Term * 0.5);
				if (point2D.Y == point.Y)
				{
					float y = point2D.Y;
					int count3 = hydraulicProperties.Count() - 1;
					for (int k = 0; k <= count3; k++)
					{
						float elevation = hydraulicProperties[k].Elevation;
						if (!(elevation < y) && (elevation > y || k == 0))
						{
							float elevationTerm = (elevation - y) * x2Term;
							float computed2 = elevationTerm * computed;
							hydraulicProperties[k].Area += elevationTerm;
							hydraulicProperties[k].TopWidth += x2Term;
							hydraulicProperties[k].WettedPerimeter += x2Term;
							hydraulicProperties[k].CentroidX += computed2;
						}
					}
					continue;
				}
				float y2;
				float y3;
				bool condition;
				if (point2D.Y < point.Y)
				{
					y2 = point2D.Y;
					y3 = point.Y;
					condition = true;
				}
				else
				{
					y2 = point.Y;
					y3 = point2D.Y;
					condition = false;
				}
				float distanceTo = point2D.DistanceTo(point);
				float y3AboveY2 = y3 - y2;
				float y3Y2Term = x2Term * (y3 - y2) * 0.5f;
				int count4 = hydraulicProperties.Count() - 1;
				for (int l = 0; l <= count4; l++)
				{
					float elevation2 = hydraulicProperties[l].Elevation;
					if (elevation2 <= y2)
					{
						continue;
					}
					if (elevation2 >= y3)
					{
						float elevation2Y3Term = (elevation2 - y3) * x2Term;
						hydraulicProperties[l].Area += y3Y2Term + elevation2Y3Term;
						hydraulicProperties[l].TopWidth += x2Term;
						hydraulicProperties[l].WettedPerimeter += distanceTo;
						if (condition)
						{
							hydraulicProperties[l].CentroidX += elevation2Y3Term * computed + y3Y2Term * (x + x2Term / 3f);
						}
						else
						{
							hydraulicProperties[l].CentroidX += elevation2Y3Term * computed + y3Y2Term * (x2 - x2Term / 3f);
						}
						continue;
					}
					float elevation2AboveY2 = elevation2 - y2;
					float elevation2AboveY2OverY3AboveY2 = elevation2AboveY2 / y3AboveY2;
					float computed3 = x2Term * elevation2AboveY2OverY3AboveY2;
					float computed4 = computed3 * elevation2AboveY2 * 0.5f;
					hydraulicProperties[l].Area += computed4;
					hydraulicProperties[l].TopWidth += computed3;
					hydraulicProperties[l].WettedPerimeter += elevation2AboveY2OverY3AboveY2 * distanceTo;
					if (condition)
					{
						hydraulicProperties[l].CentroidX += computed4 * (x + computed3 / 3f);
					}
					else
					{
						hydraulicProperties[l].CentroidX += computed4 * (x2 - computed3 / 3f);
					}
				}
			}
			float centroidX = default;
			for (int m = hydraulicProperties.Count() - 1; m >= 0; m += -1)
			{
				if (hydraulicProperties[m].Area > 0f)
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
	}

	private void EnsureHPInterpolationTolerance(float elevTol, float kTolRatio, HydraulicProperties minHP, HydraulicProperties maxHP, List<HydraulicProperties> hydrProp)
	{
		float conveyance = minHP.Conveyance();
		float conveyance2 = maxHP.Conveyance();
		float elevationDifference = maxHP.Elevation - minHP.Elevation;
		if ((conveyance < conveyance2) & (elevationDifference > elevTol))
		{
			float basek = (float)((double)(conveyance + conveyance2) * 0.5);
			HydraulicProperties maxHP2 = minHP.MidPoint(maxHP, HydraulicProperties.AreaInterpolation.Trapazoidal);
			if (ConveyanceTolRatio(basek, maxHP2.Conveyance()) > kTolRatio)
			{
				EnsureHPInterpolationTolerance(elevTol, kTolRatio, minHP, maxHP2, hydrProp);
			}
		}
		hydrProp.Add(maxHP);
	}

	public float InterpolateConveyance(float elev, HydraulicProperties belowHP, HydraulicProperties aboveHP)
	{
		return new InterpolateProperties(elev, belowHP, aboveHP).InterpolateValue(belowHP.Conveyance(), aboveHP.Conveyance());
	}

	private HydraulicProperties InterpolateHPGeometrically(float elev, HydraulicProperties belowHP, HydraulicProperties aboveHP)
	{
		InterpolateProperties interpolateProperties = new InterpolateProperties(elev, belowHP, aboveHP);
		float interpolateValue = interpolateProperties.InterpolateValue(belowHP.TopWidth, aboveHP.TopWidth);
		float wp = interpolateProperties.InterpolateValue(belowHP.WettedPerimeter, aboveHP.WettedPerimeter);
		float centX = interpolateProperties.InterpolateValue(belowHP.CentroidX, aboveHP.CentroidX);
		float a = (float)((double)(belowHP.Area + belowHP.TopWidth * interpolateProperties.ElevDelta) + (double)((interpolateValue - belowHP.TopWidth) * interpolateProperties.ElevDelta) * 0.5);
		return new HydraulicProperties(elev, a, interpolateValue, wp, centX);
	}

	private float ConveyanceTolRatio(float basek, float compareK)
	{
		return Math.Abs(basek - compareK) / basek;
	}

	private void FilterOnConveyance(float KTolRatio, List<HydraulicProperties> HPs)
	{
		List<int> intList = new List<int>(HPs.Count);
		int ordinal = 0;
		checked
		{
			int countOffset = HPs.Count - 1;
			intList.Add(ordinal);
			intList.Add(countOffset);
			FilterDouglasPeuckerRamer(ordinal, countOffset, KTolRatio, HPs, intList);
			if (intList.Count == HPs.Count)
			{
				return;
			}
			bool[] boolValues = new bool[HPs.Count - 1 + 1];
			foreach (int ordinal2 in intList)
			{
				boolValues[ordinal2] = true;
			}
			int ordinal3 = 0;
			int count = HPs.Count - 1;
			for (int i = 0; i <= count; i++)
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
	}

	private void FilterDouglasPeuckerRamer(int first, int last, float KTolRatio, List<HydraulicProperties> HPs, List<int> keepIndex)
	{
		checked
		{
			if (last - first <= 1)
			{
				return;
			}
			float conveyanceTolRatio = 0f;
			int ordinal = 0;
			int ordinal2 = first + 1;
			int count = last - 1;
			for (int i = ordinal2; i <= count; i++)
			{
				HydraulicProperties hydraulicProperties = HPs[i];
				float compareK = InterpolateConveyance(hydraulicProperties.Elevation, HPs[first], HPs[last]);
				float conveyanceTolRatio2 = ConveyanceTolRatio(hydraulicProperties.Conveyance(), compareK);
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
	}

	public float ComputeCentroidAtArea(float areaFraction)
	{
		float areaTimesAreaFraction = HydrProp.Last().Area * areaFraction;
		if (areaTimesAreaFraction <= HydrProp.First().Area)
		{
			return HydrProp.First().CentroidX;
		}
		int binarySearch = Array.BinarySearch(value: new HydraulicProperties(0f, areaTimesAreaFraction, 0f, 0f, 0f), array: HydrProp, comparer: new AreaComparer());
		if (binarySearch >= 0)
		{
			return HydrProp[binarySearch].CentroidX;
		}
		checked
		{
			if (binarySearch < 0)
			{
				binarySearch = ~binarySearch;
				float submergenceRatio = (areaTimesAreaFraction - HydrProp[binarySearch - 1].Area) / (HydrProp[binarySearch].Area - HydrProp[binarySearch - 1].Area);
				float computed = 1f - submergenceRatio;
				return HydrProp[binarySearch].CentroidX * submergenceRatio + HydrProp[binarySearch - 1].CentroidX * computed;
			}
			return HydrProp.First().CentroidX;
		}
	}

	public void ToImmediateWindow(string title, HydraulicProperties[] HP)
	{
		checked
		{
			int count = HP.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	private void Plot(Profile profile)
	{
	}

	private void Plot(HydraulicProperties[] HD, string title)
	{
	}
}
