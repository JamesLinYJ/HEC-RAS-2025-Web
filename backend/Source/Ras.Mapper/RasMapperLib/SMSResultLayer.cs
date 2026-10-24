using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Render;
using Utility.Extensions;

namespace RasMapperLib;

public class SMSResultLayer : TinFeatureLayer, IAnimatingLayer
{
	public const string WaterSurfaceFilename = "fort63.h5";

	public const string VelocityFilename = "fort64.h5";

	private const string WaterSurfaceTimesDataset = "Datasets/Water Surface Elevation (63)/Times";

	private const string WaterSurfaceValuesDataset = "Datasets/Water Surface Elevation (63)/Values";

	private const string WaterSurfaceMaxDataset = "Datasets/Water Surface Elevation (63)/Maxs";

	private const string WaterSurfaceMinDataset = "Datasets/Water Surface Elevation (63)/Mins";

	private const string VelocityMagTimesDataset = "Datasets/Depth-averaged Velocity (64) mag/Times";

	private const string VelocityMagValuesDataset = "Datasets/Depth-averaged Velocity (64) mag/Values";

	private const string VelocityMagMaxDataset = "Datasets/Depth-averaged Velocity (64) mag/Maxs";

	private const string VelocityMagMinDataset = "Datasets/Depth-averaged Velocity (64) mag/Mins";

	private const string VelocityTimesDataset = "Datasets/Depth-averaged Velocity (64)/Times";

	private const string VelocityValuesDataset = "Datasets/Depth-averaged Velocity (64)/Values";

	private const string VelocityMaxDataset = "Datasets/Depth-averaged Velocity (64)/Maxs";

	private const string VelocityMinDataset = "Datasets/Depth-averaged Velocity (64)/Mins";

	private List<string> _availableProfiles;

	private int _currentProfileIndex;

	private SMSFeatureLayer _smsMeshDefinition;

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile
	{
		get
		{
			if (_currentProfileIndex >= 0)
			{
				return _currentProfileIndex < AvailableProfiles.Count;
			}
			return false;
		}
	}

	public bool IsMinProfile => _currentProfileIndex == int.MinValue;

	public bool IsMaxProfile => _currentProfileIndex == int.MaxValue;

