using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Progress;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class LandCoverComputable : IAmComputable
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D0
	{
		public int[] tile;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				tile = other.tile;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D1
	{
		public int polyVal;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D1(_Closure_0024__15_002D1 other)
		{
			if (other != null)
			{
				polyVal = other.polyVal;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int n)
		{
			outerClosure.tile[n] = polyVal;
		}
	}

	private Projection _projectProjection;

	private List<LandCoverFile> _inputFiles;

	private string _outputTiffFilename;

	private string _outputHDF5Filename;

	private float _outputCellSize;

	private string[] _names;

	private int[] _ids;

	private List<float[]> _extras;

	private LandCoverLayerHelper _lcHelper;

	private Extent _desiredExtent;

	private IDisplayProgress _cw;

	private bool _success;

	private List<int> _selectedPayloadColumnIdxs;

	public LandCoverComputable(string outputFilename, float cellSize, Extent restrictToExtent, List<LandCoverFile> lcInputs, string[] names, int[] ids, LandCoverLayerHelper lcHelper, List<float[]> extraData, List<int> payloadColumnIdxs)
	{
		_projectProjection = SharedData.SRSProjection;
		_inputFiles = lcInputs;
		_outputTiffFilename = System.IO.Path.ChangeExtension(outputFilename, ".tif");
		_outputHDF5Filename = outputFilename;
		_outputCellSize = cellSize;
		_names = names;
		_ids = ids;
		_extras = extraData;
		_lcHelper = lcHelper;
		_desiredExtent = restrictToExtent;
		_selectedPayloadColumnIdxs = payloadColumnIdxs;
		if (extraData.IsNullOrEmpty() && payloadColumnIdxs.Count > 0)
		{
			throw new ArgumentException();
		}
		if (extraData.Count != payloadColumnIdxs.Count)
		{
			throw new ArgumentException();
		}
	}

	public void Initialize(IDisplayProgress cw)
	{
		_cw = cw;
		_cw.SetHeaderText("Compute Window - Creating Mannings n Layer '" + System.IO.Path.GetFileName(_outputHDF5Filename) + "'");
	}

	void IAmComputable.Initialize(IDisplayProgress cw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Initialize
		this.Initialize(cw);
	}

	public void Run(ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Stopwatch sw = new Stopwatch();
		Stopwatch stopwatch = Stopwatch.StartNew();
		Stopwatch stopwatch2 = new Stopwatch();
		string StdOut = "";
		string StdErr = "";
		List<string> stringList = new List<string>();
		_success = false;
		_cw.AddTimeFormattedLine("Computation Task", "hh:mm:ss");
		_cw.AddBarrier();
		Action<char> action = [SpecialName] (char c) =>
		{
			_cw.AddChar(c, Color.Red);
		};
		_cw.AddLine("PREPROCESSING: Ensuring rasters are in the correct projection...");
		stopwatch2.Start();
		if (_projectProjection == null)
		{
			_cw.AddLine("Project projection not found. Every input file is presumed to be in the same projection as the project.", Color.Red);
		}
		foreach (LandCoverFile inputFile in _inputFiles)
		{
			reporter.ReportProgress(0);
			if (inputFile.IsRaster)
			{
				bool condition = false;
				Projection projection = GDALRaster.GetProjection(inputFile.Filename);
				if (_projectProjection != null)
				{
					if (projection == null)
					{
						_cw.AddLine(inputFile.ShortName + ": Could not find a projection. Assuming project projection.");
					}
					else if (projection.IsValid() == SRSValidation.Corrupt)
					{
						_cw.AddLine(inputFile.ShortName + ": Corrupted/unknown projection. Assuming project projection.");
					}
					else if (_projectProjection.IsEqual(projection))
					{
						_cw.AddLine(inputFile.ShortName + ": Matches project projection.");
					}
					else
					{
						condition = true;
					}
				}
				if (inputFile.ToBeCropped)
				{
					if (condition)
					{
						_cw.StepStart(sw, inputFile.ShortName + " has a different projection. Warping to temp file...");
						string tempFileName = System.IO.Path.GetTempFileName();
						Geospatial.Vectors.Extent boundingBox = Projector.GetBoundingBox(inputFile.DesiredExtents, inputFile.LocalProjection, _projectProjection);
						Executables.GdalWarp("-s_srs " + projection.GetProj4().InDoubleQuotes() + " -t_srs " + SharedData.SRS_GDAL_CMS + " -te " + boundingBox.Left + " " + boundingBox.Bottom + " " + boundingBox.Right + " " + boundingBox.Top + " -dstnodata " + inputFile.NoDataValue + " -of GTIFF -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 " + inputFile.Filename.InDoubleQuotes() + " " + tempFileName.InDoubleQuotes(), out StdOut, out StdErr, _cw.GDALProgressReporter, action, _cw.SetActiveProcess);
						_cw.StepStop(sw);
						if (!File.Exists(tempFileName))
						{
							_cw.AddLine("Error: " + inputFile.Filename + " could not be warped to " + tempFileName, Color.Red);
							return;
						}
						inputFile.Filename = tempFileName;
						stringList.Add(tempFileName);
						Geospatial.Vectors.Extent fullNativeExtents = GDALRaster.GetFullNativeExtents(inputFile.Filename);
						inputFile.Extent = new Extent(fullNativeExtents.Right, fullNativeExtents.Left, fullNativeExtents.Top, fullNativeExtents.Bottom, double.NaN, double.NaN, double.NaN, double.NaN);
					}
					else
					{
						_cw.StepStart(sw, "Cropping " + inputFile.ShortName);
						string tempFileName2 = System.IO.Path.GetTempFileName();
						PixelRange desiredPixelExtents = inputFile.DesiredPixelExtents;
						Executables.GdalTranslate("-srcwin " + desiredPixelExtents.Left + " " + desiredPixelExtents.Top + " " + desiredPixelExtents.Width + " " + desiredPixelExtents.Height + " -of GTIFF -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 " + inputFile.Filename.InDoubleQuotes() + " " + tempFileName2.InDoubleQuotes(), out StdOut, out StdErr, _cw.GDALProgressReporter, action, _cw.SetActiveProcess);
						_cw.StepStop(sw);
						if (!File.Exists(tempFileName2))
						{
							_cw.AddLine("Error: " + inputFile.Filename + " was not successfully copied to " + tempFileName2, Color.Red);
							return;
						}
						inputFile.Filename = tempFileName2;
						stringList.Add(tempFileName2);
						Geospatial.Vectors.Extent fullNativeExtents2 = GDALRaster.GetFullNativeExtents(inputFile.Filename);
						inputFile.Extent = new Extent(fullNativeExtents2.Right, fullNativeExtents2.Left, fullNativeExtents2.Top, fullNativeExtents2.Bottom, double.NaN, double.NaN, double.NaN, double.NaN);
					}
				}
				else if (condition)
				{
					_cw.StepStart(sw, inputFile.ShortName + " has a different projection. Warping to temp file...");
					string tempFileName3 = System.IO.Path.GetTempFileName();
					Executables.GdalWarp("-s_srs " + projection.GetProj4().InDoubleQuotes() + " -t_srs " + SharedData.SRS_GDAL_CMS + " -dstnodata " + inputFile.NoDataValue + " -of GTIFF -co TILED=YES -co COMPRESS=DEFLATE -co ZLEVEL=1 " + inputFile.Filename.InDoubleQuotes() + " " + tempFileName3.InDoubleQuotes(), out StdOut, out StdErr, _cw.GDALProgressReporter, action, _cw.SetActiveProcess);
					_cw.StepStop(sw);
					if (!File.Exists(tempFileName3))
					{
						_cw.AddLine("Error: " + inputFile.Filename + " could not be warped to " + tempFileName3, Color.Red);
						return;
					}
					inputFile.Filename = tempFileName3;
					stringList.Add(tempFileName3);
					Geospatial.Vectors.Extent fullNativeExtents3 = GDALRaster.GetFullNativeExtents(inputFile.Filename);
					inputFile.Extent = new Extent(fullNativeExtents3.Right, fullNativeExtents3.Left, fullNativeExtents3.Top, fullNativeExtents3.Bottom, double.NaN, double.NaN, double.NaN, double.NaN);
				}
				else
				{
					Geospatial.Vectors.Extent fullNativeExtents4 = GDALRaster.GetFullNativeExtents(inputFile.Filename);
					inputFile.Extent = new Extent(fullNativeExtents4.Right, fullNativeExtents4.Left, fullNativeExtents4.Top, fullNativeExtents4.Bottom, double.NaN, double.NaN, double.NaN, double.NaN);
				}
			}
			else
			{
				_cw.AddLine(inputFile.ShortName + " is a vector file. Projection will be done on-the-fly if necessary.");
			}
			reporter.ReportProgress(100);
		}
		stopwatch2.Stop();
		_cw.AddTimeFormattedLine("Preprocessing complete.", StopwatchExtensionMethods.PrintPrettyTimeHMS(stopwatch2), Color.ForestGreen);
		_cw.AddBarrier();
		Extent extent = _desiredExtent;
		if (extent == null)
		{
			foreach (LandCoverFile inputFile2 in _inputFiles)
			{
				extent = new Extent(extent, inputFile2.Extent);
			}
		}
		SpatialIndex<Tuple<LandCoverFile, int>> spatialIndex = new SpatialIndex<Tuple<LandCoverFile, int>>(extent);
		checked
		{
			int count = _inputFiles.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				LandCoverFile landCoverFile = _inputFiles[intervalIndex];
				if (landCoverFile.IsRaster)
				{
					Extent extent2 = landCoverFile.Extent.IntersectionXY(extent);
					spatialIndex.Add(extent2, new Tuple<LandCoverFile, int>(landCoverFile, 0));
					continue;
				}
				foreach (int polygonsInBound in landCoverFile.PolygonsInBounds)
				{
					Extent extent3 = ((Polygon)landCoverFile.PolygonLayer.Feature(polygonsInBound)).ComputeExtent().IntersectionXY(extent);
					if (extent3 != null)
					{
						spatialIndex.Add(extent3, new Tuple<LandCoverFile, int>(landCoverFile, polygonsInBound));
					}
				}
			}
			_cw.StepStart(sw, "Creating output file " + System.IO.Path.GetFileName(_outputTiffFilename) + "...");
			if (File.Exists(_outputTiffFilename))
			{
				File.Delete(_outputTiffFilename);
			}
			Dictionary<LandCoverFile, GDALRaster> gdalRasterByLandCoverFile = new Dictionary<LandCoverFile, GDALRaster>();
			try
			{
				foreach (LandCoverFile inputFile3 in _inputFiles)
				{
					if (inputFile3.IsRaster)
					{
						GDALRaster filename = new GDALRaster(inputFile3.Filename);
						gdalRasterByLandCoverFile.Add(inputFile3, filename);
					}
				}
				int systemMaxXTerm = (int)System.Math.Round((extent.MaxX - extent.MinX) / (double)_outputCellSize);
				int systemMaxYTerm = (int)System.Math.Round((extent.MaxY - extent.MinY) / (double)_outputCellSize);
				TiffMetadata<int> metadata = new TiffMetadata<int>(0, RoundTo.None);
				using (IntTiffWriter intTiffWriter = new IntTiffWriter(_outputTiffFilename, systemMaxXTerm, systemMaxYTerm, metadata))
				{
					int numTilesWide = intTiffWriter.GetNumTilesWide();
					int numTilesTall = intTiffWriter.GetNumTilesTall();
					int numTilesWideTimesNumTilesTall = numTilesWide * numTilesTall;
					Extent ext = new Extent(extent.MinX + (double)((float)(numTilesWide * 256) * _outputCellSize), extent.MinX, extent.MaxY, extent.MaxY - (double)((float)(numTilesTall * 256) * _outputCellSize));
					RasterM rasterM = new RasterM(numTilesTall, numTilesWide, ext);
					RasterM raster = spatialIndex.Raster;
					RasterizePolygon rasterizePolygon = null;
					int count2 = numTilesTall - 1;
					_Closure_0024__15_002D0 capturedLocals = default;
					_Closure_0024__15_002D1 capturedLocals2 = default;
					for (int ordinal = 0; ordinal <= count2; ordinal++)
					{
						int count3 = numTilesWide - 1;
						for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
						{
							capturedLocals = new _Closure_0024__15_002D0(capturedLocals);
							int numTilesWideTerm = ordinal * numTilesWide + ordinal2;
							double minX = rasterM.CellMinX(ordinal2);
							double maxX = rasterM.CellMaxX(ordinal2);
							double minY = rasterM.CellMinY(ordinal);
							double maxY = rasterM.CellMaxY(ordinal);
							Geospatial.Vectors.Extent bbox = Converter.Convert(new Extent(maxX, minX, maxY, minY));
							RasterM rasterM2 = new RasterM(256, 256, maxX, minX, maxY, minY);
							if (rasterizePolygon == null)
							{
								rasterizePolygon = new RasterizePolygon(rasterM2);
							}
							else
							{
								rasterizePolygon.ResetRaster(rasterM2);
							}
							capturedLocals.tile = new int[65536];
							RowColRangeM rowColRangeM = raster.RowColRange(rasterM, ordinal2, ordinal);
							List<Tuple<LandCoverFile, int>> valueTupleList = new List<Tuple<LandCoverFile, int>>();
							int minRow = rowColRangeM.MinRow;
							int maxRow = rowColRangeM.MaxRow;
							for (int minRow2 = minRow; minRow2 <= maxRow; minRow2++)
							{
								int minCol = rowColRangeM.MinCol;
								int maxCol = rowColRangeM.MaxCol;
								for (int minCol2 = minCol; minCol2 <= maxCol; minCol2++)
								{
									valueTupleList.AddRange(spatialIndex.get_Bin(minRow2, minCol2));
								}
							}
							foreach (Tuple<LandCoverFile, int> tuple in from s in valueTupleList.Distinct()
								orderby _inputFiles.IndexOf(s.Item1) descending
								select s)
							{
								LandCoverFile landCoverFile2 = tuple.Item1;
								if (landCoverFile2.IsRaster)
								{
									if (gdalRasterByLandCoverFile[landCoverFile2].GetPixelCorners(bbox) != PixelRange.NullRange)
									{
										throw new NotImplementedException();
									}
									continue;
								}
								capturedLocals2 = new _Closure_0024__15_002D1(capturedLocals2);
								capturedLocals2.outerClosure = capturedLocals;
								int ordinal3 = tuple.Item2;
								Polygon polygon = (Polygon)landCoverFile2.PolygonLayer.Feature(ordinal3);
								if (polygon.ComputeExtent().Overlaps(rasterM2.ComputeExtent()))
								{
									capturedLocals2.polyVal = landCoverFile2.PolygonLookup(ordinal3);
									rasterizePolygon.ComputeCells(polygon, capturedLocals2._Lambda_0024__2);
								}
							}
							intTiffWriter.WriteTile(numTilesWideTerm, capturedLocals.tile);
							reporter.ReportProgress((int)System.Math.Round((double)(100 * (numTilesWideTerm + 1)) / (double)numTilesWideTimesNumTilesTall));
							if (reporter.CancelRequested)
							{
								_success = false;
								reporter.ReportProgress(0);
								return;
							}
						}
					}
				}
				_cw.StepStop(sw);
				reporter.ReportProgress(1);
				_cw.StepStart(sw, "Adding Overlays...");
				InterpolatedLayer.AddSRSGeorefAddo(_outputTiffFilename, systemMaxXTerm, systemMaxYTerm, extent.MinX, extent.MaxY, _outputCellSize, UseNearest: true, _cw.GDALProgressReporter, action, _cw.SetActiveProcess);
				_cw.StepStop(sw);
				reporter.ReportProgress(100);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				_cw.AddLine("An error occurred: " + ex2.Message, Color.Red);
				ProjectData.ClearProjectError();
				return;
			}
			finally
			{
				foreach (GDALRaster gdalRaster in gdalRasterByLandCoverFile.Values)
				{
					gdalRaster.Dispose();
				}
				foreach (string textValue in stringList)
				{
					File.Delete(textValue);
				}
			}
			_cw.AddBarrier();
			_cw.StepStart(sw, "Creating " + System.IO.Path.GetFileName(_outputHDF5Filename) + "...");
			File.Delete(System.IO.Path.GetFileName(_outputHDF5Filename));
			reporter.ReportProgress(0);
			using (H5Writer h5Writer = new H5Writer(_outputHDF5Filename))
			{
				h5Writer.WriteArray("Names", _names);
				h5Writer.WriteArray("IDs", _ids);
				int count4 = _selectedPayloadColumnIdxs.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count4; intervalIndex2++)
				{
					h5Writer.WriteArray(_lcHelper.ExtraColumnDatasets[_selectedPayloadColumnIdxs[intervalIndex2]], _extras[intervalIndex2]);
				}
				h5Writer.WriteAttribute("/", "GUID", Guid.NewGuid().ToString());
				SharedData.HDFWriteSRS(h5Writer);
				SharedData.HDFWriteFileType(h5Writer, "HEC Land Cover");
			}
			_cw.StepStop(sw);
			reporter.ReportProgress(100);
			stopwatch.Stop();
			_cw.AddTimeFormattedLine("Land cover Layer complete!", StopwatchExtensionMethods.PrintPrettyTimeHMS(stopwatch), Color.ForestGreen);
			_success = true;
		}
	}

	void IAmComputable.Run(ProgressReporter reporter)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Run
		this.Run(reporter);
	}

	public void Complete()
	{
		_ = _success;
	}

	void IAmComputable.Complete()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Complete
		this.Complete();
	}

	public bool Success()
	{
		return _success;
	}
}
