using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Terrain;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Progress;
using RasMapperLib.Terrain;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

[ComVisible(false)]
public class TerrainComputable : IAmComputable
{
	private enum TranslationTypeNeeded
	{
		TranslateWithSRS,
		TranslateWithoutSRS,
		Warp,
		None
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D0
	{
		public ProgressReporter reporter;

		public List<string> filesToClean;

		public string stdOut;

		public string stdErr;

		public Action<char> addRedChar;

		public List<string> processedTiffs;

		public string vrtFilename;

		public double minCellSize;

		public TerrainComputable terrainComputable;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				reporter = other.reporter;
				filesToClean = other.filesToClean;
				stdOut = other.stdOut;
				stdErr = other.stdErr;
				addRedChar = other.addRedChar;
				processedTiffs = other.processedTiffs;
				vrtFilename = other.vrtFilename;
				minCellSize = other.minCellSize;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0()
		{
			if (reporter.CancelRequested)
			{
				terrainComputable._cw.CleanFilesPrintStatus(filesToClean);
				return true;
			}
			return false;
		}

		[SpecialName]
		internal void _Lambda_0024__9()
		{
			InterpolatedLayer.CreateVRT(processedTiffs.ToArray(), vrtFilename, reporter);
		}

		[SpecialName]
		internal void _Lambda_0024__10()
		{
			Geospatial.Terrain.Terrain.GenerateTerrain(terrainComputable._terrainFilename, processedTiffs, reporter, terrainComputable._writeStitchInformation);
		}

		[SpecialName]
		internal void _Lambda_0024__11()
		{
			StitchCache stitches = new StitchCache(terrainComputable._terrainFilename, Geospatial.Terrain.Terrain.GetStitchesName());
			double nearPointTolerance = System.Math.Min(0.01, minCellSize * 0.1);
			DevelopmentTin developmentTin = new DelaunayTINFactory(nearPointTolerance).ComputeTIN(null, null, null, stitches, removeDeletedTriangles: true, double.NaN, reporter);
			if (reporter.CancelRequested)
			{
				return;
			}
			checked
			{
				using H5Writer hw = new H5Writer(terrainComputable._terrainFilename);
				using (HDFBufferedWriter<double> hDFBufferedWriter = new HDFBufferedWriter<double>(hw, Geospatial.Terrain.Terrain.GetStitchTinName() + " Points", developmentTin.Points.Count, 4, "Point", "X", "Y", "Z", "M"))
				{
					int countOffset = developmentTin.Points.Count - 1;
					for (int i = 0; i <= countOffset; i++)
					{
						PointM pointM = developmentTin.Points[i];
						hDFBufferedWriter.AddRow(pointM.X, pointM.Y, pointM.Z, pointM.M);
					}
				}
				using (HDFBufferedWriter<int> hDFBufferedWriter2 = new HDFBufferedWriter<int>(hw, Geospatial.Terrain.Terrain.GetStitchTinName() + " Triangles", developmentTin.Triangles.Count, 3, "Triangle", "p0", "p1", "p2"))
				{
					int countOffset2 = developmentTin.Triangles.Count - 1;
					for (int j = 0; j <= countOffset2; j++)
					{
						DevelopmentTriangle developmentTriangle = developmentTin.Triangles[j];
						hDFBufferedWriter2.AddRow(developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.p2);
					}
				}
				SharedData.HDFWriteSRS(hw);
				SharedData.HDFWriteFileType(hw, "HEC Terrain");
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D1
	{
		public string inputFilename;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D1(_Closure_0024__15_002D1 other)
		{
			if (other != null)
			{
				inputFilename = other.inputFilename;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D2
	{
		public string tiffFilename;

		public _Closure_0024__15_002D1 outerClosure;

		public _Closure_0024__15_002D2(_Closure_0024__15_002D2 other)
		{
			if (other != null)
			{
				tiffFilename = other.tiffFilename;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			File.Copy(outerClosure.inputFilename, tiffFilename);
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			Executables.GdalTranslate("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES " + outerClosure.inputFilename.InDoubleQuotes() + " " + tiffFilename.InDoubleQuotes(), out outerClosure.outerClosure.stdOut, out outerClosure.outerClosure.stdErr, outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, outerClosure.outerClosure.addRedChar, outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			Executables.GdalTranslate("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES -a_srs " + SharedData.SRS_GDAL_CMS + " " + outerClosure.inputFilename.InDoubleQuotes() + " " + tiffFilename.InDoubleQuotes(), out outerClosure.outerClosure.stdOut, out outerClosure.outerClosure.stdErr, outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, outerClosure.outerClosure.addRedChar, outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
		}

		[SpecialName]
		internal void _Lambda_0024__7()
		{
			InterpolatedLayer.AddHistogram(tiffFilename, null, outerClosure.outerClosure.reporter);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D3
	{
		public Projection fileProjection;

		public float verticalFactor;

		public _Closure_0024__15_002D2 outerClosure;

		public _Closure_0024__15_002D3(_Closure_0024__15_002D3 other)
		{
			if (other != null)
			{
				fileProjection = other.fileProjection;
				verticalFactor = other.verticalFactor;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__6()
		{
			string changeExtension = System.IO.Path.ChangeExtension(outerClosure.tiffFilename, ".unrounded.tif");
			File.Delete(changeExtension);
			File.Move(outerClosure.tiffFilename, changeExtension);
			FloatTiffWriter.CopyData(changeExtension, outerClosure.tiffFilename, outerClosure.outerClosure.outerClosure.terrainComputable._metadata, verticalFactor, outerClosure.outerClosure.outerClosure.reporter, "8");
			File.Delete(changeExtension);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D4
	{
		public double fileNoData;

		public _Closure_0024__15_002D5 outerClosure;

		public _Closure_0024__15_002D4(_Closure_0024__15_002D4 other)
		{
			if (other != null)
			{
				fileNoData = other.fileNoData;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			Executables.GdalWarp("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES  -r bilinear -s_srs " + outerClosure.outerClosure.fileProjection.GetProj4().InDoubleQuotes() + " -t_srs " + SharedData.SRS_GDAL_CMS + " -dstnodata " + fileNoData + " " + outerClosure.outerClosure.outerClosure.outerClosure.inputFilename.InDoubleQuotes() + " " + outerClosure.outerClosure.outerClosure.tiffFilename.InDoubleQuotes(), out outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.stdOut, out outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.stdErr, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.addRedChar, outerClosure.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D5
	{
		public int tiffWidth;

		public int tiffHeight;

		public double minX;

		public double maxY;

		public double cellSize;

		public _Closure_0024__15_002D3 outerClosure;

		public _Closure_0024__15_002D5(_Closure_0024__15_002D5 other)
		{
			if (other != null)
			{
				tiffWidth = other.tiffWidth;
				tiffHeight = other.tiffHeight;
				minX = other.minX;
				maxY = other.maxY;
				cellSize = other.cellSize;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__8()
		{
			InterpolatedLayer.AddSRSGeorefAddo(outerClosure.outerClosure.tiffFilename, tiffWidth, tiffHeight, minX, maxY, cellSize, UseNearest: false, outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, outerClosure.outerClosure.outerClosure.outerClosure.addRedChar, outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__20_002D0
	{
		public IList<string> alreadyProcessedFiles;

		public TerrainComputable terrainComputable;

		public _Closure_0024__20_002D0(_Closure_0024__20_002D0 other)
		{
			if (other != null)
			{
				alreadyProcessedFiles = other.alreadyProcessedFiles;
			}
		}

		[SpecialName]
		internal string _Lambda_0024__0(string coreFN)
		{
			string baseFolderTerrainNameTerm = terrainComputable._baseFolder + terrainComputable._terrainName + "." + coreFN + ".tif";
			if (!alreadyProcessedFiles.Contains(baseFolderTerrainNameTerm))
			{
				return baseFolderTerrainNameTerm;
			}
			int ordinal = 1;
			while (true)
			{
				baseFolderTerrainNameTerm = terrainComputable._baseFolder + terrainComputable._terrainName + "." + coreFN + "_" + ordinal + ".tif";
				if (!alreadyProcessedFiles.Contains(baseFolderTerrainNameTerm))
				{
					break;
				}
				ordinal = checked(ordinal + 1);
			}
			return baseFolderTerrainNameTerm;
		}
	}

	private TiffMetadata<float> _metadata;

	private Projection _projectProjection;

	private List<string> _filenames;

	private string _baseFolder;

	private string _terrainName;

	private string _terrainFilename;

	private bool _writeStitchInformation;

	private bool _mergeAllToSingleVRT;

	private float _overrideZScaleFactor;

	private bool _forceReprocessRASTiffs;

	private bool _forceWarp;

	private Action<char> _addRedChar;

	private IDisplayProgress _cw;

	public TerrainComputable(TiffMetadata<float> md, bool writeStitchInformation, Projection projectProjection, List<string> filenames, string baseFolder, string name, float overrideZScaleFactor = float.NaN, bool mergeAllToSingleVRT = false, bool forceReprocessRASTiffs = false, bool forceWarp = false)
	{
		_forceReprocessRASTiffs = false;
		_forceWarp = false;
		_addRedChar = [SpecialName] (char c) =>
		{
			_cw.AddChar(c, Color.Red);
		};
		_metadata = md;
		_projectProjection = projectProjection;
		_filenames = filenames;
		_baseFolder = baseFolder;
		if (_baseFolder.Last() != System.IO.Path.DirectorySeparatorChar)
		{
			_baseFolder += Conversions.ToString(System.IO.Path.DirectorySeparatorChar);
		}
		_terrainName = name;
		_terrainFilename = _baseFolder + _terrainName + ".hdf";
		_writeStitchInformation = writeStitchInformation;
		_mergeAllToSingleVRT = mergeAllToSingleVRT;
		_overrideZScaleFactor = overrideZScaleFactor;
		_forceReprocessRASTiffs = forceReprocessRASTiffs;
		_forceWarp = forceWarp;
	}

	public void Initialize(IDisplayProgress cw)
	{
		_cw = cw;
		_cw.SetHeaderText("Creating Terrain " + _terrainName.InSingleQuotes());
	}

	void IAmComputable.Initialize(IDisplayProgress cw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Initialize
		this.Initialize(cw);
	}

	public void Run(ProgressReporter reporter)
	{
		_Closure_0024__15_002D0 capturedLocals = default;
		_Closure_0024__15_002D0 capturedLocals2 = new _Closure_0024__15_002D0(capturedLocals);
		capturedLocals2.terrainComputable = this;
		capturedLocals2.reporter = reporter;
		if (capturedLocals2.reporter == null)
		{
			capturedLocals2.reporter = ProgressReporter.None();
		}
		H5Reader.SetLogging(log: false);
		H5Reader.SetSafetyChecks(boolValue: false);
		capturedLocals2.vrtFilename = System.IO.Path.ChangeExtension(_terrainFilename, "vrt");
		capturedLocals2.filesToClean = new List<string>();
		Func<bool> func = [SpecialName] () =>
		{
			if (capturedLocals2.reporter.CancelRequested)
			{
				capturedLocals2.terrainComputable._cw.CleanFilesPrintStatus(capturedLocals2.filesToClean);
				return true;
			}
			return false;
		};
		capturedLocals2.processedTiffs = new List<string>();
		Stopwatch stopwatch = Stopwatch.StartNew();
		capturedLocals2.stdOut = "";
		capturedLocals2.stdErr = "";
		capturedLocals2.addRedChar = [SpecialName] (char c) =>
		{
			_cw.AddChar(c, Color.Red);
		};
		_cw.AddTimeFormattedLine("Computation Task", "hh:mm:ss");
		_cw.AddBarrier();
		if (!Directory.Exists(_baseFolder))
		{
			Directory.CreateDirectory(_baseFolder);
		}
		if (_mergeAllToSingleVRT)
		{
			string combine = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_baseFolder), "MergedInputs.vrt");
			_cw.AddLine("Merging input files to VRT " + combine);
			string changeExtension = System.IO.Path.ChangeExtension(combine, ".txt");
			if (File.Exists(changeExtension))
			{
				File.Delete(changeExtension);
			}
			string[] contents = _filenames.ToArray();
			File.WriteAllLines(changeExtension, contents);
			Executables.GdalBuildVRT(changeExtension, combine, out capturedLocals2.stdOut, out capturedLocals2.stdErr);
			File.Delete(changeExtension);
			_filenames.Clear();
			if (!File.Exists(combine))
			{
				_cw.AddLine("Error merging input VRT.", Color.Red);
				return;
			}
			_filenames.Clear();
			_filenames.Add(combine);
		}
		int ordinal = 0;
		capturedLocals2.minCellSize = double.MaxValue;
		List<string> stringList = new List<string>();
		checked
		{
			using (List<string>.Enumerator filenames = _filenames.GetEnumerator())
			{
				_Closure_0024__15_002D1 capturedLocals3 = default;
				_Closure_0024__15_002D2 capturedLocals4 = default;
				_Closure_0024__15_002D3 capturedLocals5 = default;
				RoundTo roundingUsed = default;
				_Closure_0024__15_002D5 capturedLocals6 = default;
				_Closure_0024__15_002D4 capturedLocals7 = default;
				while (filenames.MoveNext())
				{
					capturedLocals3 = new _Closure_0024__15_002D1(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					capturedLocals3.inputFilename = filenames.Current;
					capturedLocals4 = new _Closure_0024__15_002D2(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals3;
					Stopwatch stopwatch2 = Stopwatch.StartNew();
					capturedLocals4.tiffFilename = "";
					GenerateFilename(ref capturedLocals4.outerClosure.inputFilename, ref capturedLocals4.tiffFilename, capturedLocals4.outerClosure.outerClosure.processedTiffs);
					ordinal++;
					_cw.AddLine("Importing " + ordinal + " of " + _filenames.Count + ": " + System.IO.Path.GetFileName(capturedLocals4.outerClosure.inputFilename) + " -> " + System.IO.Path.GetFileName(capturedLocals4.tiffFilename));
					try
					{
						capturedLocals5 = new _Closure_0024__15_002D3(capturedLocals5);
						capturedLocals5.outerClosure = capturedLocals4;
						capturedLocals5.fileProjection = GDALRaster.GetProjection(capturedLocals5.outerClosure.outerClosure.inputFilename);
						if (capturedLocals5.fileProjection.IsValid() == SRSValidation.Corrupt)
						{
							capturedLocals5.fileProjection = null;
						}
						capturedLocals5.verticalFactor = _overrideZScaleFactor;
						if (float.IsNaN(capturedLocals5.verticalFactor))
						{
							capturedLocals5.verticalFactor = GetVerticalScaleFactor(capturedLocals5.fileProjection);
						}
						bool isRASTerrainTiff = IsRASTerrainTiff(capturedLocals5.outerClosure.outerClosure.inputFilename, ref roundingUsed);
						File.Delete(capturedLocals5.outerClosure.tiffFilename);
						if (func())
						{
							return;
						}
						Extent fileExt = null;
						try
						{
							fileExt = Converter.Convert(GDALRaster.GetFullNativeExtents(capturedLocals5.outerClosure.outerClosure.inputFilename));
						}
						catch (Exception ex)
						{
							ProjectData.SetProjectError(ex);
							Exception ex2 = ex;
							ProjectData.ClearProjectError();
						}
						TranslationTypeNeeded fileTranslationType = GetFileTranslationType(capturedLocals5.fileProjection, fileExt);
						if (isRASTerrainTiff && fileTranslationType != TranslationTypeNeeded.Warp && !_forceReprocessRASTiffs)
						{
							_cw.TimeIt("Step 1 of 1: File detected as valid RAS GeoTiff. Copying...", [SpecialName] () =>
							{
								File.Copy(capturedLocals5.outerClosure.outerClosure.inputFilename, capturedLocals5.outerClosure.tiffFilename);
							});
							if (fileTranslationType != TranslationTypeNeeded.TranslateWithSRS)
							{
							}
							goto IL_0767;
						}
						capturedLocals6 = new _Closure_0024__15_002D5(capturedLocals6);
						capturedLocals6.outerClosure = capturedLocals5;
						switch (fileTranslationType)
						{
						case TranslationTypeNeeded.TranslateWithoutSRS:
							_cw.TimeIt("Step 1 of 4: Translating to GeoTiff without SRS...", [SpecialName] () =>
							{
								Executables.GdalTranslate("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES " + capturedLocals6.outerClosure.outerClosure.outerClosure.inputFilename.InDoubleQuotes() + " " + capturedLocals6.outerClosure.outerClosure.tiffFilename.InDoubleQuotes(), out capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.stdOut, out capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.stdErr, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.addRedChar, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
							});
							if (func())
							{
								return;
							}
							break;
						case TranslationTypeNeeded.TranslateWithSRS:
							_cw.TimeIt("Step 1 of 4: Translating to GeoTiff with SRS...", [SpecialName] () =>
							{
								Executables.GdalTranslate("-of GTIFF -ot Float32 -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 -co BIGTIFF=YES -a_srs " + SharedData.SRS_GDAL_CMS + " " + capturedLocals6.outerClosure.outerClosure.outerClosure.inputFilename.InDoubleQuotes() + " " + capturedLocals6.outerClosure.outerClosure.tiffFilename.InDoubleQuotes(), out capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.stdOut, out capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.stdErr, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.GDALProgressReporter, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.addRedChar, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._cw.SetActiveProcess);
							});
							if (func())
							{
								return;
							}
							break;
						default:
						{
							capturedLocals7 = new _Closure_0024__15_002D4(capturedLocals7);
							capturedLocals7.outerClosure = capturedLocals6;
							using (GDALRaster gDALRaster = new GDALRaster(capturedLocals7.outerClosure.outerClosure.outerClosure.outerClosure.inputFilename))
							{
								capturedLocals7.fileNoData = gDALRaster.GetNoDataVB()[0];
							}
							_cw.TimeIt("Step 1 of 4: Translating to GeoTiff and reprojecting...", capturedLocals7._Lambda_0024__5);
							if (func())
							{
								return;
							}
							break;
						}
						}
						double[] location = GDALRaster.GetLocation(capturedLocals6.outerClosure.outerClosure.tiffFilename);
						_cw.TimeIt("Step 2 of 4: Rounding and Generating Statistics...", [SpecialName] () =>
						{
							string changeExtension2 = System.IO.Path.ChangeExtension(capturedLocals6.outerClosure.outerClosure.tiffFilename, ".unrounded.tif");
							File.Delete(changeExtension2);
							File.Move(capturedLocals6.outerClosure.outerClosure.tiffFilename, changeExtension2);
							FloatTiffWriter.CopyData(changeExtension2, capturedLocals6.outerClosure.outerClosure.tiffFilename, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.terrainComputable._metadata, capturedLocals6.outerClosure.verticalFactor, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.reporter, "8");
							File.Delete(changeExtension2);
						});
						if (func())
						{
							return;
						}
						TiffReader<float>.ReadGDALStats(capturedLocals6.outerClosure.outerClosure.tiffFilename, 0, out var Min, out var Max, out var Mean, out var StdDev, out var FractionNodata);
						if (FractionNodata == 1.0)
						{
							_cw.AddLine(System.IO.Path.GetFileName(capturedLocals6.outerClosure.outerClosure.tiffFilename) + " was 100% NoData. Removing...", Color.Red);
							File.Delete(capturedLocals6.outerClosure.outerClosure.tiffFilename);
							stringList.Add(capturedLocals6.outerClosure.outerClosure.tiffFilename);
							_cw.AddBarrier();
							continue;
						}
						if (double.IsNaN(Min) || double.IsNaN(Max) || double.IsNaN(StdDev) || double.IsNaN(Mean))
						{
							_cw.AddLine("Error in stats generation, values came back NaN. If there are any NaN values in your raster, please fix them (or set them to NoData) and try again.", Color.Red);
							_cw.CleanFilesPrintStatus(capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.filesToClean);
							return;
						}
						_cw.TimeIt("Step 3 of 4: Generating Histogram...", [SpecialName] () =>
						{
							InterpolatedLayer.AddHistogram(capturedLocals6.outerClosure.outerClosure.tiffFilename, null, capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.reporter);
						});
						if (func())
						{
							return;
						}
						using (FloatTiffReader floatTiffReader = new FloatTiffReader(capturedLocals6.outerClosure.outerClosure.tiffFilename))
						{
							capturedLocals6.tiffWidth = floatTiffReader.GetImageWidth(0);
							capturedLocals6.tiffHeight = floatTiffReader.GetImageHeight(0);
						}
						capturedLocals6.minX = location[0];
						capturedLocals6.maxY = location[3];
						capturedLocals6.cellSize = location[1];
						capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.minCellSize = System.Math.Min(capturedLocals6.outerClosure.outerClosure.outerClosure.outerClosure.minCellSize, capturedLocals6.cellSize);
						_cw.TimeIt("Step 4 of 4: Adding Overlays...", capturedLocals6._Lambda_0024__8);
						if (func())
						{
							return;
						}
						goto IL_0767;
						IL_0767:
						capturedLocals5.outerClosure.outerClosure.outerClosure.processedTiffs.Add(capturedLocals5.outerClosure.tiffFilename);
						stopwatch2.Stop();
						_cw.AddTimeFormattedLine(System.IO.Path.GetFileName(capturedLocals5.outerClosure.outerClosure.inputFilename) + " Import Complete.", StopwatchExtensionMethods.PrintPrettyTimeHMS(stopwatch2), Color.ForestGreen);
						_cw.AddBarrier();
					}
					catch (Exception ex3)
					{
						ProjectData.SetProjectError(ex3);
						Exception ex4 = ex3;
						_cw.AddLine("Error creating Terrain: " + ex4.Message, Color.Red);
						_cw.CleanFilesPrintStatus(capturedLocals4.outerClosure.outerClosure.filesToClean);
						ProjectData.ClearProjectError();
						return;
					}
				}
			}
			ListExtensionMethods.RemoveRange(capturedLocals2.processedTiffs, stringList);
			_cw.AddLine("Final Processing: " + System.IO.Path.GetFileName(_terrainFilename));
			_cw.TimeIt("Step 1 of 3: Creating " + System.IO.Path.GetFileName(capturedLocals2.vrtFilename) + "...", [SpecialName] () =>
			{
				InterpolatedLayer.CreateVRT(capturedLocals2.processedTiffs.ToArray(), capturedLocals2.vrtFilename, capturedLocals2.reporter);
			});
			capturedLocals2.filesToClean.Add(capturedLocals2.vrtFilename);
			if (func())
			{
				return;
			}
			try
			{
				_cw.TimeIt("Step 2 of 3: Creating " + System.IO.Path.GetFileName(_terrainFilename + "..."), [SpecialName] () =>
				{
					Geospatial.Terrain.Terrain.GenerateTerrain(capturedLocals2.terrainComputable._terrainFilename, capturedLocals2.processedTiffs, capturedLocals2.reporter, capturedLocals2.terrainComputable._writeStitchInformation);
				});
				if (capturedLocals2.reporter.CancelRequested)
				{
					_cw.CleanFilesPrintStatus(capturedLocals2.filesToClean);
					return;
				}
				if (_writeStitchInformation)
				{
					_cw.TimeIt("Step 3 of 3: Creating Stitch-TIN for merging rasters...", [SpecialName] () =>
					{
						StitchCache stitches = new StitchCache(capturedLocals2.terrainComputable._terrainFilename, Geospatial.Terrain.Terrain.GetStitchesName());
						double nearPointTolerance = System.Math.Min(0.01, capturedLocals2.minCellSize * 0.1);
						DevelopmentTin developmentTin = new DelaunayTINFactory(nearPointTolerance).ComputeTIN(null, null, null, stitches, removeDeletedTriangles: true, double.NaN, capturedLocals2.reporter);
						if (!capturedLocals2.reporter.CancelRequested)
						{
							using (H5Writer hw = new H5Writer(capturedLocals2.terrainComputable._terrainFilename))
							{
								using (HDFBufferedWriter<double> hDFBufferedWriter = new HDFBufferedWriter<double>(hw, Geospatial.Terrain.Terrain.GetStitchTinName() + " Points", developmentTin.Points.Count, 4, "Point", "X", "Y", "Z", "M"))
								{
									int count = developmentTin.Points.Count - 1;
									for (int i = 0; i <= count; i++)
									{
										PointM pointM = developmentTin.Points[i];
										hDFBufferedWriter.AddRow(pointM.X, pointM.Y, pointM.Z, pointM.M);
									}
								}
								using (HDFBufferedWriter<int> hDFBufferedWriter2 = new HDFBufferedWriter<int>(hw, Geospatial.Terrain.Terrain.GetStitchTinName() + " Triangles", developmentTin.Triangles.Count, 3, "Triangle", "p0", "p1", "p2"))
								{
									int count2 = developmentTin.Triangles.Count - 1;
									for (int j = 0; j <= count2; j++)
									{
										DevelopmentTriangle developmentTriangle = developmentTin.Triangles[j];
										hDFBufferedWriter2.AddRow(developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.p2);
									}
								}
								SharedData.HDFWriteSRS(hw);
								SharedData.HDFWriteFileType(hw, "HEC Terrain");
							}
						}
					});
				}
				else
				{
					_cw.TimeIt("Step 3 of 3: Creating Stitch-TIN for merging rasters... (USER-SKIPPED)", [SpecialName] () =>
					{
					});
				}
				if (capturedLocals2.reporter.CancelRequested)
				{
					_cw.CleanFilesPrintStatus(capturedLocals2.filesToClean);
					return;
				}
				capturedLocals2.reporter.ReportProgress(100);
				stopwatch.Stop();
				_cw.AddBarrier();
				_cw.AddTimeFormattedLine("Terrain Complete", StopwatchExtensionMethods.PrintPrettyTimeHMS(stopwatch), Color.ForestGreen);
			}
			catch (Exception ex5)
			{
				ProjectData.SetProjectError(ex5);
				Exception ex6 = ex5;
				_cw.AddLine("Error creating Terrain: " + ex6.Message, Color.Red);
				_cw.CleanFilesPrintStatus(capturedLocals2.filesToClean);
				ProjectData.ClearProjectError();
			}
		}
	}

	void IAmComputable.Run(ProgressReporter reporter)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Run
		this.Run(reporter);
	}

	public void Complete()
	{
		if (!File.Exists(_terrainFilename))
		{
			return;
		}
		RASMapper rasMapper = SharedData.RasMapper;
		if (rasMapper != null)
		{
			TerrainLayer terrainLayer = rasMapper.TerrainsGroup.EnsureTerrain(_terrainFilename);
			if (!rasMapper.TerrainsGroup.IsExpanded)
			{
				rasMapper.TerrainsGroup.Expand();
			}
			terrainLayer.Checked = true;
			rasMapper.TerrainsGroup.Checked = true;
			terrainLayer.ZoomToLayer();
		}
	}

	void IAmComputable.Complete()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Complete
		this.Complete();
	}

	private float GetVerticalScaleFactor(Projection fileProjection)
	{
		if (_projectProjection == null || fileProjection == null)
		{
			return 1f;
		}
		string unitsString = _projectProjection.GetSanitizedLinearUnitName().ToLowerInvariant();
		bool checkForMeters = CheckForMeters(unitsString);
		bool checkForFeet = CheckForFeet(unitsString);
		string unitsString2 = fileProjection.GetSanitizedLinearUnitName().ToLowerInvariant();
		bool checkForMeters2 = CheckForMeters(unitsString2);
		bool checkForFeet2 = CheckForFeet(unitsString2);
		if (!checkForMeters2 && !checkForFeet2)
		{
			string unitsString3 = fileProjection.GetLinearUnitsName().ToLowerInvariant();
			checkForMeters2 = CheckForMeters(unitsString3);
			checkForFeet2 = CheckForFeet(unitsString3);
		}
		if (checkForMeters && checkForFeet2)
		{
			return 0.3048f;
		}
		if (checkForFeet && checkForMeters2)
		{
			return 3.28084f;
		}
		return 1f;
	}

	private static bool CheckForMeters(string unitsString)
	{
		unitsString = unitsString.ToLowerInvariant();
		if (!unitsString.Contains("meter") && !unitsString.Contains("meters") && !unitsString.Contains("metre"))
		{
			return Operators.CompareString(unitsString, "m", TextCompare: false) == 0;
		}
		return true;
	}

	private static bool CheckForFeet(string unitsString)
	{
		unitsString = unitsString.ToLowerInvariant();
		if (!unitsString.Contains("feet") && !unitsString.Contains("ft"))
		{
			return unitsString.Contains("foot");
		}
		return true;
	}

	private void GenerateFilename(ref string inputFilename, ref string tiffFilename, IList<string> alreadyProcessedFiles)
	{
		_Closure_0024__20_002D0 capturedLocals = default;
		_Closure_0024__20_002D0 capturedLocals2 = new _Closure_0024__20_002D0(capturedLocals);
		capturedLocals2.terrainComputable = this;
		capturedLocals2.alreadyProcessedFiles = alreadyProcessedFiles;
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(inputFilename);
		Func<string, string> func = [SpecialName] (string coreFN) =>
		{
			string capturedLocals3 = capturedLocals2.terrainComputable._baseFolder + capturedLocals2.terrainComputable._terrainName + "." + coreFN + ".tif";
			if (!capturedLocals2.alreadyProcessedFiles.Contains(capturedLocals3))
			{
				return capturedLocals3;
			}
			int ordinal = 1;
			while (true)
			{
				capturedLocals3 = capturedLocals2.terrainComputable._baseFolder + capturedLocals2.terrainComputable._terrainName + "." + coreFN + "_" + ordinal + ".tif";
				if (!capturedLocals2.alreadyProcessedFiles.Contains(capturedLocals3))
				{
					break;
				}
				ordinal = checked(ordinal + 1);
			}
			return capturedLocals3;
		};
		if (Operators.CompareString(System.IO.Path.GetExtension(inputFilename).ToLower(), ".adf", TextCompare: false) == 0)
		{
			string folderName = RasMapperLib.Utilities.Path.FolderName(inputFilename);
			if (string.IsNullOrWhiteSpace(folderName))
			{
				folderName = Guid.NewGuid().ToString();
			}
			tiffFilename = func(folderName);
			inputFilename = System.IO.Path.GetDirectoryName(inputFilename);
		}
		else
		{
			tiffFilename = func(fileNameWithoutExtension);
		}
	}

	public static bool IsRASTerrainTiff(string filename, ref RoundTo roundingUsed)
	{
		bool result;
		try
		{
			if (!File.Exists(filename))
			{
				result = false;
			}
			else if (Operators.CompareString(System.IO.Path.GetExtension(filename), ".tif", TextCompare: false) != 0)
			{
				result = false;
			}
			else if (!TiffBase.IsIEEFPData(filename))
			{
				result = false;
			}
			else
			{
				using (FloatTiffReader floatTiffReader = new FloatTiffReader(filename))
				{
					if (floatTiffReader.GetNumBitsPerSample(0) != 32)
					{
						result = false;
					}
					else if (!floatTiffReader.IsTiled(0))
					{
						result = false;
					}
					else if (floatTiffReader.GetTileHeight(0) != 256 || floatTiffReader.GetTileWidth(0) != 256)
					{
						result = false;
					}
					else
					{
						TiffMetadata<float> tiffMetadata = floatTiffReader.GetTiffMetadata(0);
						roundingUsed = tiffMetadata.Rounding;
						if (tiffMetadata.NoDataValue != -9999f)
						{
							result = false;
						}
						else if (InterpolatedLayer.GetDefaultOverlayString(System.Math.Max(floatTiffReader.GetImageHeight(0), floatTiffReader.GetImageWidth(0))).Split(new string[1] { " " }, StringSplitOptions.RemoveEmptyEntries).Length != checked(floatTiffReader.GetNumDirectories() - 1))
						{
							result = false;
						}
						else
						{
							string compressionUsed = tiffMetadata.CompressionUsed;
							if (compressionUsed != null && Operators.CompareString(compressionUsed.ToLower(), "adobe_deflate", TextCompare: false) == 0)
							{
								goto end_IL_003f;
							}
							result = false;
						}
					}
					goto end_IL_0000;
					end_IL_003f:;
				}
				result = !TiffReader<float>.GetHistogram(filename).IsNullOrEmpty();
			}
			end_IL_0000:;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	private TranslationTypeNeeded GetFileTranslationType(Projection fileProjection, Extent fileExt = null)
	{
		if (_forceWarp)
		{
			return TranslationTypeNeeded.Warp;
		}
		if (_projectProjection == null || _projectProjection.IsValid() == SRSValidation.Corrupt)
		{
			return TranslationTypeNeeded.TranslateWithoutSRS;
		}
		if (fileProjection == null || WoodysDeepProjectionEqual(_projectProjection, fileProjection, fileExt))
		{
			return TranslationTypeNeeded.TranslateWithSRS;
		}
		return TranslationTypeNeeded.Warp;
	}

	public bool WoodysDeepProjectionEqual(Projection projection1, Projection projection2, Extent projection1Extents = null)
	{
		if ((projection1.IsValid() == SRSValidation.Corrupt) | (projection2.IsValid() == SRSValidation.Corrupt))
		{
			return false;
		}
		Projection.ClearStoredSRS();
		if (projection1.IsEqual(projection2))
		{
			return true;
		}
		if (projection1Extents == null)
		{
			return false;
		}
		Func<double, double, double, double, bool> func = [SpecialName] (double x1, double y1, double x2, double y2) => System.Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2)) <= 0.001;
		Func<double[], double[], bool> func2 = [SpecialName] (double[] xCoords, double[] yCoords) =>
		{
			if (!func(projection1Extents.MinX, projection1Extents.MinY, xCoords[0], yCoords[0]))
			{
				return false;
			}
			if (!func(projection1Extents.MaxX, projection1Extents.MinY, xCoords[1], yCoords[1]))
			{
				return false;
			}
			if (!func(projection1Extents.MinX, projection1Extents.MaxY, xCoords[2], yCoords[2]))
			{
				return false;
			}
			return func(projection1Extents.MaxX, projection1Extents.MaxY, xCoords[3], yCoords[3]) ? true : false;
		};
		try
		{
			Proj4Projection proj4Projection = ((!(projection1.GetType() == typeof(Proj4Projection))) ? new Proj4Projection(projection1.GetProj4()) : ((Proj4Projection)projection1));
			Proj4Projection proj4Projection2 = ((!(projection2.GetType() == typeof(Proj4Projection))) ? new Proj4Projection(projection2.GetProj4()) : ((Proj4Projection)projection2));
			if (proj4Projection.IsEqual(proj4Projection2))
			{
				return true;
			}
			double[] doubleValues = new double[4];
			double[] doubleValuesArray = new double[4];
			double[] z = new double[4];
			Extent extent = projection1Extents;
			doubleValues[0] = extent.MinX;
			doubleValuesArray[0] = extent.MinY;
			doubleValues[1] = extent.MaxX;
			doubleValuesArray[1] = extent.MinY;
			doubleValues[2] = extent.MinX;
			doubleValuesArray[2] = extent.MaxY;
			doubleValues[3] = extent.MaxX;
			doubleValuesArray[3] = extent.MaxY;
			Projector.TransformPoints(proj4Projection, proj4Projection2, doubleValues, doubleValuesArray, z);
			if (func2(doubleValues, doubleValuesArray))
			{
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			Projection.ClearStoredSRS();
			ProjectData.ClearProjectError();
		}
		try
		{
			WKTProjection wKTProjection = ((!(projection1.GetType() == typeof(WKTProjection))) ? new WKTProjection(projection1.GetWKT()) : ((WKTProjection)projection1));
			WKTProjection wKTProjection2 = ((!(projection2.GetType() == typeof(WKTProjection))) ? new WKTProjection(projection2.GetWKT()) : ((WKTProjection)projection2));
			if (wKTProjection.IsEqual(wKTProjection2))
			{
				return true;
			}
			double[] doubleValues2 = new double[4];
			double[] doubleValues3 = new double[4];
			double[] z2 = new double[4];
			Extent extent2 = projection1Extents;
			doubleValues2[0] = extent2.MinX;
			doubleValues3[0] = extent2.MinY;
			doubleValues2[1] = extent2.MaxX;
			doubleValues3[1] = extent2.MinY;
			doubleValues2[2] = extent2.MinX;
			doubleValues3[2] = extent2.MaxY;
			doubleValues2[3] = extent2.MaxX;
			doubleValues3[3] = extent2.MaxY;
			Projector.TransformPoints(wKTProjection, wKTProjection2, doubleValues2, doubleValues3, z2);
			if (func2(doubleValues2, doubleValues3))
			{
				return true;
			}
			return false;
		}
		catch (Exception ex3)
		{
			ProjectData.SetProjectError(ex3);
			Exception ex4 = ex3;
			Projection.ClearStoredSRS();
			ProjectData.ClearProjectError();
		}
		try
		{
			ESRIProjection eSRIProjection;
			if (projection1.GetType() == typeof(ESRIProjection))
			{
				eSRIProjection = (ESRIProjection)projection1;
			}
			else
			{
				string getTempPathPathTerm = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".prj";
				if (File.Exists(getTempPathPathTerm))
				{
					getTempPathPathTerm = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".prj";
				}
				projection1.ExportEsri(getTempPathPathTerm);
				eSRIProjection = new ESRIProjection(getTempPathPathTerm);
				File.Delete(getTempPathPathTerm);
			}
			ESRIProjection eSRIProjection2;
			if (projection2.GetType() == typeof(ESRIProjection))
			{
				eSRIProjection2 = (ESRIProjection)projection2;
			}
			else
			{
				string getTempPathPathTerm2 = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".prj";
				if (File.Exists(getTempPathPathTerm2))
				{
					getTempPathPathTerm2 = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".prj";
				}
				projection2.ExportEsri(getTempPathPathTerm2);
				eSRIProjection2 = new ESRIProjection(getTempPathPathTerm2);
				File.Delete(getTempPathPathTerm2);
			}
			if (eSRIProjection.IsEqual(eSRIProjection2))
			{
				return true;
			}
			double[] doubleValues4 = new double[4];
			double[] doubleValues5 = new double[4];
			double[] z3 = new double[4];
			Extent extent3 = projection1Extents;
			doubleValues4[0] = extent3.MinX;
			doubleValues5[0] = extent3.MinY;
			doubleValues4[1] = extent3.MaxX;
			doubleValues5[1] = extent3.MinY;
			doubleValues4[2] = extent3.MinX;
			doubleValues5[2] = extent3.MaxY;
			doubleValues4[3] = extent3.MaxX;
			doubleValues5[3] = extent3.MaxY;
			Projector.TransformPoints(eSRIProjection, eSRIProjection2, doubleValues4, doubleValues5, z3);
			if (func2(doubleValues4, doubleValues5))
			{
				return true;
			}
			return false;
		}
		catch (Exception ex5)
		{
			ProjectData.SetProjectError(ex5);
			Exception ex6 = ex5;
			Projection.ClearStoredSRS();
			ProjectData.ClearProjectError();
		}
		return false;
	}
}
