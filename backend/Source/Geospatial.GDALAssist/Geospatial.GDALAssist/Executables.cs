using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Utility.Processes;

namespace Geospatial.GDALAssist;

public static class Executables
{
	public const string RAS_TIFF_OUTPUT_ARGS = "-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES ";

	internal static ProcessStartInfo PsiWarp;

	internal static ProcessStartInfo PsiTranslate;

	internal static ProcessStartInfo PsiOgr;

	internal static ProcessStartInfo PsiGdalInfo;

	internal static ProcessStartInfo PsiOgrInfo;

	internal static ProcessStartInfo PsiAddo;

	internal static ProcessStartInfo PsiDem;

	internal static ProcessStartInfo PsiContour;

	internal static ProcessStartInfo PsiVRT;

	internal static ProcessStartInfo PsiRasterize;

	private static void Run(ProcessStartInfo GDALExe, string args, out string StdOut, out string StdErr, Action<char> StdOutReport, Action<char> StdErrReport, Action<Process> ProcessReport)
	{
		ProcessOutput processOutput = ProcessHelper.RunStreaming(GDALExe.CloneWith(args), StdOutReport, StdErrReport, ProcessReport);
		StdOut = processOutput.StandardOutput;
		StdErr = processOutput.StandardError;
	}

	public static void GdalBuildVRT(string[] InputFiles, string OutputVRTFile, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string textValue = "";
		for (int i = 0; i < InputFiles.Length; i++)
		{
			textValue = textValue + " \"" + InputFiles[i] + "\"";
		}
		textValue = "\"" + OutputVRTFile + "\"" + textValue + " -resolution highest -allow_projection_difference";
		Run(PsiVRT, textValue, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalBuildVRT(string inputFileList, string OutputVRTFile, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string outputVRTFileTerm = "-input_file_list \"" + inputFileList + "\"";
		outputVRTFileTerm = outputVRTFileTerm + " \"" + OutputVRTFile + "\" -resolution highest";
		Run(PsiVRT, outputVRTFileTerm, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalBuildVRT(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiVRT, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalRasterize(string args, out string stdOut, out string stdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiRasterize, args, out stdOut, out stdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalContour(string InputFile, string OutputShpFile, float ContourLineDelta, ref string stdOut, ref string stdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		GdalContour("\"" + InputFile + "\" \"" + OutputShpFile + "\" -i " + ContourLineDelta, out stdOut, out stdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalContour(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiContour, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemHillshade(string InputFile, string OutputFile, int Azimuth = 315, int Altitude = 45, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		GdalDem("hillshade \"" + InputFile + "\" \"" + OutputFile + "\" -az " + Azimuth + " -alt " + Altitude, out var _, out var _, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemSlope(string InputFile, string OutputFile, bool UsePercentSlope = false, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string textValue = "";
		if (UsePercentSlope)
		{
			textValue = " -p";
		}
		GdalDem("slope \"" + InputFile + "\" \"" + OutputFile + "\"" + textValue, out var _, out var _, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemAspect(string InputFile, string OutputFile, bool UseTrigonometric = false, bool UseZeroForFlat = false, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string StdOut = "";
		string StdErr = "";
		string textValue = "";
		if (UseTrigonometric)
		{
			textValue = " -trigonometric";
		}
		if (UseZeroForFlat)
		{
			textValue += " -zero_for_flat";
		}
		GdalDem("aspect \"" + InputFile + "\" \"" + OutputFile + "\"" + textValue, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemTRI(string InputFile, string OutputFile, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string StdOut = "";
		string StdErr = "";
		GdalDem("TRI \"" + InputFile + "\" \"" + OutputFile + "\"", out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemTPI(string InputFile, string OutputFile, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string StdOut = "";
		string StdErr = "";
		GdalDem("TPI \"" + InputFile + "\" \"" + OutputFile + "\"", out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDemColor(string InputFile, string OutputFile, string ColorRampFile, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string StdOut = "";
		string StdErr = "";
		GdalDem("color-relief -alpha \"" + InputFile + "\" \"" + ColorRampFile + "\" \"" + OutputFile + "\"", out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalDem(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiDem, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalInfo(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiGdalInfo, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void OgrInfo(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiOgrInfo, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void Ogr2ogr(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiOgr, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalTranslate(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiTranslate, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalTranslate(string args)
	{
		ProcessHelper.Run(PsiTranslate, args);
	}

	public static void GdalTranslate(string inFile, string outFile)
	{
		GdalTranslate("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES  \"" + inFile + "\" \"" + outFile + "\"");
	}

	public static void GdalWarp(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiWarp, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalAddo(string Filename, int[] OverviewLevels, bool CreateSeparateOVR, out string StdOut, out string StdErr, string Compression = "DEFLATE", Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		string textValue = "";
		if (!string.IsNullOrEmpty(Compression))
		{
			textValue = textValue + "--config COMPRESS_OVERVIEW " + Compression + " ";
		}
		if (CreateSeparateOVR)
		{
			textValue += "-ro ";
		}
		textValue += "-r average ";
		textValue += Filename;
		for (int i = 0; i < OverviewLevels.Length; i++)
		{
			textValue = textValue + " " + OverviewLevels[i];
		}
		GdalAddo(textValue, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static void GdalAddo(string args, out string StdOut, out string StdErr, Action<char> StdOutReport = null, Action<char> StdErrReport = null, Action<Process> SetProcess = null)
	{
		Run(PsiAddo, args, out StdOut, out StdErr, StdOutReport, StdErrReport, SetProcess);
	}

	public static double[] ReadWLD(string worldfile)
	{
		double[] doubleValues = new double[6];
		string[] allLines = null;
		if (File.Exists(worldfile))
		{
			allLines = File.ReadAllLines(worldfile);
			if (allLines.Length > 6)
			{
				throw new Exception("Incorrect world file: " + File.ReadAllText(worldfile));
			}
			if (allLines.Length < 6)
			{
				throw new Exception("Incorrect world file: " + File.ReadAllText(worldfile));
			}
			for (int i = 0; i < allLines.Length; i++)
			{
				doubleValues[i] = Convert.ToDouble(allLines[i]);
			}
			return doubleValues;
		}
		return null;
	}

	internal static void SafeAppend(string filename, string stringValue)
	{
		FileStream fileStream = WaitForFile(filename);
		fileStream.Position = fileStream.Length;
		byte[] bytes = new ASCIIEncoding().GetBytes(stringValue);
		fileStream.Write(bytes, 0, bytes.Length);
		fileStream.Close();
	}

	private static FileStream WaitForFile(string pathName)
	{
		int ordinal = 0;
		while (true)
		{
			ordinal++;
			try
			{
				FileStream fileStream = new FileStream(pathName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
				fileStream.ReadByte();
				return fileStream;
			}
			catch
			{
				if (ordinal > 250)
				{
					throw new Exception("File handles appear to have frozen");
				}
				Thread.Sleep(10);
			}
		}
	}
}