	public bool IsNullProfile => _currentProfileIndex == -1;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentProfileIndex;
		}
		set
		{
			if (value >= 0 && value < AvailableProfiles.Count)
			{
				_currentProfileIndex = value;
			}
			else if (value == int.MinValue || value == int.MaxValue)
			{
				_currentProfileIndex = value;
			}
			else
			{
				_currentProfileIndex = -1;
			}
		}
	}

	public string CurrentProfileName
	{
		get
		{
			if (_currentProfileIndex == int.MinValue)
			{
				return "Min";
			}
			if (_currentProfileIndex == int.MaxValue)
			{
				return "Max";
			}
			if (_currentProfileIndex >= 0 && _currentProfileIndex < AvailableProfiles.Count)
			{
				return "";
			}
			return "None";
		}
	}

	public IList<string> AvailableProfiles => _availableProfiles;

	public static SMSResultLayer CreateVelocityLayer(SMSFeatureLayer smsLayer, RASResults correspondingResult, int meshIdx, string guid)
	{
		string fortH5Filename = Path.Combine(Path.GetDirectoryName(smsLayer.SourceFilename), "fort64.h5");
		SMSResultLayer sMSResultLayer = new SMSResultLayer(smsLayer, fortH5Filename);
		sMSResultLayer.SaveToSMSResultFile(correspondingResult, meshIdx, guid);
		return sMSResultLayer;
	}

	public static SMSResultLayer CreateWaterSurfaceLayer(SMSFeatureLayer smsLayer, RASResults correspondingResult, int meshIdx, string guid)
	{
		string fortH5Filename = Path.Combine(Path.GetDirectoryName(smsLayer.SourceFilename), "fort63.h5");
		SMSResultLayer sMSResultLayer = new SMSResultLayer(smsLayer, fortH5Filename);
		sMSResultLayer.SaveToSMSResultFile(correspondingResult, meshIdx, guid);
		return sMSResultLayer;
	}

	public SMSResultLayer(SMSFeatureLayer smsLayer, string fortH5Filename)
		: base(WaterSurfaceOrVelocityFile(fortH5Filename), fortH5Filename)
	{
		_currentProfileIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		_smsMeshDefinition = smsLayer;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAutoNameColumn();
	}

	protected override void LoadFeatureTable()
	{
		if (_smsMeshDefinition != null && _smsMeshDefinition.FeatureCount() != 0)
		{
			AddFeature(_smsMeshDefinition.Tin(0).Copy());
			SetFeatureName(0, _smsMeshDefinition.GetFeatureName(0));
			EnsureFileInfo();
		}
	}

	private void EnsureFileInfo()
	{
		if (_availableProfiles == null)
		{
			_availableProfiles = ProcessFileInfo();
			if (_availableProfiles == null)
			{
				ErrorMessage = "Could not parse " + SourceFilename;
			}
		}
	}

	private List<string> ProcessFileInfo()
	{
		List<string> result;
		double[] values;
		if (!File.Exists(SourceFilename))
		{
			result = null;
		}
		else
		{
			values = null;
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (IsWaterSurfaceFile(SourceFilename))
				{
					if (h5Reader.ExistsAsDataset("Datasets/Water Surface Elevation (63)/Times"))
					{
						h5Reader.ReadDataset("Datasets/Water Surface Elevation (63)/Times", ref values);
						goto IL_00a2;
					}
					result = null;
				}
				else if (IsVelocityFile(SourceFilename))
				{
					if (h5Reader.ExistsAsDataset("Datasets/Depth-averaged Velocity (64)/Times"))
					{
						h5Reader.ReadDataset("Datasets/Depth-averaged Velocity (64)/Times", ref values);
						goto IL_00a2;
					}
					result = null;
				}
				else
				{
					result = null;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		goto IL_00df;
		IL_00df:
		return result;
		IL_00a2:
		_currentProfileIndex = 0;
		List<string> stringList = new List<string>();
		checked
		{
			int count = values.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				stringList.Add(values[i].ToString());
			}
			result = stringList;
			goto IL_00df;
		}
	}

	public bool SaveToSMSResultFile(RASResults correspondingResult, int meshIdx, string guid)
	{
		_ = correspondingResult.Geometry.D2FlowArea;
		TerrainLayer terrain = correspondingResult.Geometry.Terrain;
		if (terrain == null || !terrain.AllSourceFilesExist())
		{
			return false;
		}
		if (FeatureCount() != 1)
		{
			return false;
		}
		Tin tin = Tin(0);
		double[] profileSimulationTimes = correspondingResult.ProfileSimulationTimes.ToArray();
		double profileSimulationTime = profileSimulationTimes[0];
		checked
		{
			int count = profileSimulationTimes.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				profileSimulationTimes[i] = (profileSimulationTimes[i] - profileSimulationTime) * 24.0 * 60.0 * 60.0;
			}
			if (File.Exists(SourceFilename))
			{
				File.Delete(SourceFilename);
			}
			using (H5Writer h5Writer = new H5Writer(SourceFilename))
			{
				h5Writer.WriteArray("/File Type", new string[1] { "Xmdf" });
				h5Writer.WriteArray("/File Version", new float[1] { 1.9f });
				string path = "/Datasets";
				h5Writer.CreateGroup(path);
				h5Writer.WriteAttribute(path, "Grouptype", new string[1] { "MULTI DATASETS" });
				h5Writer.WriteArray(path + "/GUID", new string[1] { guid });
				if (IsWaterSurfaceFile(SourceFilename))
				{
					string pathTerm = path + "/Water Surface Elevation (63)";
					h5Writer.CreateGroup(pathTerm);
					h5Writer.WriteAttribute(pathTerm, "Data Type", new int[1]);
					h5Writer.WriteAttribute(pathTerm, "DatasetCompression", new int[1] { -1 });
					string unitsSharedDataTerm = ((SharedData.Units != SharedData.EnumUnits.SIUnits) ? "ft" : "m");
					h5Writer.WriteAttribute(pathTerm, "DatasetUnits", new string[1] { unitsSharedDataTerm });
					h5Writer.WriteAttribute(pathTerm, "Grouptype", new string[1] { "DATASET SCALAR" });
					h5Writer.WriteAttribute(pathTerm, "TimeUnits", new string[1] { "Seconds" });
					string textValue = pathTerm + "/PROPERTIES";
					h5Writer.CreateGroup(textValue);
					h5Writer.WriteAttribute(textValue, "Grouptype", new string[1] { "PROPERTIES" });
					h5Writer.WriteArray(textValue + "/Object Type", new string[1] { "MESH2DDATAOBJ" });
					h5Writer.WriteArray(textValue + "/nullvalue", new float[1] { -99999f });
					h5Writer.WriteArray("Datasets/Water Surface Elevation (63)/Times", profileSimulationTimes);
					RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(correspondingResult.Geometry);
					rASGeometryMapPoints.ComputeForPoints(tin);
					CacheCollection cache = new CacheCollection();
					WaterSurfaceRenderer waterSurfaceRenderer = new WaterSurfaceRenderer(correspondingResult, cache);
					float[] waterSurfaces = null;
					float[] floatValues = new float[profileSimulationTimes.Length - 1 + 1];
					float[] floatValuesArray = new float[profileSimulationTimes.Length - 1 + 1];
					h5Writer.Create2dExtendibleDataset("Datasets/Water Surface Elevation (63)/Values", typeof(float), 1, tin.PointMCount());
					int count2 = profileSimulationTimes.Count() - 1;
					for (int j = 0; j <= count2; j++)
					{
						int profile = j;
						float[] outputTimes = null;
						waterSurfaceRenderer.Compute(profile, rASGeometryMapPoints, ref waterSurfaces, null, ref outputTimes);
						int count3 = waterSurfaces.Count() - 1;
						for (int k = 0; k <= count3; k++)
						{
							if (waterSurfaces[k] == -9999f)
							{
								waterSurfaces[k] = -99999f;
							}
						}
						h5Writer.AddRow("Datasets/Water Surface Elevation (63)/Values", waterSurfaces, j);
						float minValue = float.MinValue;
						float maxValue = float.MaxValue;
						int count4 = tin.PointMCount() - 1;
						for (int l = 0; l <= count4; l++)
						{
							float waterSurface = waterSurfaces[l];
							if (waterSurface != -99999f)
							{
								if (waterSurface > minValue)
								{
									minValue = waterSurface;
								}
								if (waterSurface < maxValue)
								{
									maxValue = waterSurface;
								}
							}
						}
						if (minValue == float.MinValue)
						{
							floatValues[j] = -99999f;
						}
						else
						{
							floatValues[j] = minValue;
						}
						if (maxValue == float.MinValue)
						{
							floatValuesArray[j] = -99999f;
						}
						else
						{
							floatValuesArray[j] = maxValue;
						}
					}
					h5Writer.WriteArray("Datasets/Water Surface Elevation (63)/Maxs", floatValues);
					h5Writer.WriteArray("Datasets/Water Surface Elevation (63)/Mins", floatValuesArray);
				}
				else
				{
					if (!IsVelocityFile(SourceFilename))
					{
						return false;
					}
					string pathTerm2 = path + "/Depth-averaged Velocity (64)";
					string pathTerm3 = path + "/Depth-averaged Velocity (64) mag";
					h5Writer.CreateGroup(pathTerm2);
					h5Writer.CreateGroup(pathTerm3);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64)/Times", profileSimulationTimes);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64) mag/Times", profileSimulationTimes);
					h5Writer.WriteAttribute(pathTerm2, "Data Type", new int[1]);
					h5Writer.WriteAttribute(pathTerm3, "Data Type", new int[1]);
					h5Writer.WriteAttribute(pathTerm2, "DatasetCompression", new int[1] { -1 });
					h5Writer.WriteAttribute(pathTerm3, "DatasetCompression", new int[1] { -1 });
					string path2 = ((SharedData.Units != SharedData.EnumUnits.SIUnits) ? "f/s" : "m/s");
					h5Writer.WriteAttribute(pathTerm2, "DatasetUnits", new string[1] { path2 });
					h5Writer.WriteAttribute(pathTerm3, "DatasetUnits", new string[1] { path2 });
					h5Writer.WriteAttribute(pathTerm2, "Grouptype", new string[1] { "DATASET VECTOR" });
					h5Writer.WriteAttribute(pathTerm3, "Grouptype", new string[1] { "DATASET SCALAR" });
					h5Writer.WriteAttribute(pathTerm2, "TimeUnits", new string[1] { "Seconds" });
					h5Writer.WriteAttribute(pathTerm3, "TimeUnits", new string[1] { "Seconds" });
					string textValue2 = pathTerm2 + "/PROPERTIES";
					string textValue3 = pathTerm3 + "/PROPERTIES";
					h5Writer.CreateGroup(textValue2);
					h5Writer.CreateGroup(textValue3);
					h5Writer.WriteAttribute(textValue2, "Grouptype", new string[1] { "PROPERTIES" });
					h5Writer.WriteAttribute(textValue3, "Grouptype", new string[1] { "PROPERTIES" });
					h5Writer.WriteArray(textValue2 + "/Object Type", new string[1] { "MESH2DDATAOBJ" });
					h5Writer.WriteArray(textValue3 + "/Object Type", new string[1] { "MESH2DDATAOBJ" });
					h5Writer.WriteArray(textValue2 + "/nullvalue", new float[1] { -99999f });
					h5Writer.WriteArray(textValue3 + "/nullvalue", new float[1] { -99999f });
					RASGeometryMapPoints rASGeometryMapPoints2 = new RASGeometryMapPoints(correspondingResult.Geometry);
					rASGeometryMapPoints2.ComputeForPoints(tin);
					CacheCollection cache2 = new CacheCollection();
					VelocityRenderer velocityRenderer = new VelocityRenderer(correspondingResult, cache2);
					float[] outVelocityMag = null;
					float[] outVelocityX = null;
					float[] outVelocityY = null;
					float[] floatValues2 = new float[profileSimulationTimes.Length - 1 + 1];
					float[] floatValues3 = new float[profileSimulationTimes.Length - 1 + 1];
					h5Writer.Create2dExtendibleDataset("Datasets/Depth-averaged Velocity (64) mag/Values", typeof(float), 1, tin.PointMCount());
					float[,,] floatValues4 = new float[profileSimulationTimes.Length - 1 + 1, tin.PointMCount() - 1 + 1, 2];
					float[] terrain2 = terrain.ComputePointElevations(tin);
					int count5 = profileSimulationTimes.Count() - 1;
					for (int m = 0; m <= count5; m++)
					{
						int profile2 = m;
						float[] outputTimes = null;
						velocityRenderer.Compute(profile2, rASGeometryMapPoints2, terrain2, ref outVelocityX, ref outVelocityY, ref outVelocityMag, ref outputTimes);
						int count6 = outVelocityMag.Count() - 1;
						for (int n = 0; n <= count6; n++)
						{
							if (outVelocityMag[n] == -9999f)
							{
								outVelocityMag[n] = 0f;
							}
							if (outVelocityX[n] == -9999f)
							{
								outVelocityX[n] = 0f;
							}
							if (outVelocityY[n] == -9999f)
							{
								outVelocityY[n] = 0f;
							}
						}
						h5Writer.AddRow("Datasets/Depth-averaged Velocity (64) mag/Values", outVelocityMag, m);
						float minValue2 = float.MinValue;
						float maxValue2 = float.MaxValue;
						int count7 = tin.PointMCount() - 1;
						for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
						{
							floatValues4[m, intervalIndex, 0] = outVelocityX[intervalIndex];
							floatValues4[m, intervalIndex, 1] = outVelocityY[intervalIndex];
							float outVelocityMag2 = outVelocityMag[intervalIndex];
							if (outVelocityMag2 > minValue2)
							{
								minValue2 = outVelocityMag2;
							}
							if (outVelocityMag2 < maxValue2)
							{
								maxValue2 = outVelocityMag2;
							}
						}
						floatValues2[m] = minValue2;
						floatValues3[m] = maxValue2;
					}
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64)/Values", floatValues4);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64) mag/Maxs", floatValues2);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64)/Maxs", floatValues2);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64) mag/Mins", floatValues3);
					h5Writer.WriteArray("Datasets/Depth-averaged Velocity (64)/Mins", floatValues3);
				}
			}
			WriteTheStupidTextFile();
			return true;
		}
	}

	private void WriteTheStupidTextFile()
	{
		checked
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			StringBuilder stringBuilder = new StringBuilder();
			string featureName = GetFeatureName(0);
			if (string.IsNullOrEmpty(featureName))
			{
				featureName = "HEC-RAS Mesh";
			}
			featureName = featureName.Substring(0, Math.Min(24, featureName.Length));
			stringBuilder.Append(featureName.PadLeft(32));
			stringBuilder.Append("HEC-RAS".PadLeft(24));
			stringBuilder.Append(featureName.PadLeft(24));
			stringBuilder.AppendLine();
			if (IsWaterSurfaceFile(SourceFilename))
			{
				double[] values = null;
				h5Reader.ReadDataset("Datasets/Water Surface Elevation (63)/Times", ref values);
				double computed = values[1] - values[0];
				float[,] values2 = null;
				h5Reader.ReadDataset("Datasets/Water Surface Elevation (63)/Values", ref values2);
				stringBuilder.AppendLine("\t\t" + values.Count() + "\t\t" + values2.GetLength(1) + " 1800 \t" + computed + "\t1");
				int count = values.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					stringBuilder.AppendLine("\t" + values[i] + "\t" + i);
					int count2 = values2.GetLength(1) - 1;
					for (int j = 0; j <= count2; j++)
					{
						stringBuilder.AppendLine("\t\t" + (j + 1) + "\t" + values2[i, j]);
					}
				}
				string path = Path.Combine(Path.GetDirectoryName(SourceFilename), "fort.63");
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				File.WriteAllText(path, stringBuilder.ToString());
			}
			else
			{
				if (!IsVelocityFile(SourceFilename))
				{
					return;
				}
				double[] values3 = null;
				h5Reader.ReadDataset("Datasets/Depth-averaged Velocity (64)/Times", ref values3);
				double values3Step = values3[1] - values3[0];
				float[,,] values4 = null;
				h5Reader.ReadDataset("Datasets/Depth-averaged Velocity (64)/Values", ref values4);
				stringBuilder.AppendLine("\t\t" + values3.Count() + "\t\t" + values4.GetLength(1) + " 1800 \t" + values3Step + "\t2");
				int count3 = values3.Length - 1;
				for (int k = 0; k <= count3; k++)
				{
					stringBuilder.AppendLine("\t" + values3[k] + "\t" + k);
					int count4 = values4.GetLength(1) - 1;
					for (int l = 0; l <= count4; l++)
					{
						stringBuilder.AppendLine("\t\t" + (l + 1) + "\t" + values4[k, l, 0] + "\t" + values4[k, l, 1]);
					}
				}
				string path2 = Path.Combine(Path.GetDirectoryName(SourceFilename), "fort.64");
				if (File.Exists(path2))
				{
					File.Delete(path2);
				}
				File.WriteAllText(path2, stringBuilder.ToString());
			}
		}
	}

	public static string WaterSurfaceOrVelocityFile(string filename)
	{
		if (IsWaterSurfaceFile(filename))
		{
			return "Water Surface";
		}
		if (IsVelocityFile(filename))
		{
			return "Velocity";
		}
		return "Unknown";
	}

	public static string WaterSurfaceOrVelocityFile(bool isWaterSurface)
	{
		if (isWaterSurface)
		{
			return "Water Surface";
		}
		return "Velocity";
	}

	public static bool IsWaterSurfaceFile(string filename)
	{
		if (string.Equals(Path.GetFileNameWithoutExtension(filename), "fort63", StringComparison.InvariantCultureIgnoreCase))
		{
			return true;
		}
		return false;
	}

	public static bool IsVelocityFile(string filename)
	{
		if (string.Equals(Path.GetFileNameWithoutExtension(filename), "fort64", StringComparison.InvariantCultureIgnoreCase))
		{
			return true;
		}
		return false;
	}

	public void TrySetProfile(string profile)
	{
		if (profile.RoughlyEquals("Max"))
		{
			CurrentProfileIndex = int.MaxValue;
		}
		else if (profile.RoughlyEquals("Min"))
		{
			CurrentProfileIndex = int.MinValue;
		}
		else
		{
			CurrentProfileIndex = AvailableProfiles.IndexOf(profile);
		}
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		CurrentProfileIndex = int.MaxValue;
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		CurrentProfileIndex = int.MinValue;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
