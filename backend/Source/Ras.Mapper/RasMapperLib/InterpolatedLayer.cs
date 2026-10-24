using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class InterpolatedLayer : ImageLayer, IRasterLayer<float>
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__23_002D0
	{
		public int cols;

		public float[] floatValues;

		public int[] outputGreyscaleBuffer;

		public int flatCellColor;

		public float invcellsize8;

		public float zfactor;

		public float cos_zenith;

		public float sin_zenith;

		public float azimuth;

		public byte alpha;

		public int alphaBlack;

		public _Closure_0024__23_002D0(_Closure_0024__23_002D0 other)
		{
			if (other != null)
			{
				cols = other.cols;
				floatValues = other.floatValues;
				outputGreyscaleBuffer = other.outputGreyscaleBuffer;
				flatCellColor = other.flatCellColor;
				invcellsize8 = other.invcellsize8;
				zfactor = other.zfactor;
				cos_zenith = other.cos_zenith;
				sin_zenith = other.sin_zenith;
				azimuth = other.azimuth;
				alpha = other.alpha;
				alphaBlack = other.alphaBlack;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int row)
		{
			checked
			{
				int count = cols - 2;
				for (int i = 1; i <= count; i++)
				{
					int rowTerm = row * cols + i;
					float floatValue = floatValues[rowTerm - cols - 1];
					float floatValue2 = floatValues[rowTerm - cols];
					float floatValue3 = floatValues[rowTerm - cols + 1];
					float floatValue4 = floatValues[rowTerm - 1];
					float floatValue5 = floatValues[rowTerm + 1];
					float floatValue6 = floatValues[rowTerm + cols - 1];
					float floatValue7 = floatValues[rowTerm + cols];
					float floatValue8 = floatValues[rowTerm + cols + 1];
					if (floatValue != -9999f && floatValue2 != -9999f && floatValue3 != -9999f && floatValue4 != -9999f && floatValue5 != -9999f && floatValue6 != -9999f && floatValue7 != -9999f && floatValue8 != -9999f)
					{
						float computed = floatValue3 + floatValue5 + floatValue5 + floatValue8 - (floatValue + floatValue4 + floatValue4 + floatValue6);
						float computed2 = floatValue6 + floatValue7 + floatValue7 + floatValue8 - (floatValue + floatValue2 + floatValue2 + floatValue3);
						if (computed == 0f && computed2 == 0f)
						{
							outputGreyscaleBuffer[rowTerm] = flatCellColor;
							continue;
						}
						computed *= invcellsize8;
						computed2 *= invcellsize8;
						float systemTerm = zfactor * (float)System.Math.Pow(computed * computed + computed2 * computed2, 0.5);
						float systemTerm2 = (float)System.Math.Pow(systemTerm * systemTerm + 1f, 0.5);
						float computed3 = 1f / systemTerm2;
						float computed4 = computed3;
						float computed5 = systemTerm * computed3;
						double systemTerm3 = 0.0;
						if (computed != 0f)
						{
							systemTerm3 = System.Math.Atan2(computed2, 0f - computed);
							if (systemTerm3 < 0.0)
							{
								systemTerm3 += System.Math.PI * 2.0;
							}
						}
						else if (computed2 > 0f)
						{
							systemTerm3 = System.Math.PI / 2.0;
						}
						else if (computed2 < 0f)
						{
							systemTerm3 = 4.71238898038469;
						}
						float systemTerm4 = cos_zenith * computed4 + sin_zenith * computed5 * (float)System.Math.Cos((double)azimuth - systemTerm3);
						if (systemTerm4 > 0f)
						{
							int systemTerm5 = (int)System.Math.Round((float)unchecked((int)alpha) * (1f - systemTerm4));
							outputGreyscaleBuffer[rowTerm] = systemTerm5 << 24;
						}
						else
						{
							outputGreyscaleBuffer[rowTerm] = alphaBlack;
						}
					}
					else
					{
						outputGreyscaleBuffer[rowTerm] = 0;
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__24_002D0
	{
		public int cols;

		public float[] floatValues;

		public int[] colors;

		public int contourColor;

		public _Closure_0024__24_002D0(_Closure_0024__24_002D0 other)
		{
			if (other != null)
			{
				cols = other.cols;
				floatValues = other.floatValues;
				colors = other.colors;
				contourColor = other.contourColor;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int row)
		{
			checked
			{
				int count = cols - 2;
				for (int i = 1; i <= count; i++)
				{
					int rowTerm = row * cols + i;
					float floatValue = floatValues[rowTerm - cols - 1];
					float floatValue2 = floatValues[rowTerm - cols];
					float floatValue3 = floatValues[rowTerm - cols + 1];
					float floatValue4 = floatValues[rowTerm - 1];
					float floatValue5 = floatValues[rowTerm];
					float floatValue6 = floatValues[rowTerm + 1];
					float floatValue7 = floatValues[rowTerm + cols - 1];
					float floatValue8 = floatValues[rowTerm + cols];
					float floatValue9 = floatValues[rowTerm + cols + 1];
					if (floatValue != -9999f && floatValue2 != -9999f && floatValue3 != -9999f && floatValue4 != -9999f && floatValue5 != -9999f && floatValue6 != -9999f && floatValue7 != -9999f && floatValue8 != -9999f && floatValue9 != -9999f)
					{
						if (floatValue < floatValue5 && floatValue9 < floatValue5)
						{
							colors[rowTerm] = contourColor;
						}
						else if (floatValue3 < floatValue5 && floatValue7 < floatValue5)
						{
							colors[rowTerm] = contourColor;
						}
						else if (floatValue2 < floatValue5 && floatValue8 < floatValue5)
						{
							colors[rowTerm] = contourColor;
						}
						else if (floatValue4 < floatValue5 && floatValue6 < floatValue5)
						{
							colors[rowTerm] = contourColor;
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__25_002D0
	{
		public int cols;

		public float[] cellContour;

		public int contourColor;

		public int[] outColors;

		public _Closure_0024__25_002D0(_Closure_0024__25_002D0 other)
		{
			if (other != null)
			{
				cols = other.cols;
				cellContour = other.cellContour;
				contourColor = other.contourColor;
				outColors = other.outColors;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int row)
		{
			checked
			{
				int rowTerm = row * cols;
				int count = cols - 2;
				for (int i = 1; i <= count; i++)
				{
					rowTerm++;
					float cellContour = this.cellContour[rowTerm];
					if (cellContour != -9999f)
					{
						int ordinal = rowTerm - cols;
						float cellContour2 = this.cellContour[ordinal];
						if (cellContour2 != -9999f)
						{
							ContourRasterEdge(contourColor, cellContour, cellContour2, ref outColors[rowTerm], ref outColors[ordinal]);
						}
						int ordinal2 = rowTerm + 1;
						float cellContour3 = this.cellContour[ordinal2];
						if (cellContour3 != -9999f)
						{
							ContourRasterEdge(contourColor, cellContour, cellContour3, ref outColors[rowTerm], ref outColors[ordinal2]);
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__27_002D0
	{
		public int cols;

		public float[] floatValues;

		public float[] cellContour;

		public float invContour;

		public _Closure_0024__27_002D0(_Closure_0024__27_002D0 other)
		{
			if (other != null)
			{
				cols = other.cols;
				floatValues = other.floatValues;
				cellContour = other.cellContour;
				invContour = other.invContour;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int row)
		{
			checked
			{
				int rowTerm = row * cols;
				int count = cols - 2;
				for (int i = 1; i <= count; i++)
				{
					rowTerm++;
					if (floatValues[rowTerm] != -9999f)
					{
						cellContour[rowTerm] = floatValues[rowTerm] * invContour;
					}
				}
			}
		}
	}

	public const float NoData = -9999f;

	public const int DefaultTileSize = 256;

	public const int DefaultOverlayTileSize = 256;

	public const int DefaultHistogramBucketCount = 128;

	public const string sGeoTiffFilter = "GeoTiff (*.tif)|*.tif";

	public const string sGeoTiffExtension = ".tif";

	public const string sFLTFilter = "Generic Float (*.flt)|*.flt";

	public virtual string BalloonHighlightStringFormat { get; }

	public virtual bool TreatZeroAsNoData { get; set; }

	public bool ShowValueOnRightClickContextMenu { get; set; }

	public InterpolatedLayer()
		: this("Interpolated Layer")
	{
	}

	public InterpolatedLayer(string name)
		: base(name)
	{
		BalloonHighlightStringFormat = "";
		TreatZeroAsNoData = false;
		ShowValueOnRightClickContextMenu = true;
		_resampleMethod = SharedData.GDALResampleNames.First();
	}

	public InterpolatedLayer(string name, string filename)
		: base(name, filename)
	{
		BalloonHighlightStringFormat = "";
		TreatZeroAsNoData = false;
		ShowValueOnRightClickContextMenu = true;
		_resampleMethod = SharedData.GDALResampleNames.First();
	}

	public void WarpAndSave(Projection destProjection, double cellSize, string tiffFilename)
	{
		Projection sourceProjection = GetSourceProjection();
		checked
		{
			if (sourceProjection != null)
			{
				Geospatial.Vectors.Extent boundingBox = Projector.GetBoundingBox(Converter.Convert(Extent), sourceProjection, destProjection, 24);
				int systemWidthTerm = (int)System.Math.Round(boundingBox.Width / cellSize);
				int systemHeightTerm = (int)System.Math.Round(boundingBox.Height / cellSize);
				int rows = (int)System.Math.Ceiling((double)systemHeightTerm / 256.0);
				int cols = (int)System.Math.Ceiling((double)systemWidthTerm / 256.0);
				new RasterM(new PointM(boundingBox.Left, boundingBox.Top), systemHeightTerm, systemWidthTerm, cellSize);
				new RasterM(new PointM(boundingBox.Left, boundingBox.Top), rows, cols, cellSize * 256.0);
				throw new NotImplementedException();
			}
		}
	}

	public virtual bool Resample(RasterM raster, ref float[] values)
	{
		throw new NotImplementedException();
	}

	bool IRasterLayer<float>.Resample(RasterM raster, ref float[] values)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Resample
		return this.Resample(raster, ref values);
	}

	protected static void ComputeHillShade(RasterM raster, float[] values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, int[] outputGreyscaleBuffer)
	{
		_Closure_0024__23_002D0 capturedLocals = default;
		_Closure_0024__23_002D0 capturedLocals2 = new _Closure_0024__23_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		capturedLocals2.alpha = alpha;
		capturedLocals2.zfactor = zfactor;
		capturedLocals2.outputGreyscaleBuffer = outputGreyscaleBuffer;
		float cellSize = (float)raster.CellSize;
		capturedLocals2.invcellsize8 = 1f / (cellSize * 8f);
		double altitude_degPiTerm = (double)(90f - altitude_deg) * System.Math.PI / 180.0;
		capturedLocals2.cos_zenith = (float)System.Math.Cos(altitude_degPiTerm);
		capturedLocals2.sin_zenith = (float)System.Math.Sin(altitude_degPiTerm);
		float azimuth_degTerm = 360f - azimuth_deg + 90f;
		if (azimuth_degTerm > 360f)
		{
			azimuth_degTerm -= 360f;
		}
		capturedLocals2.azimuth = azimuth_degTerm * ((float)System.Math.PI / 180f);
		checked
		{
			uint capturedLocals3 = (uint)System.Math.Round((float)unchecked((int)capturedLocals2.alpha) * (1f - capturedLocals2.cos_zenith)) << 24;
			uint[] src = new uint[1] { capturedLocals3 };
			int[] intValues = new int[1];
			Buffer.BlockCopy(src, 0, intValues, 0, 4);
			capturedLocals2.flatCellColor = intValues[0];
			int rows = raster.Rows;
			capturedLocals2.cols = raster.Cols;
			capturedLocals2.alphaBlack = 0;
			System.Threading.Tasks.Parallel.For(1, rows - 1, [SpecialName] (int row) =>
			{
				int count = capturedLocals2.cols - 2;
				for (int i = 1; i <= count; i++)
				{
					int capturedLocals4 = row * capturedLocals2.cols + i;
					float capturedLocals5 = capturedLocals2.floatValues[capturedLocals4 - capturedLocals2.cols - 1];
					float capturedLocals6 = capturedLocals2.floatValues[capturedLocals4 - capturedLocals2.cols];
					float capturedLocals7 = capturedLocals2.floatValues[capturedLocals4 - capturedLocals2.cols + 1];
					float capturedLocals8 = capturedLocals2.floatValues[capturedLocals4 - 1];
					float capturedLocals9 = capturedLocals2.floatValues[capturedLocals4 + 1];
					float capturedLocals10 = capturedLocals2.floatValues[capturedLocals4 + capturedLocals2.cols - 1];
					float capturedLocals11 = capturedLocals2.floatValues[capturedLocals4 + capturedLocals2.cols];
					float capturedLocals12 = capturedLocals2.floatValues[capturedLocals4 + capturedLocals2.cols + 1];
					if (capturedLocals5 != -9999f && capturedLocals6 != -9999f && capturedLocals7 != -9999f && capturedLocals8 != -9999f && capturedLocals9 != -9999f && capturedLocals10 != -9999f && capturedLocals11 != -9999f && capturedLocals12 != -9999f)
					{
						float computed = capturedLocals7 + capturedLocals9 + capturedLocals9 + capturedLocals12 - (capturedLocals5 + capturedLocals8 + capturedLocals8 + capturedLocals10);
						float computed2 = capturedLocals10 + capturedLocals11 + capturedLocals11 + capturedLocals12 - (capturedLocals5 + capturedLocals6 + capturedLocals6 + capturedLocals7);
						if (computed == 0f && computed2 == 0f)
						{
							capturedLocals2.outputGreyscaleBuffer[capturedLocals4] = capturedLocals2.flatCellColor;
						}
						else
						{
							computed *= capturedLocals2.invcellsize8;
							computed2 *= capturedLocals2.invcellsize8;
							float capturedLocals13 = capturedLocals2.zfactor * (float)System.Math.Pow(computed * computed + computed2 * computed2, 0.5);
							float systemTerm = (float)System.Math.Pow(capturedLocals13 * capturedLocals13 + 1f, 0.5);
							float computed3 = 1f / systemTerm;
							float computed4 = computed3;
							float computed5 = capturedLocals13 * computed3;
							double systemTerm2 = 0.0;
							if (computed != 0f)
							{
								systemTerm2 = System.Math.Atan2(computed2, 0f - computed);
								if (systemTerm2 < 0.0)
								{
									systemTerm2 += System.Math.PI * 2.0;
								}
							}
							else if (computed2 > 0f)
							{
								systemTerm2 = System.Math.PI / 2.0;
							}
							else if (computed2 < 0f)
							{
								systemTerm2 = 4.71238898038469;
							}
							float capturedLocals14 = capturedLocals2.cos_zenith * computed4 + capturedLocals2.sin_zenith * computed5 * (float)System.Math.Cos((double)capturedLocals2.azimuth - systemTerm2);
							if (capturedLocals14 > 0f)
							{
								int capturedLocals15 = (int)System.Math.Round((float)unchecked((int)capturedLocals2.alpha) * (1f - capturedLocals14));
								capturedLocals2.outputGreyscaleBuffer[capturedLocals4] = capturedLocals15 << 24;
							}
							else
							{
								capturedLocals2.outputGreyscaleBuffer[capturedLocals4] = capturedLocals2.alphaBlack;
							}
						}
					}
					else
					{
						capturedLocals2.outputGreyscaleBuffer[capturedLocals4] = 0;
					}
				}
			});
			if (rows > 1)
			{
				int count2 = capturedLocals2.cols - 1;
				for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
				{
					int capturedLocals16 = capturedLocals2.cols + intervalIndex;
					capturedLocals2.outputGreyscaleBuffer[intervalIndex] = capturedLocals2.outputGreyscaleBuffer[capturedLocals16];
				}
			}
			if (capturedLocals2.cols > 1)
			{
				int count3 = rows - 1;
				for (int ordinal = 0; ordinal <= count3; ordinal++)
				{
					int capturedLocals17 = ordinal * capturedLocals2.cols;
					_ = capturedLocals17 + 1;
					capturedLocals2.outputGreyscaleBuffer[capturedLocals17] = capturedLocals2.outputGreyscaleBuffer[capturedLocals17 + 1];
				}
			}
		}
	}

	private void ComputeRidges(RasterM raster, float[] values, int contourColor, int[] colors)
	{
		_Closure_0024__24_002D0 capturedLocals = default;
		_Closure_0024__24_002D0 capturedLocals2 = new _Closure_0024__24_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		capturedLocals2.contourColor = contourColor;
		capturedLocals2.colors = colors;
		int rows = raster.Rows;
		capturedLocals2.cols = raster.Cols;
		checked
		{
			System.Threading.Tasks.Parallel.For(1, rows - 1, [SpecialName] (int row) =>
			{
				int count = capturedLocals2.cols - 2;
				for (int i = 1; i <= count; i++)
				{
					int capturedLocals3 = row * capturedLocals2.cols + i;
					float capturedLocals4 = capturedLocals2.floatValues[capturedLocals3 - capturedLocals2.cols - 1];
					float capturedLocals5 = capturedLocals2.floatValues[capturedLocals3 - capturedLocals2.cols];
					float capturedLocals6 = capturedLocals2.floatValues[capturedLocals3 - capturedLocals2.cols + 1];
					float capturedLocals7 = capturedLocals2.floatValues[capturedLocals3 - 1];
					float capturedLocals8 = capturedLocals2.floatValues[capturedLocals3];
					float capturedLocals9 = capturedLocals2.floatValues[capturedLocals3 + 1];
					float capturedLocals10 = capturedLocals2.floatValues[capturedLocals3 + capturedLocals2.cols - 1];
					float capturedLocals11 = capturedLocals2.floatValues[capturedLocals3 + capturedLocals2.cols];
					float capturedLocals12 = capturedLocals2.floatValues[capturedLocals3 + capturedLocals2.cols + 1];
					if (capturedLocals4 != -9999f && capturedLocals5 != -9999f && capturedLocals6 != -9999f && capturedLocals7 != -9999f && capturedLocals8 != -9999f && capturedLocals9 != -9999f && capturedLocals10 != -9999f && capturedLocals11 != -9999f && capturedLocals12 != -9999f)
					{
						if (capturedLocals4 < capturedLocals8 && capturedLocals12 < capturedLocals8)
						{
							capturedLocals2.colors[capturedLocals3] = capturedLocals2.contourColor;
						}
						else if (capturedLocals6 < capturedLocals8 && capturedLocals10 < capturedLocals8)
						{
							capturedLocals2.colors[capturedLocals3] = capturedLocals2.contourColor;
						}
						else if (capturedLocals5 < capturedLocals8 && capturedLocals11 < capturedLocals8)
						{
							capturedLocals2.colors[capturedLocals3] = capturedLocals2.contourColor;
						}
						else if (capturedLocals7 < capturedLocals8 && capturedLocals9 < capturedLocals8)
						{
							capturedLocals2.colors[capturedLocals3] = capturedLocals2.contourColor;
						}
					}
				}
			});
		}
	}

	public static void ComputeRasterContours(RasterM raster, float[] values, float[] bufferValues, ContourProperties contourProperties, int[] outColors)
	{
		_Closure_0024__25_002D0 capturedLocals = default;
		_Closure_0024__25_002D0 capturedLocals2 = new _Closure_0024__25_002D0(capturedLocals);
		capturedLocals2.outColors = outColors;
		int rows = raster.Rows;
		capturedLocals2.cols = raster.Cols;
		capturedLocals2.contourColor = contourProperties.ColorARGB;
		capturedLocals2.cellContour = GetCellContourValues(raster, values, bufferValues, contourProperties);
		System.Threading.Tasks.Parallel.For(1, rows, checked([SpecialName] (int row) =>
		{
			int capturedLocals3 = row * capturedLocals2.cols;
			int count = capturedLocals2.cols - 2;
			for (int i = 1; i <= count; i++)
			{
				capturedLocals3++;
				float cellContour = capturedLocals2.cellContour[capturedLocals3];
				if (cellContour != -9999f)
				{
					int capturedLocals4 = capturedLocals3 - capturedLocals2.cols;
					float cellContour2 = capturedLocals2.cellContour[capturedLocals4];
					if (cellContour2 != -9999f)
					{
						ContourRasterEdge(capturedLocals2.contourColor, cellContour, cellContour2, ref capturedLocals2.outColors[capturedLocals3], ref capturedLocals2.outColors[capturedLocals4]);
					}
					int ordinal = capturedLocals3 + 1;
					float cellContour3 = capturedLocals2.cellContour[ordinal];
					if (cellContour3 != -9999f)
					{
						ContourRasterEdge(capturedLocals2.contourColor, cellContour, cellContour3, ref capturedLocals2.outColors[capturedLocals3], ref capturedLocals2.outColors[ordinal]);
					}
				}
			}
		}));
	}

	public static List<SegmentM> ComputeVectorContourSegments(RasterM raster, float[] values, ContourProperties contourProperties)
	{
		int rows = raster.Rows;
		int cols = raster.Cols;
		checked
		{
			float[] floatValues = new float[values.Length - 1 + 1];
			Array.Copy(values, floatValues, values.Length - 1);
			float[] cellContourValues = GetCellContourValues(raster, values, floatValues, contourProperties);
			bool[] boolValues = new bool[values.Length - 1 + 1];
			Dictionary<Tuple<int, int>, SegmentM> segmentMByValueTuple = new Dictionary<Tuple<int, int>, SegmentM>();
			HashSet<int> intSet = new HashSet<int>();
			HashSet<Tuple<int, int>> valueTupleSet = new HashSet<Tuple<int, int>>();
			int count = rows - 1;
			for (int i = 1; i <= count; i++)
			{
				int colsTerm = i * cols;
				int count2 = cols - 2;
				for (int j = 1; j <= count2; j++)
				{
					colsTerm++;
					float cellContourValue = cellContourValues[colsTerm];
					if (cellContourValue == -9999f)
					{
						continue;
					}
					int colsTerm2 = colsTerm - cols;
					float cellContourValue2 = cellContourValues[colsTerm2];
					if (cellContourValue2 != -9999f)
					{
						ContourVectorEdge(cellContourValue, cellContourValue2, ref boolValues[colsTerm], ref boolValues[colsTerm2]);
						if (boolValues[colsTerm] && boolValues[colsTerm2])
						{
							valueTupleSet.Remove(Tuple.Create(colsTerm, colsTerm2));
							PointM pA = raster.CellCenter(colsTerm);
							PointM pB = raster.CellCenter(colsTerm2);
							segmentMByValueTuple.Add(Tuple.Create(colsTerm, colsTerm2), new SegmentM(pA, pB));
						}
						else if (boolValues[colsTerm])
						{
							intSet.Add(colsTerm);
							valueTupleSet.Add(Tuple.Create(colsTerm, colsTerm2));
						}
						else if (boolValues[colsTerm2])
						{
							intSet.Add(colsTerm2);
							valueTupleSet.Add(Tuple.Create(colsTerm, colsTerm2));
						}
						else
						{
							valueTupleSet.Add(Tuple.Create(colsTerm, colsTerm2));
						}
					}
					int ordinal = colsTerm + 1;
					float cellContourValue3 = cellContourValues[ordinal];
					if (cellContourValue3 != -9999f)
					{
						ContourVectorEdge(cellContourValue, cellContourValue3, ref boolValues[colsTerm], ref boolValues[ordinal]);
						if (boolValues[colsTerm] && boolValues[ordinal])
						{
							valueTupleSet.Remove(Tuple.Create(colsTerm, ordinal));
							PointM pA2 = raster.CellCenter(colsTerm);
							PointM pB2 = raster.CellCenter(ordinal);
							segmentMByValueTuple.Add(Tuple.Create(colsTerm, ordinal), new SegmentM(pA2, pB2));
						}
						else if (boolValues[colsTerm])
						{
							intSet.Add(colsTerm);
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal));
						}
						else if (boolValues[ordinal])
						{
							intSet.Add(ordinal);
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal));
						}
						else
						{
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal));
						}
					}
					int ordinal2 = colsTerm2 + 1;
					float cellContourValue4 = cellContourValues[ordinal2];
					if (cellContourValue4 != -9999f)
					{
						ContourVectorEdge(cellContourValue, cellContourValue4, ref boolValues[colsTerm], ref boolValues[ordinal2]);
						if (boolValues[colsTerm] && boolValues[ordinal2])
						{
							valueTupleSet.Remove(Tuple.Create(colsTerm, ordinal2));
							PointM pA3 = raster.CellCenter(colsTerm);
							PointM pB3 = raster.CellCenter(ordinal2);
							segmentMByValueTuple.Add(Tuple.Create(colsTerm, ordinal2), new SegmentM(pA3, pB3));
						}
						else if (boolValues[colsTerm])
						{
							intSet.Add(colsTerm);
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal2));
						}
						else if (boolValues[ordinal2])
						{
							intSet.Add(ordinal2);
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal2));
						}
						else
						{
							valueTupleSet.Add(Tuple.Create(colsTerm, ordinal2));
						}
					}
				}
			}
			foreach (Tuple<int, int> tuple in valueTupleSet)
			{
				if (intSet.Contains(tuple.Item1) && intSet.Contains(tuple.Item2))
				{
					PointM pA4 = raster.CellCenter(tuple.Item1);
					PointM pB4 = raster.CellCenter(tuple.Item2);
					segmentMByValueTuple.Add(Tuple.Create(tuple.Item1, tuple.Item2), new SegmentM(pA4, pB4));
				}
			}
			List<SegmentM> segmentMs = new List<SegmentM>();
			foreach (KeyValuePair<Tuple<int, int>, SegmentM> localValue in segmentMByValueTuple)
			{
				segmentMs.Add(localValue.Value);
			}
			return segmentMs;
		}
	}

	private static float[] GetCellContourValues(RasterM raster, float[] values, float[] bufferValues, ContourProperties contourProperties)
	{
		_Closure_0024__27_002D0 capturedLocals = default;
		_Closure_0024__27_002D0 capturedLocals2 = new _Closure_0024__27_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		int rows = raster.Rows;
		capturedLocals2.cols = raster.Cols;
		capturedLocals2.cellContour = bufferValues;
		Buffers.ArraySetAllVB(capturedLocals2.cellContour, -9999f);
		float interval = contourProperties.Interval;
		capturedLocals2.invContour = 1f / interval;
		System.Threading.Tasks.Parallel.For(1, rows, checked([SpecialName] (int row) =>
		{
			int capturedLocals3 = row * capturedLocals2.cols;
			int count = capturedLocals2.cols - 2;
			for (int i = 1; i <= count; i++)
			{
				capturedLocals3++;
				if (capturedLocals2.floatValues[capturedLocals3] != -9999f)
				{
					capturedLocals2.cellContour[capturedLocals3] = capturedLocals2.floatValues[capturedLocals3] * capturedLocals2.invContour;
				}
			}
		}));
		return capturedLocals2.cellContour;
	}

	private static void ContourRasterEdge(int contourColor, float va, float vb, ref int ca, ref int cb)
	{
		if (va > vb)
		{
			float systemVaTerm = (float)System.Math.Floor(va);
			if (vb < systemVaTerm)
			{
				if (va - systemVaTerm < systemVaTerm - vb)
				{
					ca = contourColor;
				}
				else
				{
					cb = contourColor;
				}
			}
		}
		else
		{
			if (!(va < vb))
			{
				return;
			}
			float systemVbTerm = (float)System.Math.Floor(vb);
			if (va < systemVbTerm)
			{
				if (vb - systemVbTerm < systemVbTerm - va)
				{
					cb = contourColor;
				}
				else
				{
					ca = contourColor;
				}
			}
		}
	}

	private static void ContourVectorEdge(float va, float vb, ref bool chooseA, ref bool chooseB)
	{
		if (va > vb)
		{
			float systemVaTerm = (float)System.Math.Floor(va);
			if (vb < systemVaTerm)
			{
				if (va - systemVaTerm < systemVaTerm - vb)
				{
					chooseA = true;
				}
				else
				{
					chooseB = true;
				}
			}
		}
		else
		{
			if (!(va < vb))
			{
				return;
			}
			float systemVbTerm = (float)System.Math.Floor(vb);
			if (va < systemVbTerm)
			{
				if (vb - systemVbTerm < systemVbTerm - va)
				{
					chooseB = true;
				}
				else
				{
					chooseA = true;
				}
			}
		}
	}

	public static void AddProjection(string filename)
	{
		if (SharedData.SRSFilenameShowStatus())
		{
			GDALRaster.SetProjection(filename, SharedData.SRSProjection);
		}
		else if (Operators.CompareString(SharedData.SRSFilename, "", TextCompare: false) != 0 && SharedData.RasMapper != null)
		{
			SharedData.RasMapper.StatusMessageAdd("Warning - SRS Projection not available");
		}
	}

	public static void AddGeoreferencing(string filename, double minX, double maxY, double cellSize)
	{
		GDALRaster.SetLocation(filename, new double[6]
		{
			minX,
			cellSize,
			0.0,
			maxY,
			0.0,
			0.0 - cellSize
		});
	}

	public static void AddHistogram(string filename, List<int> tilesWithData = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int numBuckets = 256;
		int[] Buckets = null;
		if (tilesWithData.IsNullOrEmpty())
		{
			FloatTiffWriter.ComputeSetHistogram(filename, numBuckets, out Buckets, reporter);
		}
		else
		{
			FloatTiffWriter.ComputeSetHistogramPartial(filename, numBuckets, out Buckets, tilesWithData, reporter);
		}
	}

	public static void AddOverlays(string filename, int width, int height, bool UseNearest, Action<char> ReportStdOut, Action<char> ReportStdErr, Action<Process> SetProcess)
	{
		int systemWidthTerm = System.Math.Max(width, height);
		if (systemWidthTerm > 256)
		{
			string defaultOverlayString = GetDefaultOverlayString(systemWidthTerm);
			string useNearestTerm = ((!UseNearest) ? "-r average" : "-r near");
			useNearestTerm += " --config COMPRESS_OVERVIEW DEFLATE ";
			string StdOut = "";
			string StdErr = "";
			Executables.GdalAddo(useNearestTerm + "\"" + filename + "\"" + defaultOverlayString, out StdOut, out StdErr, ReportStdOut, ReportStdErr, SetProcess);
		}
	}

	public static string GetDefaultOverlayString(int maxDimension)
	{
		string defaultOverlayString = "";
		int ordinal = 1;
		while (!(System.Math.Ceiling((double)maxDimension / (double)ordinal) <= 256.0))
		{
			ordinal = checked(ordinal * 2);
			defaultOverlayString = defaultOverlayString + " " + Conversions.ToString(ordinal);
		}
		return defaultOverlayString;
	}

	public static void AddSRSGeorefAddo(string filename, int width, int height, double minX, double maxY, double cellSize, bool UseNearest, Action<char> ReportStdOut, Action<char> ReportStdErr, Action<Process> ReportProcID)
	{
		AddProjection(filename);
		AddGeoreferencing(filename, minX, maxY, cellSize);
		AddOverlays(filename, width, height, UseNearest, ReportStdOut, ReportStdErr, ReportProcID);
	}

	public static void AddSRSGeoref(string filename, double minX, double maxY, double cellSize)
	{
		AddProjection(filename);
		AddGeoreferencing(filename, minX, maxY, cellSize);
	}

	public static void CreateVRT(string[] filenames, string vrtFilename, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		File.Delete(vrtFilename);
		string StdOut = "";
		string StdErr = "";
		Executables.GdalBuildVRT(filenames, vrtFilename, out StdOut, out StdErr);
		Histogram vRTHistogram = GetVRTHistogram(vrtFilename, reporter);
		double minimum = vRTHistogram.Minimum;
		double maximum = vRTHistogram.Maximum;
		double mean = vRTHistogram.ComputeMean();
		double stddev = vRTHistogram.ComputeStandardDeviation();
		GDALRaster.SetStats(vrtFilename, minimum, maximum, mean, stddev);
	}

	public static Histogram GetVRTHistogram(string vrtFilename, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int[] Buckets = null;
		GDALRaster.ReadCurrentHistogram(vrtFilename, out var Min, out var Max, out Buckets, null);
		if (Buckets != null && Buckets.Count() > 0)
		{
			double areaPerCell;
			using (GDALRaster gDALRaster = new GDALRaster(vrtFilename))
			{
				areaPerCell = gDALRaster.GetCellSizeX() * gDALRaster.GetCellSizeY();
			}
			reporter.ReportProgress(100);
			return new Histogram(Min, Max, Buckets, areaPerCell);
		}
		double scaledLengthSum = 0.0;
		string[] vRTSources = GDALRaster.GetVRTSources(vrtFilename);
		List<Histogram> histograms = new List<Histogram>();
		string[] vRTSourcesArray = vRTSources;
		foreach (string vRTSource in vRTSourcesArray)
		{
			if (Operators.CompareString(vRTSource, vrtFilename, TextCompare: false) != 0)
			{
				double areaPerCell2;
				using (GDALRaster gDALRaster2 = new GDALRaster(vRTSource))
				{
					areaPerCell2 = gDALRaster2.GetCellSizeX() * gDALRaster2.GetCellSizeY();
				}
				int[] histogramArray = null;
				TiffReader<float>.ReadGDALStats(vRTSource, 0, out var Min2, out var Max2, out var _, out var _, out var _);
				histogramArray = TiffReader<float>.GetHistogram(vRTSource);
				if (histogramArray == null || histogramArray.Count() == 0)
				{
					throw new Exception("Histogram not set for " + vRTSource);
				}
				histograms.Add(new Histogram(Min2, Max2, histogramArray, areaPerCell2));
			}
			scaledLengthSum += 1.0 / (double)vrtFilename.Length;
		}
		Histogram histogram = Histogram.MergeHistograms(histograms);
		double getCellSizeXGDALRaster3Term;
		using (GDALRaster gDALRaster3 = new GDALRaster(vrtFilename))
		{
			getCellSizeXGDALRaster3Term = gDALRaster3.GetCellSizeX() * gDALRaster3.GetCellSizeY();
		}
		checked
		{
			long[] longs = new long[histogram.BinCount - 1 + 1];
			int count = histogram.BinCount - 1;
			for (int j = 0; j <= count; j++)
			{
				longs[j] = (long)System.Math.Round(histogram.get_BinArea(j) / getCellSizeXGDALRaster3Term);
			}
			int[] intValues = new int[histogram.BinCount - 1 + 1];
			long longsTerm = longs.Max();
			if (longsTerm > int.MaxValue)
			{
				int count2 = intValues.Count() - 1;
				for (int k = 0; k <= count2; k++)
				{
					intValues[k] = (int)System.Math.Round(System.Math.Min(2147483647.0, (double)longs[k] * 2147483647.0 / (double)longsTerm));
				}
			}
			else
			{
				int count3 = intValues.Count() - 1;
				for (int l = 0; l <= count3; l++)
				{
					intValues[l] = (int)longs[l];
				}
			}
			GDALRaster.SetHistogram(vrtFilename, histogram.Minimum, histogram.Maximum, intValues);
			return histogram;
		}
	}

	public static void WriteFLT(string filename, RasterM raster, float[] values)
	{
		filename = System.IO.Path.ChangeExtension(filename, "flt");
		File.Delete(filename);
		byte[] byteValues = new byte[checked(values.Length * 4 - 1 + 1)];
		Buffer.BlockCopy(values, 0, byteValues, 0, byteValues.Length);
		FileStream fileStream = new FileStream(filename, FileMode.CreateNew, FileAccess.Write);
		fileStream.Write(byteValues, 0, byteValues.Length);
		fileStream.Close();
		raster.WriteFloatHeaderfile(filename, -9999f);
	}

	public static string[] SourceFilenames(string filename)
	{
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".vrt", TextCompare: false) == 0)
		{
			return GDALRaster.GetVRTSources(filename);
		}
		return new string[1] { filename };
	}

	protected override Bitmap CreateIcon()
	{
		return CreateImageIcon("grid");
	}
}
