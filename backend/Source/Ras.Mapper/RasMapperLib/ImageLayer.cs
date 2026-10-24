using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using TiffAssist;

namespace RasMapperLib;

public class ImageLayer : Layer
{
	private struct TransformCoefficients77
	{
		public float A;

		public float B;

		public float C;

		public float D;

		public float E;

		public float F;

		public float G;

		public float H;

		public TransformCoefficients77(double[,] rhs, int m)
		{
			this = default;
			A = (float)rhs[0, m];
			B = (float)rhs[1, m];
			C = (float)rhs[2, m];
			D = (float)rhs[3, m];
			E = (float)rhs[4, m];
			F = (float)rhs[5, m];
			G = (float)rhs[6, m];
			H = (float)rhs[7, m];
		}

		public void RasterRowCoefficients(float tr, ref float intercept, ref float coefSC, ref float coefSC2)
		{
			intercept = A + (C + F * tr) * tr;
			coefSC = B + (D + H * tr) * tr;
			coefSC2 = E + G * tr;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__40_002D0
	{
		public TransformCoefficients77 coefCol;

		public TransformCoefficients77 coefRow;

		public RasterM targetRaster;

		public int sColMax;

		public int sRowMax;

		public RasterM sourceRaster;

		public float[] sourceData;

		public float srcNoData;

		public float[] targetData;

		public _Closure_0024__40_002D0(_Closure_0024__40_002D0 other)
		{
			if (other != null)
			{
				coefCol = other.coefCol;
				coefRow = other.coefRow;
				targetRaster = other.targetRaster;
				sColMax = other.sColMax;
				sRowMax = other.sRowMax;
				sourceRaster = other.sourceRaster;
				sourceData = other.sourceData;
				srcNoData = other.srcNoData;
				targetData = other.targetData;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int tr)
		{
			float intercept = default;
			float coefSC = default;
			float coefSC2 = default;
			coefCol.RasterRowCoefficients(tr, ref intercept, ref coefSC, ref coefSC2);
			float intercept2 = default;
			float coefSC3 = default;
			float coefSC4 = default;
			coefRow.RasterRowCoefficients(tr, ref intercept2, ref coefSC3, ref coefSC4);
			checked
			{
				int trTimesCols = tr * targetRaster.Cols;
				int colsOffset = targetRaster.Cols - 1;
				for (int i = 0; i <= colsOffset; i++)
				{
					int systemInterceptTerm = (int)System.Math.Round(intercept + (coefSC + coefSC2 * (float)i) * (float)i);
					if (systemInterceptTerm >= 0 && systemInterceptTerm <= sColMax)
					{
						int systemIntercept2Term = (int)System.Math.Round(intercept2 + (coefSC3 + coefSC4 * (float)i) * (float)i);
						if (systemIntercept2Term >= 0 && systemIntercept2Term <= sRowMax)
						{
							int cellIndex = sourceRaster.CellIndex(systemIntercept2Term, systemInterceptTerm);
							float sourceData = this.sourceData[cellIndex];
							if (sourceData == srcNoData)
							{
								sourceData = -9999f;
							}
							targetData[trTimesCols] = sourceData;
						}
					}
					trTimesCols++;
				}
			}
		}
	}

	private Bitmap _bmp;

	private Projection _lastRenderedProjection;

	protected string _resampleMethod;

	private Projection _extentComputedWithLocalProjection;

	private Extent _nativeExtent;

	private Projection _nativeProjection;

	[CompilerGenerated]
	private RasterM _CurrentPlottedRaster;

	public string ResampleMethod
	{
		get
		{
			return _resampleMethod;
		}
		set
		{
			_resampleMethod = value;
			if (!Enumerable.Contains(SharedData.GDALResampleNames, _resampleMethod))
			{
				_resampleMethod = SharedData.GDALResampleNames.First();
			}
		}
	}

	public override string LayerPropertiesText { get; }

	public virtual byte DefaultAlphaValue => byte.MaxValue;

	public override Extent Extent
	{
		get
		{
			if (!SourceFileExists)
			{
				_extent = null;
				return _extent;
			}
			Projection sRSProjection = SharedData.SRSProjection;
			if (_extent == null || (sRSProjection != null && !sRSProjection.Equals(_extentComputedWithLocalProjection)))
			{
				try
				{
					using (GDALRaster gDALRaster = new GDALRaster(GetGDRName()))
					{
						if (sRSProjection != null && ((gDALRaster.GetProjection().IsValid() == SRSValidation.Valid) | (gDALRaster.GetProjection().IsValid() == SRSValidation.Unsupported)))
						{
							_extent = Converter.Convert(gDALRaster.GetProjectedExtents(sRSProjection));
						}
						else
						{
							_extent = Converter.Convert(gDALRaster.GetFullNativeExtents());
						}
					}
					_extentComputedWithLocalProjection = sRSProjection;
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					_extent = null;
					ProjectData.ClearProjectError();
				}
			}
			return _extent;
		}
	}

	public virtual Extent NativeExtent
	{
		get
		{
			if (!SourceFileExists)
			{
				_nativeExtent = null;
				return _nativeExtent;
			}
			if (_nativeExtent == null && !TryGetNativeExtent(ref _nativeExtent))
			{
				_nativeExtent = null;
			}
			return _nativeExtent;
		}
	}

	public virtual Projection NativeProjection
	{
		get
		{
			if (!SourceFileExists)
			{
				_nativeProjection = null;
				return _nativeProjection;
			}
			if (_nativeProjection == null && !TryGetNativeProjection(ref _nativeProjection))
			{
				_nativeProjection = null;
			}
			return _nativeProjection;
		}
	}

	public RasterM CurrentPlottedRaster
	{
		[CompilerGenerated]
		get
		{
			return _CurrentPlottedRaster;
		}
		[CompilerGenerated]
		set
		{
			_CurrentPlottedRaster = value;
		}
	}

	public virtual string GetGDRName()
	{
		return SourceFilename;
	}

	public virtual bool TryGetNativeExtent(ref Extent ext)
	{
		bool result;
		if (!SourceFileExists)
		{
			result = false;
		}
		else
		{
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(GetGDRName());
				ext = Converter.Convert(gDALRaster.GetFullNativeExtents());
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ext = null;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public virtual bool TryGetNativeRaster(ref RasterM raster)
	{
		bool result;
		if (!SourceFileExists)
		{
			result = false;
		}
		else
		{
			try
			{
				using GDALRaster gdr = new GDALRaster(GetGDRName());
				raster = new RasterM(gdr);
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				raster = null;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public virtual bool TryGetNativeProjection(ref Projection proj)
	{
		bool result;
		if (!SourceFileExists)
		{
			result = false;
		}
		else
		{
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(GetGDRName());
				proj = gDALRaster.GetProjection();
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				proj = null;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public ImageLayer()
		: this("Image Layer")
	{
	}

	public ImageLayer(string name)
		: base(name)
	{
		_bmp = null;
		_lastRenderedProjection = null;
		LayerPropertiesText = "Image Display Properties";
		Initialize();
	}

	public ImageLayer(string name, string filename)
		: base(name, filename)
	{
		_bmp = null;
		_lastRenderedProjection = null;
		LayerPropertiesText = "Image Display Properties";
		Initialize();
	}

	public void Initialize()
	{
		_resampleMethod = SharedData.GDALResampleNames.First();
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		_resampleMethod = XML.StringTextElement(xmlLayer, "ResampleMethod");
		if (!Enumerable.Contains(SharedData.GDALResampleNames, _resampleMethod))
		{
			_resampleMethod = SharedData.GDALResampleNames.First();
		}
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		XML.AddTextElement(doc, xmlLayer, "ResampleMethod", _resampleMethod);
	}

	protected virtual bool NeedNewBitmap(RasterM raster)
	{
		if (_bmp == null)
		{
			return true;
		}
		if (ResetBitmap)
		{
			return true;
		}
		if (!raster.EqualRasterM(_CurrentPlottedRaster))
		{
			return true;
		}
		if (NewProjection())
		{
			return true;
		}
		return false;
	}

	protected virtual bool NewProjection()
	{
		if (_lastRenderedProjection != null && _lastRenderedProjection.Equals(SharedData.SRSProjection))
		{
			return false;
		}
		_lastRenderedProjection = SharedData.SRSProjection;
		return true;
	}

	public static RasterM ComputeRasterM(string filename)
	{
		RasterM result;
		try
		{
			using GDALRaster gDALRaster = new GDALRaster(filename);
			int pixelsWide = gDALRaster.PixelsWide;
			int pixelsTall = gDALRaster.PixelsTall;
			Extent ext = Converter.Convert(GDALRaster.GetFullNativeExtents(filename));
			result = new RasterM(pixelsTall, pixelsWide, ext);
		}
		catch (Exception projectError)
		{
			ProjectData.SetProjectError(projectError);
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public virtual Projection GetSourceProjection()
	{
		Projection result;
		if (!File.Exists(SourceFilename))
		{
			result = null;
		}
		else
		{
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(SourceFilename);
				result = gDALRaster.GetProjection();
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	protected virtual void MakeBitmapNewWarp(RasterM raster, int[] colors, ref Bitmap bmp)
	{
		try
		{
			if (!File.Exists(SourceFilename))
			{
				bmp = null;
				ErrorMessage = "Error loading image: '" + SourceFilename + "' does not exist.";
				return;
			}
			_ = SharedData.SRSProjection;
			Converter.Convert(raster);
			throw new NotImplementedException();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			bmp = null;
			ErrorMessage = "Error loading image from file: '" + SourceFilename + "'" + ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	protected virtual void MakeBitmapOldWarp(RasterM raster, int[] colors, ref Bitmap bmp)
	{
		try
		{
			if (!File.Exists(SourceFilename))
			{
				bmp = null;
				ErrorMessage = "Error loading image: '" + SourceFilename + "' does not exist.";
				return;
			}
			_ = SharedData.SRSProjection;
			Converter.Convert(raster);
			throw new NotImplementedException();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			bmp = null;
			ErrorMessage = "Error loading image from file: '" + SourceFilename + "'" + ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	public static void WarpData(Projection sourceProjection, RasterM sourceRaster, float[] sourceData, Projection targetProjection, RasterM targetRaster, float[] targetData, float srcNoData = -9999f)
	{
		_Closure_0024__40_002D0 capturedLocals = default;
		_Closure_0024__40_002D0 capturedLocals2 = new _Closure_0024__40_002D0(capturedLocals);
		capturedLocals2.sourceRaster = sourceRaster;
		capturedLocals2.sourceData = sourceData;
		capturedLocals2.targetRaster = targetRaster;
		capturedLocals2.targetData = targetData;
		capturedLocals2.srcNoData = srcNoData;
		double cellCenterX = capturedLocals2.targetRaster.CellCenterX(0);
		checked
		{
			double cellCenterX2 = capturedLocals2.targetRaster.CellCenterX(capturedLocals2.targetRaster.Cols - 1);
			double cellCenterXCellCenterX2Term = (cellCenterX + cellCenterX2) * 0.5;
			double cellCenterY = capturedLocals2.targetRaster.CellCenterY(capturedLocals2.targetRaster.Rows - 1);
			double cellCenterY2 = capturedLocals2.targetRaster.CellCenterY(0);
			double cellCenterYCellCenterY2Term = (cellCenterY + cellCenterY2) * 0.5;
			double[] doubleValues = new double[8];
			double[] doubleValuesArray = new double[8];
			double[] z = new double[8];
			doubleValues[0] = cellCenterX;
			doubleValuesArray[0] = cellCenterY;
			doubleValues[1] = cellCenterXCellCenterX2Term;
			doubleValuesArray[1] = cellCenterY;
			doubleValues[2] = cellCenterX2;
			doubleValuesArray[2] = cellCenterY;
			doubleValues[3] = cellCenterX2;
			doubleValuesArray[3] = cellCenterYCellCenterY2Term;
			doubleValues[4] = cellCenterX2;
			doubleValuesArray[4] = cellCenterY2;
			doubleValues[5] = cellCenterXCellCenterX2Term;
			doubleValuesArray[5] = cellCenterY2;
			doubleValues[6] = cellCenterX;
			doubleValuesArray[6] = cellCenterY2;
			doubleValues[7] = cellCenterX;
			doubleValuesArray[7] = cellCenterYCellCenterY2Term;
			double[] doubleValues2 = new double[8];
			double[] doubleValues3 = new double[8];
			Array.Copy(doubleValues, doubleValues2, 8);
			Array.Copy(doubleValuesArray, doubleValues3, 8);
			Projector.TransformPoints(targetProjection, sourceProjection, doubleValues2, doubleValues3, z);
			if (capturedLocals2.targetRaster.CellCount == 1)
			{
				PointM pointM = new PointM(doubleValues2[0], doubleValues3[0]);
				if (capturedLocals2.sourceRaster.Contains(pointM))
				{
					capturedLocals2.targetData[0] = capturedLocals2.sourceData[capturedLocals2.sourceRaster.CellIndex(pointM)];
				}
				return;
			}
			double[] doubleValues4 = new double[8];
			double[] doubleValues5 = new double[8];
			int ordinal = 0;
			do
			{
				doubleValues4[ordinal] = capturedLocals2.sourceRaster.ColD(doubleValues2[ordinal]);
				doubleValues5[ordinal] = capturedLocals2.sourceRaster.RowD(doubleValues3[ordinal]);
				ordinal++;
			}
			while (ordinal <= 7);
			double[,] doubleValues6 = new double[8, 8];
			int ordinal2 = 0;
			do
			{
				double colD = capturedLocals2.targetRaster.ColD(doubleValues[ordinal2]);
				double rowD = capturedLocals2.targetRaster.RowD(doubleValuesArray[ordinal2]);
				doubleValues6[ordinal2, 0] = 1.0;
				doubleValues6[ordinal2, 1] = colD;
				doubleValues6[ordinal2, 2] = rowD;
				doubleValues6[ordinal2, 3] = colD * rowD;
				doubleValues6[ordinal2, 4] = colD * colD;
				doubleValues6[ordinal2, 5] = rowD * rowD;
				doubleValues6[ordinal2, 6] = colD * colD * rowD;
				doubleValues6[ordinal2, 7] = rowD * rowD * colD;
				ordinal2++;
			}
			while (ordinal2 <= 7);
			double[,] doubleValues7 = new double[8, 2];
			int ordinal3 = 0;
			do
			{
				doubleValues7[ordinal3, 0] = doubleValues4[ordinal3];
				doubleValues7[ordinal3, 1] = doubleValues5[ordinal3];
				ordinal3++;
			}
			while (ordinal3 <= 7);
			RasMapperLib.Utilities.Math.GaussJordan(doubleValues6, doubleValues7);
			capturedLocals2.coefCol = new TransformCoefficients77(doubleValues7, 0);
			capturedLocals2.coefRow = new TransformCoefficients77(doubleValues7, 1);
			capturedLocals2.sColMax = capturedLocals2.sourceRaster.Cols - 1;
			capturedLocals2.sRowMax = capturedLocals2.sourceRaster.Rows - 1;
			Parallel.For(0, capturedLocals2.targetRaster.Rows, [SpecialName] (int tr) =>
			{
				float intercept = default;
				float coefSC = default;
				float coefSC2 = default;
				capturedLocals2.coefCol.RasterRowCoefficients(tr, ref intercept, ref coefSC, ref coefSC2);
				float intercept2 = default;
				float coefSC3 = default;
				float coefSC4 = default;
				capturedLocals2.coefRow.RasterRowCoefficients(tr, ref intercept2, ref coefSC3, ref coefSC4);
				int trTimesCols = tr * capturedLocals2.targetRaster.Cols;
				int count = capturedLocals2.targetRaster.Cols - 1;
				for (int i = 0; i <= count; i++)
				{
					int systemInterceptTerm = (int)System.Math.Round(intercept + (coefSC + coefSC2 * (float)i) * (float)i);
					if (systemInterceptTerm >= 0 && systemInterceptTerm <= capturedLocals2.sColMax)
					{
						int systemIntercept2Term = (int)System.Math.Round(intercept2 + (coefSC3 + coefSC4 * (float)i) * (float)i);
						if (systemIntercept2Term >= 0 && systemIntercept2Term <= capturedLocals2.sRowMax)
						{
							int cellIndex = capturedLocals2.sourceRaster.CellIndex(systemIntercept2Term, systemInterceptTerm);
							float computed = capturedLocals2.sourceData[cellIndex];
							if (computed == capturedLocals2.srcNoData)
							{
								computed = -9999f;
							}
							capturedLocals2.targetData[trTimesCols] = computed;
						}
					}
					trTimesCols++;
				}
			});
		}
	}

	protected virtual Bitmap CreateIcon()
	{
		return CreateImageIcon("img");
	}

	protected Bitmap CreateImageIcon(string txt)
	{
		return null;
	}

	public static RasterM TiffTileRaster(TiffReaderBase tr)
	{
		Geospatial.Vectors.Extent fullNativeExtents = GDALRaster.GetFullNativeExtents(tr.Filename);
		Extent extent = new Extent(fullNativeExtents.Right, fullNativeExtents.Left, fullNativeExtents.Top, fullNativeExtents.Bottom);
		double deltaX = extent.DeltaX;
		double deltaY = extent.DeltaY;
		double deltaXOverImageWidth = deltaX / (double)tr.GetImageWidth(0);
		double deltaYOverImageHeight = deltaY / (double)tr.GetImageHeight(0);
		double systemTerm = System.Math.Max(deltaXOverImageWidth, deltaYOverImageHeight);
		double getTileWidthTrTerm = (double)tr.GetTileWidth(0) * systemTerm;
		int numTilesWide = tr.GetNumTilesWide(0);
		int numTilesTall = tr.GetNumTilesTall(0);
		double minX = extent.MinX;
		double maxY = extent.MaxY;
		return new RasterM(numTilesTall, numTilesWide, minX + (double)numTilesWide * getTileWidthTrTerm, minX, maxY, maxY - (double)numTilesTall * getTileWidthTrTerm);
	}
}
