using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Geospatial.Vectors;
using H5Assist;
using Utility.Extensions;
using Utility.Progress;

namespace Geospatial.IO;

public static class Sms
{
	private static readonly string[] _whitespace = new string[2] { " ", "/t" };

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

	public static bool TryImportSMSMeshDefinition(string filename, out string meshName, out Tin tin, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		meshName = "";
		tin = null;
		if (!File.Exists(filename))
		{
			reporter.ReportError("Source file " + filename.InSingleQuotes() + " does not exist.");
			return false;
		}
		string[] allLines = File.ReadAllLines(filename);
		if (allLines.Length < 6)
		{
			reporter.ReportError("Malformed SMS Grid File");
			return false;
		}
		meshName = allLines[0];
		reporter.ReportMessage("Mesh Name: " + meshName);
		string[] parts = allLines[1].Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2)
		{
			reporter.ReportError("Malformed SMS Grid File");
			return false;
		}
		if (!int.TryParse(parts[0], out var result))
		{
			reporter.ReportError("Malformed SMS Grid File");
			return false;
		}
		if (!int.TryParse(parts[1], out var result2))
		{
			reporter.ReportError("Malformed SMS Grid File");
			return false;
		}
		reporter.ReportMessage("Points: " + result2);
		reporter.ReportMessage("Triangles: " + result);
		PointCollection pointCollection = new PointCollection(hasZ: true);
		for (int i = 2; i < 2 + result2; i++)
		{
			if (i % 100 == 0)
			{
				reporter.ReportProgress((int)(45.0 * (double)i / (double)result2));
			}
			string[] partsArray = allLines[i].Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
			if (partsArray.Length != 4)
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!float.TryParse(partsArray[1], out var result3))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!float.TryParse(partsArray[2], out var result4))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!float.TryParse(partsArray[3], out var result5))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			pointCollection.Add(result3, result4, result5);
		}
		List<TriangleDefinition> triangleDefinitions = new List<TriangleDefinition>();
		for (int j = 2 + result2; j < 2 + result2 + result; j++)
		{
			if (j % 100 == 0)
			{
				reporter.ReportProgress((int)(45.0 + 45.0 * (double)(j - result2) / (double)result));
			}
			string[] parts2 = allLines[j].Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
			if (parts2.Length != 5)
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!int.TryParse(parts2[2], out var result6))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!int.TryParse(parts2[3], out var result7))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			if (!int.TryParse(parts2[4], out var result8))
			{
				reporter.ReportError("Malformed SMS Grid File");
				return false;
			}
			triangleDefinitions.Add(new TriangleDefinition(result6 - 1, result7 - 1, result8 - 1, -1, -1, -1));
		}
		tin = Tin.RebuildAdjacencyInformation(pointCollection, triangleDefinitions);
		reporter.ReportProgress(100);
		return true;
	}

	public static bool TryParseFort63TextFile(string filename, out double[,] values, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		values = null;
		if (!File.Exists(filename))
		{
			reporter.ReportError("Source file " + filename.InSingleQuotes() + " does not exist.");
			return false;
		}
		try
		{
			using FileStream stream = File.OpenRead(filename);
			using StreamReader streamReader = new StreamReader(stream);
			string line = streamReader.ReadLine();
			line.Trim();
			line = streamReader.ReadLine();
			line.Trim();
			line = streamReader.ReadLine();
			line.Trim();
			line = streamReader.ReadLine();
			string[] parts = line.Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 4)
			{
				reporter.ReportError("Unexpected text on line 5. Malformed fort.63/fort.64 File");
				return false;
			}
			if (!int.TryParse(parts[0], out var result))
			{
				reporter.ReportError("Unexpected text on line 5, timestep count. Malformed fort.63/fort.64 File");
				return false;
			}
			if (!int.TryParse(parts[1], out var result2))
			{
				reporter.ReportError("Unexpected text on line 5, point count. Malformed fort.63/fort.64 File");
				return false;
			}
			if (!double.TryParse(parts[3], out var _))
			{
				reporter.ReportError("Unexpected text on line 5, delta time. Malformed fort.63/fort.64 File");
				return false;
			}
			_ = 1 + result2;
			values = new double[result, result2];
			int ordinal = 5;
			for (int i = 0; i < result; i++)
			{
				line = streamReader.ReadLine();
				parts = line.Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length != 2)
				{
					LineError(reporter, line, ordinal);
					return false;
				}
				if (!double.TryParse(parts[0], out var _))
				{
					LineError(reporter, line, ordinal);
					return false;
				}
				ordinal++;
				for (int j = 0; j < result2; j++)
				{
					line = streamReader.ReadLine();
					parts = line.Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length != 2)
					{
						LineError(reporter, line, ordinal);
						return false;
					}
					if (!double.TryParse(parts[1], out var result5))
					{
						LineError(reporter, line, ordinal);
						return false;
					}
					values[i, j] = result5;
					ordinal++;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reporter.ReportError(ex.Message);
			return false;
		}
	}

	private static bool MatchesTimestepHeader(string[] split, out double timestepValue)
	{
		if (split.Length != 4)
		{
			timestepValue = -1.0;
			return false;
		}
		if (double.TryParse(split[0], out timestepValue) && int.TryParse(split[1], out var result) && int.TryParse(split[2], out result) && double.TryParse(split[3], out var _))
		{
			return true;
		}
		timestepValue = -1.0;
		return false;
	}

	private static bool MatchesPointValueLine(string[] split, out int ptIdx, out double unity)
	{
		if (split.Length != 2)
		{
			ptIdx = -1;
			unity = -1.0;
			return false;
		}
		if (int.TryParse(split[0], out ptIdx) && double.TryParse(split[1], out unity))
		{
			return true;
		}
		ptIdx = -1;
		unity = -1.0;
		return false;
	}

	private static bool MatchesGlobalHeader(string[] split, out int tsCount, out int ptCount)
	{
		if (split.Length < 5)
		{
			tsCount = -1;
			ptCount = -1;
			return false;
		}
		if (int.TryParse(split[0], out tsCount) && int.TryParse(split[1], out ptCount) && double.TryParse(split[2], out var _) && int.TryParse(split[3], out var _) && int.TryParse(split[4], out var result3))
		{
			if (result3 == 1 || result3 == 2)
			{
				return true;
			}
			return false;
		}
		tsCount = -1;
		ptCount = -1;
		return false;
	}

	public static bool TryParseFort63TextFile_V2(string filename, out List<float[]> values, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (!File.Exists(filename))
		{
			reporter.ReportError("Source file " + filename.InSingleQuotes() + " does not exist.");
			values = null;
			return false;
		}
		values = new List<float[]>();
		try
		{
			using FileStream stream = File.OpenRead(filename);
			using StreamReader streamReader = new StreamReader(stream);
			int unity = -1;
			bool condition = false;
			int ptCount = 0;
			int tsCount = 0;
			string line = streamReader.ReadLine();
			while (!string.IsNullOrEmpty(line))
			{
				string[] split = line.Split(_whitespace, StringSplitOptions.RemoveEmptyEntries);
				if (!condition)
				{
					if (MatchesGlobalHeader(split, out tsCount, out ptCount))
					{
						condition = true;
					}
					line = streamReader.ReadLine();
					continue;
				}
				int ptIdx;
				double computed;
				if (MatchesTimestepHeader(split, out var _))
				{
					unity++;
					reporter.ReportProgress((int)(100f * (float)unity / (float)tsCount));
					values.Add(new float[ptCount]);
				}
				else if (unity >= 0 && MatchesPointValueLine(split, out ptIdx, out computed))
				{
					values[unity][ptIdx] = (float)computed;
				}
				line = streamReader.ReadLine();
			}
			return true;
		}
		catch (Exception ex)
		{
			reporter.ReportError(ex.Message);
			return false;
		}
	}

	private static void LineError(ProgressReporter reporter, string line, int lineNum)
	{
		reporter.ReportError("Unexpected formatting on line " + (lineNum + 1) + ": " + line);
	}

	public static void WriteTheStupidTextFile(string filename)
	{
		using H5Reader h5Reader = new H5Reader(filename);
		StringBuilder stringBuilder = new StringBuilder();
		string textValue = "";
		if (string.IsNullOrEmpty(textValue))
		{
			textValue = "HEC-RAS Mesh";
		}
		textValue = textValue.Substring(0, Math.Min(24, textValue.Length));
		stringBuilder.Append(textValue.PadLeft(32));
		stringBuilder.Append("HEC-RAS".PadLeft(24));
		stringBuilder.Append(textValue.PadLeft(24));
		stringBuilder.AppendLine();
		if (IsWaterSurfaceFile(filename))
		{
			h5Reader.ReadDataset("Datasets/Water Surface Elevation (63)/Times", out double[] values);
			double computed = values[1] - values[0];
			h5Reader.ReadDataset("Datasets/Water Surface Elevation (63)/Values", out float[,] values2);
			stringBuilder.AppendLine("\t\t" + values.Length + "\t\t" + values2.GetLength(1) + " 1800 \t" + computed + "\t1");
			for (int i = 0; i < values.Length; i++)
			{
				stringBuilder.AppendLine("\t" + values[i] + "\t" + i);
				for (int j = 0; j <= values2.GetLength(1) - 1; j++)
				{
					stringBuilder.AppendLine("\t\t" + (j + 1) + "\t" + values2[i, j]);
				}
			}
			string path = Path.Combine(Path.GetDirectoryName(filename), "fort.63");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.WriteAllText(path, stringBuilder.ToString());
		}
		else
		{
			if (!IsVelocityFile(filename))
			{
				return;
			}
			h5Reader.ReadDataset("Datasets/Depth-averaged Velocity (64)/Times", out double[] values3);
			double values3Step = values3[1] - values3[0];
			h5Reader.ReadDataset("Datasets/Depth-averaged Velocity (64)/Values", out float[,,] values4);
			stringBuilder.AppendLine("\t\t" + values3.Count() + "\t\t" + values4.GetLength(1) + " 1800 \t" + values3Step + "\t2");
			for (int k = 0; k <= values3.Length - 1; k++)
			{
				stringBuilder.AppendLine("\t" + values3[k] + "\t" + k);
				for (int l = 0; l < values4.GetLength(1); l++)
				{
					stringBuilder.AppendLine("\t\t" + (l + 1) + "\t" + values4[k, l, 0] + "\t" + values4[k, l, 1]);
				}
			}
			string path2 = Path.Combine(Path.GetDirectoryName(filename), "fort.64");
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
			File.WriteAllText(path2, stringBuilder.ToString());
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
}
