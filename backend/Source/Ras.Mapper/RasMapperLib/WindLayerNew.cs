using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.PairedData;
using Geospatial.Rasterizers;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist.VB;
using Hec.Dss;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Progress;
using Utility.Units;

namespace RasMapperLib;

public class WindLayerNew : Layer, IWindLayer, IVelocityLayer
{
	Projection IMeteorologyLayer.OverrideProjection { get => Projection; set => Projection = value; }

	private class Parser
	{
		[CompilerGenerated]
private sealed class _Closure_0024__11_002D0
		{
			public float aRatio;

			public float bRatio;

			public _Closure_0024__11_002D0(_Closure_0024__11_002D0 other)
			{
				if (other != null)
				{
					aRatio = other.aRatio;
					bRatio = other.bRatio;
				}
			}

			[SpecialName]
			internal double _Lambda_0024__3(float s)
			{
				return s * bRatio;
			}
		}

		[CompilerGenerated]
private sealed class _Closure_0024__11_002D1
		{
			public float aUnitFactor;

			public float bUnitFactor;

			public _Closure_0024__11_002D0 outerClosure;

			public _Closure_0024__11_002D1(_Closure_0024__11_002D1 other)
			{
				if (other != null)
				{
					aUnitFactor = other.aUnitFactor;
					bUnitFactor = other.bUnitFactor;
				}
			}

			[SpecialName]
			internal double _Lambda_0024__0(float s)
			{
				return s * outerClosure.aRatio * aUnitFactor;
			}

			[SpecialName]
			internal double _Lambda_0024__1(float s)
			{
				return s * outerClosure.bRatio * bUnitFactor;
			}

			[SpecialName]
			internal double _Lambda_0024__2(float s)
			{
				return s * outerClosure.aRatio * aUnitFactor;
			}
		}

		private const bool LoadConstantAsWindLayerNew = true;

