using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Progress;
using Utility.Units;

namespace RasMapperLib;

public class WindLayer : Layer, IWindLayer, IVelocityLayer
{
	Projection IMeteorologyLayer.OverrideProjection { get => Projection; set => Projection = value; }

	public const string HDFWindGroupName = "Event Conditions/Meteorology/Wind";

	public const string HDFWindDirectionGroupName = "Event Conditions/Meteorology/Wind Direction";

	public const string HDFWindSpeedGroupName = "Event Conditions/Meteorology/Wind Speed";

	public const string HDFWindVxGroupName = "Event Conditions/Meteorology/Wind Velocity X";

	public const string HDFWindVyGroupName = "Event Conditions/Meteorology/Wind Velocity Y";

	public const string sWindSpeed = "Wind Speed";

	public const string sWindDirection = "Wind Direction";

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

	public WindLayer()
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
		if (h5w.ExistsAsGroup("Event Conditions/Meteorology/Wind Velocity X"))
		{
			h5w.Delete("Event Conditions/Meteorology/Wind Velocity X");
		}
		if (h5w.ExistsAsGroup("Event Conditions/Meteorology/Wind Velocity Y"))
		{
			h5w.Delete("Event Conditions/Meteorology/Wind Velocity Y");
		}
	}

	public static bool TryParseWindLayer(H5Reader h5r, RASResults res, ref IWindLayer lyr, ProgressReporter prog)
	{
		bool resHasBeenRunTerm = res?.HasBeenRun() ?? false;
		lyr = null;
		bool existsAsGroup = h5r.ExistsAsGroup("Event Conditions/Meteorology/Wind Direction");
		bool existsAsGroup2 = h5r.ExistsAsGroup("Event Conditions/Meteorology/Wind Speed");
		if (!existsAsGroup && !existsAsGroup2)
		{
			return true;
		}
		bool condition = true;
		if (h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Enabled", ref condition))
		{
			if (!condition)
			{
				return true;
			}
		}
		else if (h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Enabled").RoughlyEquals("False"))
		{
			return true;
		}
		if (h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Enabled", ref condition))
		{
			if (!condition)
			{
				return true;
			}
		}
		else if (h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Enabled").RoughlyEquals("False"))
		{
			return true;
		}
		if (!existsAsGroup)
		{
			prog.ReportError("Wind layer has a direction dataset without a speed dataset, could not use.");
			return false;
		}
		if (!existsAsGroup2)
		{
			prog.ReportError("Wind layer has a speed dataset without a direction dataset, could not use.");
			return false;
		}
		if (SharedData.DebugMode)
		{
			prog.ReportMessage("(DEBUG) Wind data detected, parsing...");
		}
		if (resHasBeenRunTerm)
		{
			lyr = new ResultWindLayer(res);
			return true;
		}
		float ratio = h5r.TryReadSingleAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Ratio");
		if (float.IsNaN(ratio))
		{
			ratio = 1f;
		}
		if (ratio != 1f)
		{
			prog.ReportMessage("Wind Speed being magnified by a factor of " + ratio);
		}
		MeteorologyMode meteorologyMode = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Mode"));
		MeteorologyMode meteorologyMode2 = MetParser.ParseMeteorologyMode(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Mode"));
		if (meteorologyMode != meteorologyMode2)
		{
			prog.ReportError("Wind and direction were specified with different modes, which is not supported at this time.");
			return false;
		}
		switch (meteorologyMode)
		{
		case MeteorologyMode.Constant:
		{
			string textValue = "";
			string textValue2 = "";
			float computed = default;
			float computed2 = default;
			if (!h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Constant Value", ref computed) || !h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Constant Value", ref computed2) || !h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Constant Units", ref textValue) || !h5r.TryReadAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Constant Units", ref textValue2))
			{
				prog.ReportError("Wind layer with mode \"Constant\" requires the attributes " + "Constant Value".InDoubleQuotes() + " and " + "Constant Units".InDoubleQuotes() + ", which are missing.");
				return false;
			}
			WindSpeed windSpeed = WindSpeed.TryParse(textValue);
			if (windSpeed == null)
			{
				prog.ReportWarning("Wind layer could not parse unit string " + textValue.InDoubleQuotes() + ". Length/Second will be assumed based on project USC/SI settings.");
			}
			else if (!windSpeed.Equals(SharedData.WindSpeedUnits))
			{
				computed *= (float)windSpeed.ConvertTo(SharedData.WindSpeedUnits, 1.0);
			}
			float vx = default;
			float vy = default;
			ConvertToVxVy(computed, computed2, ref vx, ref vy);
			lyr = new ConstantWindLayer("Constant Wind Layer", vx, vy);
			lyr.Ratio = ratio;
			return true;
		}
		case MeteorologyMode.Gridded:
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Gridded Wind data detected");
			}
			string tryReadStringAttributeScalar = h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Projection Override");
			string tryReadStringAttributeScalar2 = h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Projection Override");
			string tryReadStringAttributeScalar3 = tryReadStringAttributeScalar;
			if (string.IsNullOrEmpty(tryReadStringAttributeScalar3))
			{
				tryReadStringAttributeScalar3 = tryReadStringAttributeScalar2;
			}
			ESRIProjection eSRIProjection = null;
			if (!string.IsNullOrEmpty(tryReadStringAttributeScalar3))
			{
				if (File.Exists(tryReadStringAttributeScalar3))
				{
					try
					{
						eSRIProjection = new ESRIProjection(tryReadStringAttributeScalar3);
						prog.ReportMessage("Override projection for Wind layer detected, using " + tryReadStringAttributeScalar3);
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						eSRIProjection = null;
						prog.ReportError("Override projection for Wind layer detected, but had an error: " + tryReadStringAttributeScalar3 + ", " + ex2.Message);
						ProjectData.ClearProjectError();
					}
				}
				else
				{
					prog.ReportError("Override projection for Wind layer detected, but file does not exist: " + tryReadStringAttributeScalar3);
				}
			}
			MeteorologyGriddedSource meteorologyGriddedSource = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "Source"));
			MeteorologyGriddedSource meteorologyGriddedSource2 = MetParser.ParseMeteorologyGriddedSource(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Speed", "Source"));
			if (meteorologyGriddedSource != meteorologyGriddedSource2)
			{
				prog.ReportError("Wind and direction were specified with different source file(s), which is not supported at this time.");
				return false;
			}
			if (meteorologyGriddedSource == MeteorologyGriddedSource.DSS)
			{
				string filename = h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "DSS Filename");
				filename = RasMapperLib.Utilities.Path.MakeAbsolute(filename, SharedData.RasMapFilename);
				if (!File.Exists(filename))
				{
					prog.ReportError("Wind filename " + filename.InDoubleQuotes() + " not found.");
					return false;
				}
				if (string.IsNullOrEmpty(h5r.TryReadStringAttributeScalar("Event Conditions/Meteorology/Wind Direction", "DSS Pathname")))
				{
					prog.ReportError("Wind pathname attribute is missing.");
					return false;
				}
				prog.ReportError("DSS Wind files not yet supported");
				return false;
			}
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("(DEBUG) Using imported Wind data.");
			}
			prog.ReportError("Code removed");
			return false;
		}
		case MeteorologyMode.Point:
			prog.ReportError("Point wind field not currently supported.");
			return false;
		default:
			prog.ReportError("Could not parse the WindMode data - exiting...");
			return false;
		}
	}

	public bool RasterizeSaveData(string hdfFilename, string windFolder, RASResults res, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
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