		public static bool TryParseWindLayer(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, ref IWindLayer lyr, ProgressReporter prog = null)
		{
			if (prog == null)
			{
				prog = ProgressReporter.None();
			}
			if (res != null && res.HasBeenRun())
			{
				lyr = new ResultWindLayer(res);
				return true;
			}
			bool tryParseEnabledState = TryParseEnabledState(h5r, "Event Conditions/Meteorology/Wind Direction");
			bool tryParseEnabledState2 = TryParseEnabledState(h5r, "Event Conditions/Meteorology/Wind Speed");
			bool tryParseEnabledState3 = TryParseEnabledState(h5r, "Event Conditions/Meteorology/Wind Velocity X");
			bool tryParseEnabledState4 = TryParseEnabledState(h5r, "Event Conditions/Meteorology/Wind Velocity Y");
			if (tryParseEnabledState && tryParseEnabledState2)
			{
				return TryParseWindLayer_MagDir(h5r, res, metLayer, ref lyr, prog);
			}
			if (tryParseEnabledState3 && tryParseEnabledState4)
			{
				return TryParseWindLayer_VxVy(h5r, res, metLayer, ref lyr, prog);
			}
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) No wind data detected.");
			}
			return true;
		}

		private static bool TryParseWindLayer_VxVy(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Wind Vx/Vy data detected, parsing...");
			}
			float sanitizeRatio = ReadSanitizeRatio(h5r, "Event Conditions/Meteorology/Wind Velocity X");
			if (sanitizeRatio != 1f)
			{
				prog.ReportMessage("Wind Vx is being scaled by " + sanitizeRatio);
			}
			float sanitizeRatio2 = ReadSanitizeRatio(h5r, "Event Conditions/Meteorology/Wind Velocity Y");
			if (sanitizeRatio2 != 1f)
			{
				prog.ReportMessage("Wind Vy is being scaled by " + sanitizeRatio2);
			}
			MeteorologyMode meteorologyMode = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Velocity X", "Mode"));
			MeteorologyMode meteorologyMode2 = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Velocity Y", "Mode"));
			if (meteorologyMode != meteorologyMode2)
			{
				prog.ReportError("Wind inputs were specified with different modes (" + meteorologyMode.ToString() + " and " + meteorologyMode2.ToString() + "), which is not supported at this time.");
				return false;
			}
			switch (meteorologyMode)
			{
			case MeteorologyMode.Constant:
				return TryParseWindLayer_VxVy_Constant(h5r, sanitizeRatio, sanitizeRatio2, ref lyr, prog);
			case MeteorologyMode.Gridded:
				return TryParseWindLayer_VxVy_Gridded(h5r, sanitizeRatio, sanitizeRatio2, ref lyr, prog);
			case MeteorologyMode.Point:
				return TryParseWindLayer_VxVy_Point(h5r, res, metLayer, sanitizeRatio, sanitizeRatio2, ref lyr, prog);
			default:
				prog.ReportError("Could not parse the Wind Mode data - exiting...");
				return false;
			}
		}

		private static bool TryParseWindLayer_MagDir(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Wind Speed/Direction data detected, parsing...");
			}
			float sanitizeRatio = ReadSanitizeRatio(h5r, "Event Conditions/Meteorology/Wind Speed");
			if (sanitizeRatio != 1f)
			{
				prog.ReportMessage("Wind Speed being magnified by a factor of " + sanitizeRatio);
			}
			MeteorologyMode meteorologyMode = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Mode"));
			MeteorologyMode meteorologyMode2 = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Mode"));
			if (meteorologyMode != meteorologyMode2)
			{
				prog.ReportError("Wind inputs were specified with different modes (" + meteorologyMode.ToString() + " and " + meteorologyMode2.ToString() + "), which is not supported at this time.");
				return false;
			}
			switch (meteorologyMode)
			{
			case MeteorologyMode.Constant:
				return TryParseWindLayer_MagDir_Constant(h5r, sanitizeRatio, ref lyr, prog);
			case MeteorologyMode.Gridded:
				return TryParseWindLayer_MagDir_Gridded(h5r, sanitizeRatio, ref lyr, prog);
			case MeteorologyMode.Point:
				return TryParseWindLayer_MagDir_Point(h5r, res, metLayer, sanitizeRatio, ref lyr, prog);
			default:
				prog.ReportError("Could not parse the Wind Mode data - exiting...");
				return false;
			}
		}

		private static bool TryParseWindLayer_VxVy_Constant(H5Reader h5r, float ratioVx, float ratioVy, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Constant Vx/Vy Wind data detected.");
			}
			string units = "";
			string units2 = "";
			float convertToNative = default;
			float convertToNative2 = default;
			if (!TryReadConstantAttributes(h5r, "Event Conditions/Meteorology/Wind Velocity X", ref units, ref convertToNative, prog) || !TryReadConstantAttributes(h5r, "Event Conditions/Meteorology/Wind Velocity Y", ref units2, ref convertToNative2, prog))
			{
				return false;
			}
			if (Operators.CompareString(units, units2, TextCompare: false) != 0)
			{
				prog.ReportError("Wind VX units don't match Wind VY units (" + units + " vs " + units2 + "), which is unsupported at this time.");
				return false;
			}
			convertToNative = ConvertToNative(units, convertToNative, prog);
			convertToNative2 = ConvertToNative(units2, convertToNative2, prog);
			convertToNative *= ratioVx;
			convertToNative2 *= ratioVy;
			lyr = CreateConstantWindLayerVxVy(convertToNative, convertToNative2);
			return true;
		}

		private static bool TryParseWindLayer_MagDir_Constant(H5Reader h5r, float ratioMag, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Constant Mag/Dir Wind data detected.");
			}
			string units = "";
			string units2 = "";
			float computed = default;
			float convertToNative = default;
			if (!TryReadConstantAttributes(h5r, "Event Conditions/Meteorology/Wind Speed", ref units, ref convertToNative, prog) || !TryReadConstantAttributes(h5r, "Event Conditions/Meteorology/Wind Direction", ref units2, ref computed, prog))
			{
				return false;
			}
			convertToNative = ConvertToNative(units, convertToNative, prog);
			convertToNative *= ratioMag;
			lyr = CreateConstantWindLayerMagDir(convertToNative, computed);
			return true;
		}

		private static bool TryParseWindLayer_VxVy_Gridded(H5Reader h5r, float ratioVx, float ratioVy, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind data detected");
			}
			Projection projection = TryGetOverrideProjection(h5r, "Event Conditions/Meteorology/Wind Velocity X", prog);
			if (projection == null)
			{
				projection = TryGetOverrideProjection(h5r, "Event Conditions/Meteorology/Wind Velocity Y", prog);
			}
			if (projection != null)
			{
				prog.ReportWarning("Warning: Wind Override Projection not currently implemented.");
			}
			MeteorologyGriddedSource meteorologyGriddedSource = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Velocity X", "Source"));
			MeteorologyGriddedSource meteorologyGriddedSource2 = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Velocity Y", "Source"));
			if (meteorologyGriddedSource != meteorologyGriddedSource2)
			{
				prog.ReportError("Wind inputs were specified with different sources, which is not supported at this time.");
				return false;
			}
			if (meteorologyGriddedSource == MeteorologyGriddedSource.DSS)
			{
				return TryParseWindLayer_VxVy_Gridded_Dss(h5r, ratioVx, ratioVy, ref lyr, prog);
			}
			return TryParseWindLayer_VxVy_Gridded_Imported(h5r, ratioVx, ratioVy, ref lyr, prog);
		}

		private static bool TryParseWindLayer_MagDir_Gridded(H5Reader h5r, float magRatio, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind data detected");
			}
			Projection projection = TryGetOverrideProjection(h5r, "Event Conditions/Meteorology/Wind Speed", prog);
			if (projection == null)
			{
				projection = TryGetOverrideProjection(h5r, "Event Conditions/Meteorology/Wind Direction", prog);
			}
			if (projection != null)
			{
				prog.ReportWarning("Warning: Wind Override Projection not currently implemented.");
			}
			MeteorologyGriddedSource meteorologyGriddedSource = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Source"));
			MeteorologyGriddedSource meteorologyGriddedSource2 = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Source"));
			if (meteorologyGriddedSource != meteorologyGriddedSource2)
			{
				prog.ReportError("Wind inputs were specified with different gridded sources, which is not supported at this time.");
				return false;
			}
			if (meteorologyGriddedSource == MeteorologyGriddedSource.DSS)
			{
				return TryParseWindLayer_MagDir_Gridded_Dss(h5r, magRatio, ref lyr, prog);
			}
			return TryParseWindLayer_MagDir_Gridded_Imported(h5r, magRatio, ref lyr, prog);
		}

		private static bool TryParseWindLayer_VxVy_Point(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, float vxRatio, float vyRatio, ref IWindLayer lyr, ProgressReporter prog)
		{
			return TryParseWindLayer_VxVyMagDir_Point(h5r, res, metLayer, vxRatio, vyRatio, isVxVy: true, ref lyr, prog);
		}

		private static bool TryParseWindLayer_MagDir_Point(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, float magRatio, ref IWindLayer lyr, ProgressReporter prog)
		{
			return TryParseWindLayer_VxVyMagDir_Point(h5r, res, metLayer, magRatio, 1f, isVxVy: false, ref lyr, prog);
		}

		private static bool TryParseWindLayer_VxVyMagDir_Point(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, float aRatio, float bRatio, bool isVxVy, ref IWindLayer lyr, ProgressReporter prog)
		{
			_Closure_0024__11_002D0 capturedLocals = new _Closure_0024__11_002D0(null);
			capturedLocals.aRatio = aRatio;
			capturedLocals.bRatio = bRatio;
			string path = ((!isVxVy) ? "Speed/Direction" : "Vx/Vy");
			string textValue;
			string textValue2;
			if (isVxVy)
			{
				textValue = "Vx";
				textValue2 = "Vy";
			}
			else
			{
				textValue = "Speed";
				textValue2 = "Direction";
			}
			_ = SharedData.WindSpeedUnits;
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gauged " + path + " Wind data detected");
			}
			DateTime dtOffset = DateTime.MinValue;
			if (res != null)
			{
				dtOffset = res.PlanAttributes.StartTime;
			}
			if (metLayer == null)
			{
				prog.ReportError("Error loading Meteorological Stations");
				return false;
			}
			if (metLayer.FeatureCount() == 0)
			{
				prog.ReportError("Error: Gauged " + path + " Wind selected, but no gauges have been added.");
				return false;
			}
			List<string> stringList;
			List<string> stringListList;
			if (isVxVy)
			{
				stringList = metLayer.GetWindVelocityXDatasetNames();
				stringListList = metLayer.GetWindVelocityYDatasetNames();
			}
			else
			{
				stringList = metLayer.GetWindSpeedDatasetNames();
				stringListList = metLayer.GetWindDirectionDatasetNames();
			}
			List<string> stringList2 = new List<string>();
			List<string> stringList3 = new List<string>();
			List<PointM> pointMs = new List<PointM>();
			List<string> stringList4 = new List<string>();
			checked
			{
				int count = metLayer.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					string featureName = metLayer.GetFeatureName(i);
					string textValue3 = stringList[i];
					string textValue4 = stringListList[i];
					bool existsAsDataset = h5r.ExistsAsDataset(textValue3);
					bool existsAsDataset2 = h5r.ExistsAsDataset(textValue4);
					if (existsAsDataset && existsAsDataset2)
					{
						stringList2.Add(textValue3);
						stringList3.Add(textValue4);
						pointMs.Add(metLayer.Point(i).PointM());
						stringList4.Add(featureName);
						continue;
					}
					if (existsAsDataset)
					{
						prog.ReportError("Error: Gauge " + featureName.InDoubleQuotes() + " has Wind " + textValue + " data, but no " + textValue2 + " data specified.");
						return false;
					}
					if (existsAsDataset2)
					{
						prog.ReportError("Error: Gauge " + featureName.InDoubleQuotes() + " has Wind " + textValue2 + " data, but no " + textValue + " data specified.");
						return false;
					}
				}
				List<MetTimeSeries> tryParsePointTimeSeries = MetParser.TryParsePointTimeSeries(h5r, stringList2, prog);
				List<MetTimeSeries> tryParsePointTimeSeriesList = MetParser.TryParsePointTimeSeries(h5r, stringList3, prog);
				if (tryParsePointTimeSeries == null || tryParsePointTimeSeriesList == null || tryParsePointTimeSeries.Count != tryParsePointTimeSeriesList.Count)
				{
					prog.ReportError("Error loading Wind " + path + " time-series data.");
					return false;
				}
				List<IDiscreteVelocityTimeSeries> iDiscreteVelocityTimeSeries = new List<IDiscreteVelocityTimeSeries>();
				int count2 = tryParsePointTimeSeries.Count - 1;
				_Closure_0024__11_002D1 capturedLocals2 = default;
				for (int j = 0; j <= count2; j++)
				{
					capturedLocals2 = new _Closure_0024__11_002D1(capturedLocals2);
					capturedLocals2.outerClosure = capturedLocals;
					MetTimeSeries metTimeSeries = tryParsePointTimeSeries[j];
					MetTimeSeries metTimeSeries2 = tryParsePointTimeSeriesList[j];
					if (metTimeSeries == null || metTimeSeries2 == null)
					{
						prog.ReportError("Could not load data for gauge " + stringList4[j]);
						return false;
					}
					List<float> values = null;
					List<DateTime> times = null;
					metTimeSeries.GetCompleteTimeSeries(dtOffset, ref values, ref times);
					List<float> values2 = null;
					List<DateTime> times2 = null;
					metTimeSeries2.GetCompleteTimeSeries(dtOffset, ref values2, ref times2);
					if (values == null || times == null || values2 == null || times2 == null)
					{
						prog.ReportError("Could not load any data for gauge " + stringList4[j]);
						return false;
					}
					if (times.Count != times2.Count)
					{
						prog.ReportError("Gauge " + stringList4[j].InDoubleQuotes() + " has a different number of entries for Wind " + path + ", which is not currently supported.");
						return false;
					}
					if (times.Count == 0)
					{
						prog.ReportError("Gauge " + stringList4[j] + " has no valid Wind data.");
						return false;
					}
					capturedLocals2.aUnitFactor = 1f;
					capturedLocals2.bUnitFactor = 1f;
					if (isVxVy)
					{
						capturedLocals2.aUnitFactor = ConvertToNative(metTimeSeries.UnitString, 1f, prog);
						capturedLocals2.bUnitFactor = ConvertToNative(metTimeSeries2.UnitString, 1f, prog);
					}
					else
					{
						capturedLocals2.aUnitFactor = ConvertToNative(metTimeSeries.UnitString, 1f, prog);
					}
					if (isVxVy)
					{
						iDiscreteVelocityTimeSeries.Add(new VelocityTimeSeries(times, values.Select(capturedLocals2._Lambda_0024__0).ToArray(), values2.Select(capturedLocals2._Lambda_0024__1).ToArray()));
						continue;
					}
					iDiscreteVelocityTimeSeries.Add(VelocityTimeSeries.FromMagnitudeDirection(times, values.Select(capturedLocals2._Lambda_0024__2).ToArray(), ((IEnumerable<float>)values2).Select((Func<float, double>)([SpecialName] (float s) => s * capturedLocals2.outerClosure.bRatio)).ToArray(), DirectionConvention.ComingFrom));
				}
				List<Geospatial.Vectors.Point> gaugeLocations = Converter.Convert(pointMs);
				new GaugedVelocityResampler(gaugeLocations, iDiscreteVelocityTimeSeries, new TinInterpolator(), -9999f);
				if (metLayer.DefaultMetRasterization == null)
				{
					prog.ReportError("Met station layer does not have a valid default rasterization.");
					return false;
				}
				return false;
			}
		}

		private static bool TryParseWindLayer_VxVy_Gridded_Dss(H5Reader h5r, float ratioVx, float ratioVy, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind source is DSS.");
			}
			string dssFilename = "";
			string dssPath = "";
			if (!TryParseDssFilePath(h5r, "Event Conditions/Meteorology/Wind Velocity X", ref dssFilename, ref dssPath, prog))
			{
				return false;
			}
			string dssFilename2 = "";
			string dssPath2 = "";
			if (!TryParseDssFilePath(h5r, "Event Conditions/Meteorology/Wind Velocity Y", ref dssFilename2, ref dssPath2, prog))
			{
				return false;
			}
			DssTimeSeriesRasterDataSource dssTimeSeriesRasterDataSource = new DssTimeSeriesRasterDataSource(dssFilename, dssPath, keepOpen: false);
			DssTimeSeriesRasterDataSource dssTimeSeriesRasterDataSource2 = new DssTimeSeriesRasterDataSource(dssFilename2, dssPath2, keepOpen: false);
			if (dssTimeSeriesRasterDataSource.ProfileCount == 0)
			{
				prog.ReportError("Wind data error, could not load any data for " + dssFilename + ": " + dssPath);
				return false;
			}
			if (dssTimeSeriesRasterDataSource2.ProfileCount == 0)
			{
				prog.ReportError("Wind data error, could not load any data for " + dssFilename2 + ": " + dssPath2);
				return false;
			}
			if (dssTimeSeriesRasterDataSource.ProfileCount != dssTimeSeriesRasterDataSource2.ProfileCount)
			{
				prog.ReportError("Wind data error, Vx/Vy data has different number of bands. VX: " + dssTimeSeriesRasterDataSource.ProfileCount + ", VY: " + dssTimeSeriesRasterDataSource2.ProfileCount);
				return false;
			}
			float convertToNative = ConvertToNative(dssTimeSeriesRasterDataSource.GetGrid(0).Units, 1f, prog);
			ratioVx *= convertToNative;
			ratioVy *= convertToNative;
			return false;
		}

		private static bool TryParseWindLayer_VxVy_Gridded_Imported(H5Reader h5r, float ratioVx, float ratioVy, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind source is Imported/HDF5.");
			}
			ProgressReporter.DebugPrint();
			prog.ReportError("Error reading imported Wind Vx/Vy data. Code removed.");
			return false;
		}

		private static Projection LoadProjection(H5Reader h5r, string folder, ProgressReporter prog)
		{
			string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(folder, "Projection");
			if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				try
				{
					WKTProjection result = new WKTProjection(tryReadStringAttributeScalar);
					if (SharedData.SRSProjection != null)
					{
						return result;
					}
					prog.ReportMessage("No RasMapper projection set, treating Wind data as native projection.");
				}
				catch (Exception projectError)
				{
					ProjectData.SetProjectError(projectError);
					prog.ReportError("Error parsing projection string " + tryReadStringAttributeScalar.InDoubleQuotes() + " (Treating as native projection)");
					ProjectData.ClearProjectError();
				}
			}
			else if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) No Wind projection set for imported data, treating as native.");
			}
			return null;
		}

		private static bool TryParseWindLayer_MagDir_Gridded_Dss(H5Reader h5r, float magRatio, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind source is DSS.");
			}
			string dssFilename = "";
			string dssPath = "";
			if (!TryParseDssFilePath(h5r, "Event Conditions/Meteorology/Wind Speed", ref dssFilename, ref dssPath, prog))
			{
				return false;
			}
			string dssFilename2 = "";
			string dssPath2 = "";
			if (!TryParseDssFilePath(h5r, "Event Conditions/Meteorology/Wind Direction", ref dssFilename2, ref dssPath2, prog))
			{
				return false;
			}
			DssTimeSeriesRasterDataSource dssTimeSeriesRasterDataSource = new DssTimeSeriesRasterDataSource(dssFilename, dssPath, keepOpen: false);
			DssTimeSeriesRasterDataSource dssTimeSeriesRasterDataSource2 = new DssTimeSeriesRasterDataSource(dssFilename2, dssPath2, keepOpen: false);
			if (dssTimeSeriesRasterDataSource.ProfileCount == 0)
			{
				prog.ReportError("Wind data error, could not load any data for " + dssFilename + ": " + dssPath);
				return false;
			}
			if (dssTimeSeriesRasterDataSource2.ProfileCount == 0)
			{
				prog.ReportError("Wind data error, could not load any data for " + dssFilename2 + ": " + dssPath2);
				return false;
			}
			if (dssTimeSeriesRasterDataSource.ProfileCount != dssTimeSeriesRasterDataSource2.ProfileCount)
			{
				prog.ReportError("Wind data error, Magnitude/Direction data has different number of bands. Magnitude: " + dssTimeSeriesRasterDataSource.ProfileCount + ", Direction: " + dssTimeSeriesRasterDataSource2.ProfileCount);
				return false;
			}
			int ordinal = 0;
			dssTimeSeriesRasterDataSource.UseReader([SpecialName] (DssReader dssr) =>
			{
				ordinal = dssr.GetDSSFileVersion();
			});
			return false;
		}

		private static bool TryParseDssFilePath(H5Reader h5r, string groupname, ref string dssFilename, ref string dssPath, ProgressReporter prog)
		{
			dssFilename = h5r.TryReadStringAttributeScalar(groupname, "DSS Filename");
			dssFilename = RasMapperLib.Utilities.Path.MakeAbsolute(dssFilename, SharedData.RasMapFilename);
			if (!File.Exists(dssFilename))
			{
				prog.ReportError("DSS File " + dssFilename.InDoubleQuotes() + " not found.");
				return false;
			}
			dssPath = h5r.TryReadStringAttributeScalar(groupname, "DSS Pathname");
			if (string.IsNullOrEmpty(dssPath))
			{
				prog.ReportError("'DSS Pathname' attribute is missing from " + groupname);
				return false;
			}
			return true;
		}

		private static bool TryParseWindLayer_MagDir_Gridded_Imported(H5Reader h5r, float magRatio, ref IWindLayer lyr, ProgressReporter prog)
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind source is Imported/HDF5.");
			}
			prog.ReportError("Error reading imported Wind Speed/Direction data.");
			return false;
		}

		private static bool TryReadConstantAttributes(H5Reader h5r, string group, ref string units, ref float computed, ProgressReporter prog)
		{
			if (!h5r.TryReadAttributeScalar(group, "Constant Value", ref computed) || !h5r.TryReadAttributeScalar(group, "Constant Units", ref units))
			{
				prog.ReportError("Missing required attributes " + "Constant Value".InDoubleQuotes() + " and/or " + "Constant Units".InDoubleQuotes());
				return false;
			}
			return true;
		}

		private static float ConvertToNative(string vxUnits, float vxValue, ProgressReporter prog)
		{
			WindSpeed windSpeed = WindSpeed.TryParse(vxUnits);
			if (windSpeed == null)
			{
				prog.ReportWarning("Wind layer could not parse unit string " + vxUnits.InDoubleQuotes() + ". Length/Second will be assumed based on project USC/SI settings.");
			}
			else if (!windSpeed.Equals(SharedData.WindSpeedUnits))
			{
				float convertTo = (float)windSpeed.ConvertTo(SharedData.WindSpeedUnits, 1.0);
				vxValue *= convertTo;
				if (SharedData.DebugMode)
				{
					prog.ReportMessage("DEBUG: Wind dimension converting from " + windSpeed.Name + " to " + SharedData.WindSpeedUnits.Name + ": Factor of " + convertTo);
				}
			}
			return vxValue;
		}

		public static WindLayerNew CreateConstantWindLayerVxVy(float vx, float vy)
		{
			return null;
		}

		public static WindLayerNew CreateConstantWindLayerMagDir(float mag, float dir)
		{
			return null;
		}

		private static bool TryParseEnabledState(H5Reader h5r, string groupName)
		{
			if (!h5r.ExistsAsGroup(groupName))
			{
				return false;
			}
			bool condition = true;
			if (h5r.TryReadAttributeScalar(groupName, "Enabled", ref condition))
			{
				return condition;
			}
			if (h5r.TryReadStringAttributeScalar(groupName, "Enabled").RoughlyEquals("False"))
			{
				return false;
			}
			return true;
		}

		private static Projection TryGetOverrideProjection(H5Reader h5r, string group, ProgressReporter prog)
		{
			string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar(group, "Projection Override");
			if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				return null;
			}
			ESRIProjection result = null;
			if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				if (File.Exists(tryReadStringAttributeScalar))
				{
					try
					{
						result = new ESRIProjection(tryReadStringAttributeScalar);
						prog.ReportMessage("Override projection for Wind layer detected, using " + tryReadStringAttributeScalar);
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						result = null;
						prog.ReportError("Override projection for Wind layer detected, but had an error: " + tryReadStringAttributeScalar + ", " + ex2.Message);
						ProjectData.ClearProjectError();
					}
				}
				else
				{
					prog.ReportError("Override projection for Wind layer detected, but file does not exist: " + tryReadStringAttributeScalar);
				}
			}
			return result;
		}
	}

	public const string HDFWindGroupName = "Event Conditions/Meteorology/Wind";

	public const string HDFWindDirectionGroupName = "Event Conditions/Meteorology/Wind Direction";

	public const string HDFWindSpeedGroupName = "Event Conditions/Meteorology/Wind Speed";

	public const string HDFWindVxGroupName = "Event Conditions/Meteorology/Wind Velocity X";

	public const string HDFWindVyGroupName = "Event Conditions/Meteorology/Wind Velocity Y";

	public const string sWindSpeed = "Wind Speed";

	public const string sWindDirection = "Wind Direction";

	public const string sWindVx = "Wind Velocity X";

	public const string sWindVy = "Wind Velocity Y";

	private Projection _windOverrideProj;

	private int _currentBandIndex;

	private readonly List<string> _profiles = new List<string>();

	public override Extent Extent
	{
		get
		{
			if (_extent != null)
			{
				return _extent;
			}
			return Extent.NullExtent;
		}
	}

	public Projection Projection
	{
		get
		{
			return _windOverrideProj;
		}
		set
		{
			_windOverrideProj = value;
		}
	}

	public float Ratio { get; set; }

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile
	{
		get
		{
			if (_currentBandIndex >= 0)
			{
				return _currentBandIndex < ProfileCount;
			}
			return false;
		}
	}

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile => !IsProfile;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentBandIndex;
		}
		set
		{
			_currentBandIndex = value;
		}
	}

	public string CurrentProfileName
	{
		get
		{
			if (IsNullProfile)
			{
				return "None";
			}
			return _profiles[CurrentProfileIndex];
		}
	}

	public IList<string> AvailableProfiles => _profiles;

	public int ProfileCount
	{
		get
		{
			if (AvailableProfiles == null)
			{
				return 0;
			}
			return AvailableProfiles.Count;
		}
	}

	public InterpolationMode RasterInterpMode { get; set; }

	public WindLayerNew()
	{
		Ratio = 1f;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		RasterInterpMode = InterpolationMode.Nearest;
	}

	public bool GetWindTimeSeries(PointM pt, ref float[] magnitudes, ref float[] directions, ref DateTime[] times)
	{
		return false;
	}

	bool IWindLayer.GetWindTimeSeries(PointM pt,ref float[] magnitudes,ref float[] directions,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWindTimeSeries
		return this.GetWindTimeSeries(pt, ref magnitudes, ref directions, ref times);
	}

	public bool GetTimeSeries(PointM pt, ref float[] values, ref DateTime[] times)
	{
		float[] directions = null;
		return GetWindTimeSeries(pt, ref values, ref directions, ref times);
	}

	bool IMeteorologyLayer.GetTimeSeries(PointM pt,ref float[] values,ref DateTime[] times)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetTimeSeries
		return this.GetTimeSeries(pt, ref values, ref times);
	}

	public void ResampleVelocities(int profileIndex, RasterM raster, ref float[] vx, ref float[] vy, [Optional][DefaultParameterValue(null)] ref float[] velMag)
	{
	}

	public static void ConvertToVxVy(float[] mag, float[] dir, ref float[] vx, ref float[] vy)
	{
		Buffers.ArraySetAllVB(ref vx, -9999f, mag.Length);
		Buffers.ArraySetAllVB(ref vy, -9999f, mag.Length);
		checked
		{
			int count = mag.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				float mag2 = mag[i];
				float dir2 = dir[i];
				if (mag2 != -9999f && dir2 != -9999f)
				{
					vx[i] = (float)(System.Math.Sin((double)dir2 * 0.0174533) * (double)(0f - mag2));
					vy[i] = (float)(System.Math.Cos((double)dir2 * 0.0174533) * (double)(0f - mag2));
				}
			}
		}
	}

	public static void ConvertToVxVy(float mag, float dir, ref float vx, ref float vy)
	{
		if (mag == -9999f || dir == -9999f)
		{
			vx = -9999f;
			vy = -9999f;
		}
		else
		{
			vx = (float)(System.Math.Sin((double)dir * 0.0174533) * (double)(0f - mag));
			vy = (float)(System.Math.Cos((double)dir * 0.0174533) * (double)(0f - mag));
		}
	}

	public static void ConvertToMagDir(float vx, float vy, ref float mag, ref float dir)
	{
		if (vx == -9999f || vy == -9999f)
		{
			mag = -9999f;
			dir = -9999f;
			return;
		}
		SegmentM segmentM = new SegmentM(0.0, 0.0, vx, vy);
		mag = (float)segmentM.Length;
		double dir2 = segmentM.Bearing();
		dir2 = 360.0 - dir2;
		dir2 -= 90.0;
		if (dir2 < 0.0)
		{
			dir2 += 360.0;
		}
		if (dir2 >= 360.0)
		{
			dir2 -= 360.0;
		}
		dir = (float)dir2;
	}

	public static void EnsureClean(H5Writer h5w)
	{
		if (h5w.ExistsAsGroup("Event Conditions/Meteorology/Wind Direction"))
		{
			h5w.Delete("Event Conditions/Meteorology/Wind Direction");
		}
		if (h5w.ExistsAsGroup("Event Conditions/Meteorology/Wind Speed"))
		{
			h5w.Delete("Event Conditions/Meteorology/Wind Speed");
		}
	}

	private static float ReadSanitizeRatio(H5Reader hr, string grpName)
	{
		if (!hr.ExistsAsGroup(grpName))
		{
			return 1f;
		}
		float tryReadSingleAttributeScalar = hr.TryReadSingleAttributeScalar(grpName, "Ratio");
		if (float.IsNaN(tryReadSingleAttributeScalar))
		{
			tryReadSingleAttributeScalar = 1f;
		}
		return tryReadSingleAttributeScalar;
	}

	public static bool TryParseWindLayer(H5Reader h5r, RASResults res, MeteorologicalStationLayer metLayer, ref IWindLayer lyr, ProgressReporter prog)
	{
		return Parser.TryParseWindLayer(h5r, res, metLayer, ref lyr, prog);
	}

	private static List<DateTime> SanitizeDateTimes(List<DateTime> dts, RASResults res)
	{
		if (dts.Count == 2 && DateTime.Compare(dts[0], DateTime.MinValue) == 0 && DateTime.Compare(dts[1], DateTime.MaxValue) == 0)
		{
			return new List<DateTime>
			{
				res.PlanAttributes.StartTime,
				res.PlanAttributes.EndTime
			};
		}
		return dts;
	}

	public bool RasterizeSaveData(string hdfFilename, string windFolder, RASResults res, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		RASGeometry geometry = res.Geometry;
		if (res == null || geometry == null)
		{
			prog.ReportError("Error: Result failed to load correctly.");
			return false;
		}
		prog.ReportError("Wind code removed");
		return false;
	}

	bool IMeteorologyLayer.RasterizeSaveData(string hdfFilename,string windFolder,RASResults res,ProgressReporter prog )
	{
		//ILSpy generated this explicit interface implementation from .override directive in RasterizeSaveData
		return this.RasterizeSaveData(hdfFilename, windFolder, res, prog);
	}

	public bool ReadNativeRaster(int profileIndex, ref float[] vx, ref float[] vy)
	{
		return false;
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNode xmlNode = xmlLayer.SelectSingleNode("Metadata");
		if (xmlNode != null)
		{
			_currentBandIndex = XML.IntegerAttribute(xmlNode, "BandIndex", -1);
		}
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		XmlElement xmlElement = doc.CreateElement("Metadata");
		xmlLayer.AppendChild(xmlElement);
		xmlElement.SetAttribute("BandIndex", _currentBandIndex.ToString());
	}

	public void TrySetProfile(string profile)
	{
		checked
		{
			int count = _profiles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (profile.RoughlyEquals(_profiles[i]))
				{
					CurrentProfileIndex = i;
					break;
				}
			}
		}
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		throw new NotImplementedException();
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		throw new NotImplementedException();
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}

	public void AlertTimeWindowChanged(DateTime startDt, DateTime endDt)
	{
	}

	void IMeteorologyLayer.AlertTimeWindowChanged(DateTime startDt,DateTime endDt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in AlertTimeWindowChanged
		this.AlertTimeWindowChanged(startDt, endDt);
	}
}
