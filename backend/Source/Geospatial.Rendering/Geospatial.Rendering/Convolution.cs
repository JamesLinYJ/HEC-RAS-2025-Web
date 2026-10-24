using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Utility.Extensions;

namespace Geospatial.Rendering;

[SupportedOSPlatform("windows")]
public static class Convolution
{
	public unsafe static Bitmap Convolve(Bitmap b, ConvolutionMatrix3x3 m)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			throw new NotImplementedException();
		}
		if (m.Factor == 0)
		{
			return null;
		}
		Bitmap bitmap = (Bitmap)b.Clone();
		BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		BitmapData bitmapData2 = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		int stride = bitmapData.Stride;
		int scaledStride = stride * 2;
		nint scan = bitmapData.Scan0;
		nint scan2 = bitmapData2.Scan0;
		byte* scan3 = (byte*)scan;
		byte* scan22 = (byte*)scan2;
		int strideWidthTerm = stride - b.Width * 3;
		int count = b.Width - 2;
		int count2 = b.Height - 2;
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				int scan32 = (scan22[2] * m.TopLeft + scan22[5] * m.TopMid + scan22[8] * m.TopRight + scan22[2 + stride] * m.MidLeft + scan22[5 + stride] * m.Pixel + scan22[8 + stride] * m.MidRight + scan22[2 + scaledStride] * m.BotLeft + scan22[5 + scaledStride] * m.BotMid + scan22[8 + scaledStride] * m.BotRight) / m.Factor + m.Offset;
				if (scan32 < 0)
				{
					scan32 = 0;
				}
				if (scan32 > 255)
				{
					scan32 = 255;
				}
				scan3[5 + stride] = (byte)scan32;
				scan32 = (scan22[1] * m.TopLeft + scan22[4] * m.TopMid + scan22[7] * m.TopRight + scan22[1 + stride] * m.MidLeft + scan22[4 + stride] * m.Pixel + scan22[7 + stride] * m.MidRight + scan22[1 + scaledStride] * m.BotLeft + scan22[4 + scaledStride] * m.BotMid + scan22[7 + scaledStride] * m.BotRight) / m.Factor + m.Offset;
				if (scan32 < 0)
				{
					scan32 = 0;
				}
				if (scan32 > 255)
				{
					scan32 = 255;
				}
				scan3[4 + stride] = (byte)scan32;
				scan32 = (*scan22 * m.TopLeft + scan22[3] * m.TopMid + scan22[6] * m.TopRight + scan22[stride] * m.MidLeft + scan22[3 + stride] * m.Pixel + scan22[6 + stride] * m.MidRight + scan22[scaledStride] * m.BotLeft + scan22[3 + scaledStride] * m.BotMid + scan22[6 + scaledStride] * m.BotRight) / m.Factor + m.Offset;
				if (scan32 < 0)
				{
					scan32 = 0;
				}
				if (scan32 > 255)
				{
					scan32 = 255;
				}
				scan3[3 + stride] = (byte)scan32;
				scan3 += 3;
				scan22 += 3;
			}
			scan3 += strideWidthTerm;
			scan22 += strideWidthTerm;
		}
		b.UnlockBits(bitmapData);
		bitmap.UnlockBits(bitmapData2);
		return bitmap;
	}

	public unsafe static Bitmap Convolve(Bitmap b, Blur blur)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			throw new NotImplementedException();
		}
		Bitmap bitmap = (Bitmap)b.Clone();
		BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		BitmapData bitmapData2 = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		int height = blur.Height;
		int heightTerm = (int)Math.Floor((float)height / 2f);
		int stride = bitmapData.Stride;
		int[,] intValues = new int[height, height];
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < height; j++)
			{
				intValues[i, j] = i * stride + j * 3;
			}
		}
		nint scan = bitmapData.Scan0;
		nint scan2 = bitmapData2.Scan0;
		byte* scan3 = (byte*)scan;
		byte* scan22 = (byte*)scan2;
		int strideWidthTerm = stride - b.Width * 3;
		int count = b.Width - height - 1;
		int count2 = b.Height - height - 1;
		for (int k = 0; k < count2; k++)
		{
			for (int l = 0; l < count; l++)
			{
				int scan32 = 0;
				for (int m = 0; m < height; m++)
				{
					for (int n = 0; n < height; n++)
					{
						scan32 += (int)((float)(int)scan22[2 + intValues[m, n]] * blur.Matrix[m, n]);
					}
				}
				scan32 = (int)((float)scan32 * blur.Factor);
				scan32 += blur.Offset;
				if (scan32 < 0)
				{
					scan32 = 0;
				}
				if (scan32 > 255)
				{
					scan32 = 255;
				}
				scan3[2 + intValues[heightTerm, heightTerm]] = (byte)scan32;
				int scan33 = 0;
				for (int intervalIndex = 0; intervalIndex < height; intervalIndex++)
				{
					for (int intervalIndex2 = 0; intervalIndex2 < height; intervalIndex2++)
					{
						scan33 += (int)((float)(int)scan22[1 + intValues[intervalIndex, intervalIndex2]] * blur.Matrix[intervalIndex, intervalIndex2]);
					}
				}
				scan33 = (int)((float)scan33 * blur.Factor);
				scan33 += blur.Offset;
				if (scan33 < 0)
				{
					scan33 = 0;
				}
				if (scan33 > 255)
				{
					scan33 = 255;
				}
				scan3[1 + intValues[heightTerm, heightTerm]] = (byte)scan33;
				int scan34 = 0;
				for (int intervalIndex3 = 0; intervalIndex3 < height; intervalIndex3++)
				{
					for (int intervalIndex4 = 0; intervalIndex4 < height; intervalIndex4++)
					{
						scan34 += (int)((float)(int)scan22[intValues[intervalIndex3, intervalIndex4]] * blur.Matrix[intervalIndex3, intervalIndex4]);
					}
				}
				scan34 = (int)((float)scan34 * blur.Factor);
				scan34 += blur.Offset;
				if (scan34 < 0)
				{
					scan34 = 0;
				}
				if (scan34 > 255)
				{
					scan34 = 255;
				}
				scan3[intValues[heightTerm, heightTerm]] = (byte)scan34;
				scan3 += 3;
				scan22 += 3;
			}
			scan3 -= 3;
			scan22 -= 3;
			scan3 += strideWidthTerm;
			scan22 += strideWidthTerm;
		}
		b.UnlockBits(bitmapData2);
		bitmap.UnlockBits(bitmapData);
		return bitmap;
	}

	public unsafe static Bitmap Convolve(Bitmap b, BlurDouble blur, int taper = 5)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			throw new NotImplementedException();
		}
		Bitmap bitmap = (Bitmap)b.Clone();
		BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		BitmapData bitmapData2 = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		int size = blur.Height;
		int mid = (int)Math.Floor((double)(float)size / 2.0);
		int stride = bitmapData.Stride;
		int[,] offsets = new int[size, size];
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				offsets[i, j] = i * stride + j * 3;
			}
		}
		nint scan = bitmapData.Scan0;
		nint scan2 = bitmapData2.Scan0;
		byte* writePtr = (byte*)scan;
		byte* readPtr = (byte*)scan2;
		int nWidth = b.Width - size - 1;
		int nHeight = b.Height - size - 1;
		bool allowTaper = taper > 0;
		int lBoundTaper = taper;
		int rBoundTaper = nWidth - taper - 1;
		int uBoundTaper = taper;
		int bBoundTaper = nHeight - taper - 1;
		Parallel.For(0, nHeight, (int y) =>
		{
			for (int k = 0; k < nWidth; k++)
			{
				int strideTerm = y * stride + k * 3;
				double writePtr2 = 0.0;
				for (int l = 0; l < size; l++)
				{
					for (int m = 0; m < size; m++)
					{
						writePtr2 += (double)(int)readPtr[strideTerm + 2 + offsets[l, m]] * blur.Matrix[l, m];
					}
				}
				writePtr2 *= blur.Factor;
				writePtr2 += (double)blur.Offset;
				if (writePtr2 < 0.0)
				{
					writePtr2 = 0.0;
				}
				if (writePtr2 > 255.0)
				{
					writePtr2 = 255.0;
				}
				int offsetsMidTerm = strideTerm + 2 + offsets[mid, mid];
				writePtr[offsetsMidTerm] = (byte)writePtr2;
				double writePtr3 = 0.0;
				for (int n = 0; n < size; n++)
				{
					for (int intervalIndex = 0; intervalIndex < size; intervalIndex++)
					{
						writePtr3 += (double)(int)readPtr[strideTerm + 1 + offsets[n, intervalIndex]] * blur.Matrix[n, intervalIndex];
					}
				}
				writePtr3 *= blur.Factor;
				writePtr3 += (double)blur.Offset;
				if (writePtr3 < 0.0)
				{
					writePtr3 = 0.0;
				}
				if (writePtr3 > 255.0)
				{
					writePtr3 = 255.0;
				}
				int offsetsMidTerm2 = strideTerm + 1 + offsets[mid, mid];
				writePtr[offsetsMidTerm2] = (byte)writePtr3;
				double writePtr4 = 0.0;
				for (int intervalIndex2 = 0; intervalIndex2 < size; intervalIndex2++)
				{
					for (int intervalIndex3 = 0; intervalIndex3 < size; intervalIndex3++)
					{
						writePtr4 += (double)(int)readPtr[strideTerm + offsets[intervalIndex2, intervalIndex3]] * blur.Matrix[intervalIndex2, intervalIndex3];
					}
				}
				writePtr4 *= blur.Factor;
				writePtr4 += (double)blur.Offset;
				if (writePtr4 < 0.0)
				{
					writePtr4 = 0.0;
				}
				if (writePtr4 > 255.0)
				{
					writePtr4 = 255.0;
				}
				int offsetsMidTerm3 = strideTerm + offsets[mid, mid];
				writePtr[offsetsMidTerm3] = (byte)writePtr4;
				if (allowTaper)
				{
					if (k < lBoundTaper)
					{
						float taperTerm = (float)(taper - k) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm2 = (float)(taper - y) / (float)taper;
							float computed = 1f - (1f - taperTerm) * (1f - taperTerm2);
							float computed2 = 1f - computed;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed2 + (float)(int)readPtr[offsetsMidTerm] * computed);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed2 + (float)(int)readPtr[offsetsMidTerm2] * computed);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed2 + (float)(int)readPtr[offsetsMidTerm3] * computed);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm = (float)(taper - (nHeight - y - 1)) / (float)taper;
							float computed3 = 1f - (1f - taperTerm) * (1f - taperNHeightTerm);
							float computed4 = 1f - computed3;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed4 + (float)(int)readPtr[offsetsMidTerm] * computed3);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed4 + (float)(int)readPtr[offsetsMidTerm2] * computed3);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed4 + (float)(int)readPtr[offsetsMidTerm3] * computed3);
						}
						else
						{
							float computed5 = taperTerm;
							float computed6 = 1f - computed5;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed6 + (float)(int)readPtr[offsetsMidTerm] * computed5);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed6 + (float)(int)readPtr[offsetsMidTerm2] * computed5);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed6 + (float)(int)readPtr[offsetsMidTerm3] * computed5);
						}
					}
					else if (k > rBoundTaper)
					{
						float taperNWidthTerm = (float)(taper - (nWidth - k - 1)) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm3 = (float)(taper - y) / (float)taper;
							float computed7 = 1f - (1f - taperNWidthTerm) * (1f - taperTerm3);
							float computed8 = 1f - computed7;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed8 + (float)(int)readPtr[offsetsMidTerm] * computed7);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed8 + (float)(int)readPtr[offsetsMidTerm2] * computed7);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed8 + (float)(int)readPtr[offsetsMidTerm3] * computed7);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm2 = (float)(taper - (nHeight - y - 1)) / (float)taper;
							float computed9 = 1f - (1f - taperNWidthTerm) * (1f - taperNHeightTerm2);
							float computed10 = 1f - computed9;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed10 + (float)(int)readPtr[offsetsMidTerm] * computed9);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed10 + (float)(int)readPtr[offsetsMidTerm2] * computed9);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed10 + (float)(int)readPtr[offsetsMidTerm3] * computed9);
						}
						else
						{
							float computed11 = taperNWidthTerm;
							float computed12 = 1f - computed11;
							writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed12 + (float)(int)readPtr[offsetsMidTerm] * computed11);
							writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed12 + (float)(int)readPtr[offsetsMidTerm2] * computed11);
							writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed12 + (float)(int)readPtr[offsetsMidTerm3] * computed11);
						}
					}
					else if (y < uBoundTaper)
					{
						float taperTerm4 = (float)(taper - y) / (float)taper;
						float computed13 = 1f - taperTerm4;
						writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed13 + (float)(int)readPtr[offsetsMidTerm] * taperTerm4);
						writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed13 + (float)(int)readPtr[offsetsMidTerm2] * taperTerm4);
						writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed13 + (float)(int)readPtr[offsetsMidTerm3] * taperTerm4);
					}
					else if (y > bBoundTaper)
					{
						float taperNHeightTerm3 = (float)(taper - (nHeight - y - 1)) / (float)taper;
						float computed14 = 1f - taperNHeightTerm3;
						writePtr[offsetsMidTerm] = (byte)((float)(int)writePtr[offsetsMidTerm] * computed14 + (float)(int)readPtr[offsetsMidTerm] * taperNHeightTerm3);
						writePtr[offsetsMidTerm2] = (byte)((float)(int)writePtr[offsetsMidTerm2] * computed14 + (float)(int)readPtr[offsetsMidTerm2] * taperNHeightTerm3);
						writePtr[offsetsMidTerm3] = (byte)((float)(int)writePtr[offsetsMidTerm3] * computed14 + (float)(int)readPtr[offsetsMidTerm3] * taperNHeightTerm3);
					}
				}
			}
		});
		b.UnlockBits(bitmapData2);
		bitmap.UnlockBits(bitmapData);
		return bitmap;
	}

	public unsafe static Bitmap ConvolveBoxSAT(Bitmap bmp, int radius = 5, float scale = 0.3f, int offset = 160, int taper = 5)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			throw new NotImplementedException();
		}
		radius = Math.Abs(radius);
		Bitmap bitmap = (Bitmap)bmp.Clone();
		if (radius == 0)
		{
			return bitmap;
		}
		BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
		BitmapData bitmapData2 = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		int scaledRadius = radius * 2;
		int stride = bitmapData.Stride;
		nint scan = bitmapData.Scan0;
		nint scan2 = bitmapData2.Scan0;
		byte* writePtr = (byte*)scan;
		byte* scan22 = (byte*)scan2;
		SummedAreaTableWithBoundary(scan22, bmp.Width, bmp.Height, radius, stride, out var r, out var g, out var b);
		ComputeFromRGBSAT(scan22, writePtr, r, g, b, radius, taper, bmp.Width, bmp.Height, stride, scale, offset);
		bmp.UnlockBits(bitmapData2);
		bitmap.UnlockBits(bitmapData);
		return bitmap;
	}

	public unsafe static byte[,] ConvolveBoxSAT(byte[,] buf, int radius = 5, float scale = 0.3f, int offset = 160, int taper = 5)
	{
		radius = Math.Abs(radius);
		byte[,] byteValues = (byte[,])buf.Clone();
		if (radius == 0)
		{
			return byteValues;
		}
		int length = buf.GetLength(1);
		int length2 = buf.GetLength(0);
		int scaledRadius = radius * 2;
		int length3 = length;
		fixed (byte* writePtr = byteValues)
		{
			fixed (byte* buf2 = buf)
			{
				SummedAreaTableWithBoundary(buf2, length, length2, radius, length3, out var r, out var g, out var b);
				ComputeFromRGBSAT(buf2, writePtr, r, g, b, radius, taper, length, length2, length3, scale, offset);
			}
		}
		throw new NotImplementedException();
	}

	public unsafe static byte[] ConvolveBoxSAT(byte[] buf, int width, int height, int radius = 5, float scale = 0.3f, int offset = 160, int taper = 5)
	{
		if (buf.Length != width * height)
		{
			throw new ArgumentException("Buffer is not width * height");
		}
		radius = Math.Abs(radius);
		byte[] byteValues = (byte[])buf.Clone();
		if (radius == 0)
		{
			return byteValues;
		}
		int scaledRadius = radius * 2;
		fixed (byte* writePtr = byteValues)
		{
			fixed (byte* buf2 = buf)
			{
				SummedAreaTableWithBoundary(buf2, width, height, radius, width, out var r, out var g, out var b);
				ComputeFromRGBSAT(buf2, writePtr, r, g, b, radius, taper, width, height, width, scale, offset);
			}
		}
		throw new NotImplementedException();
	}

	private unsafe static void ComputeFromRGBSAT(byte* readPtr, byte* writePtr, int[,] r, int[,] g, int[,] b, int radius, int taper, int nWidth, int nHeight, int strideBytes, float scale, int offset)
	{
		int scaledRadius = radius * 2;
		int area = (scaledRadius + 1) * (scaledRadius + 1);
		bool allowTaper = taper > 0;
		int lBoundTaper = taper;
		int rBoundTaper = nWidth - taper - 1;
		int uBoundTaper = taper;
		int bBoundTaper = nHeight - taper - 1;
		Parallel.For(0, nHeight, (int y) =>
		{
			int strideBytesTerm = y * strideBytes;
			int radiusTerm = radius * 2 + 1;
			int radiusTerm2 = y + radius * 2 + 1;
			for (int i = 0; i < nWidth; i++)
			{
				int ordinal = b[y, i];
				int ordinal2 = b[y, radiusTerm];
				int ordinal3 = b[radiusTerm2, i];
				int writePtr2 = (int)((float)(b[radiusTerm2, radiusTerm] - ordinal2 - ordinal3 + ordinal) * scale / (float)area) + offset;
				if (writePtr2 < 0)
				{
					writePtr2 = 0;
				}
				if (writePtr2 > 255)
				{
					writePtr2 = 255;
				}
				int ordinal4 = strideBytesTerm;
				writePtr[ordinal4] = (byte)writePtr2;
				strideBytesTerm++;
				ordinal = g[y, i];
				ordinal2 = g[y, radiusTerm];
				ordinal3 = g[radiusTerm2, i];
				writePtr2 = (int)((float)(g[radiusTerm2, radiusTerm] - ordinal2 - ordinal3 + ordinal) * scale / (float)area) + offset;
				if (writePtr2 < 0)
				{
					writePtr2 = 0;
				}
				if (writePtr2 > 255)
				{
					writePtr2 = 255;
				}
				int ordinal5 = strideBytesTerm;
				writePtr[ordinal5] = (byte)writePtr2;
				strideBytesTerm++;
				ordinal = r[y, i];
				ordinal2 = r[y, radiusTerm];
				ordinal3 = r[radiusTerm2, i];
				writePtr2 = (int)((float)(r[radiusTerm2, radiusTerm] - ordinal2 - ordinal3 + ordinal) * scale / (float)area) + offset;
				if (writePtr2 < 0)
				{
					writePtr2 = 0;
				}
				if (writePtr2 > 255)
				{
					writePtr2 = 255;
				}
				int ordinal6 = strideBytesTerm;
				writePtr[ordinal6] = (byte)writePtr2;
				strideBytesTerm++;
				radiusTerm++;
				if (allowTaper)
				{
					float computed = 0f;
					if (i < lBoundTaper)
					{
						float taperTerm = (float)(taper - i) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm2 = (float)(taper - y) / (float)taper;
							computed = 1f - (1f - taperTerm) * (1f - taperTerm2);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm = (float)(taper - (nHeight - y - 1)) / (float)taper;
							computed = 1f - (1f - taperTerm) * (1f - taperNHeightTerm);
						}
						else
						{
							computed = taperTerm;
						}
					}
					else if (i > rBoundTaper)
					{
						float taperNWidthTerm = (float)(taper - (nWidth - 1 - i)) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm3 = (float)(taper - y) / (float)taper;
							computed = 1f - (1f - taperNWidthTerm) * (1f - taperTerm3);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm2 = (float)(taper - (nHeight - y - 1)) / (float)taper;
							computed = 1f - (1f - taperNWidthTerm) * (1f - taperNHeightTerm2);
						}
						else
						{
							computed = taperNWidthTerm;
						}
					}
					else if (y < uBoundTaper)
					{
						computed = (float)(taper - y) / (float)taper;
					}
					else if (y > bBoundTaper)
					{
						computed = (float)(taper - (nHeight - y - 1)) / (float)taper;
					}
					computed *= 0.8f;
					float computed2 = 1f - computed;
					writePtr[ordinal6] = (byte)((float)(int)writePtr[ordinal6] * computed2 + (float)(int)readPtr[ordinal6] * computed);
					writePtr[ordinal5] = (byte)((float)(int)writePtr[ordinal5] * computed2 + (float)(int)readPtr[ordinal5] * computed);
					writePtr[ordinal4] = (byte)((float)(int)writePtr[ordinal4] * computed2 + (float)(int)readPtr[ordinal4] * computed);
				}
			}
		});
	}

	private unsafe static void SummedAreaTable(byte* read, int w, int h, int stride, out int[,] r, out int[,] g, out int[,] b)
	{
		int[,] red = new int[h, w];
		int[,] grn = new int[h, w];
		int[,] blu = new int[h, w];
		Parallel.For(0, h, (int y) =>
		{
			int readSum = 0;
			int readSum2 = 0;
			int readSum3 = 0;
			int strideTerm = y * stride;
			for (int i = 0; i < w; i++)
			{
				readSum += read[strideTerm];
				readSum2 += read[strideTerm + 1];
				readSum3 += read[strideTerm + 2];
				blu[y, i] = readSum;
				grn[y, i] = readSum2;
				red[y, i] = readSum3;
				strideTerm += 3;
			}
		});
		Parallel.For(0, w, (int x) =>
		{
			for (int i = 1; i < h; i++)
			{
				blu[i, x] += blu[i - 1, x];
				grn[i, x] += grn[i - 1, x];
				red[i, x] += red[i - 1, x];
			}
		});
		r = red;
		g = grn;
		b = blu;
	}

	private unsafe static void SummedAreaTableWithBoundary(byte* read, int w, int h, int radius, int stride, out int[,] r, out int[,] g, out int[,] b)
	{
		int topLeftBoundaryPx = radius + 1;
		int radius2 = radius;
		int extraPxWH = topLeftBoundaryPx + radius2;
		int[,] red = new int[h + extraPxWH, w + extraPxWH];
		int[,] grn = new int[h + extraPxWH, w + extraPxWH];
		int[,] blu = new int[h + extraPxWH, w + extraPxWH];
		Parallel.For(0, h + extraPxWH, (int y) =>
		{
			int readSum = 0;
			int readSum2 = 0;
			int readSum3 = 0;
			int a = (y - radius) * stride;
			a = a.Bound(0, (h - 1) * stride);
			for (int i = 0; i < topLeftBoundaryPx; i++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, i] = readSum;
				grn[y, i] = readSum2;
				red[y, i] = readSum3;
			}
			for (int j = topLeftBoundaryPx; j < w + topLeftBoundaryPx; j++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, j] = readSum;
				grn[y, j] = readSum2;
				red[y, j] = readSum3;
				a += 3;
			}
			a -= 3;
			for (int k = w + topLeftBoundaryPx; k < w + extraPxWH; k++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, k] = readSum;
				grn[y, k] = readSum2;
				red[y, k] = readSum3;
			}
		});
		Parallel.For(0, w + extraPxWH, (int x) =>
		{
			for (int i = 1; i < h + extraPxWH; i++)
			{
				blu[i, x] += blu[i - 1, x];
				grn[i, x] += grn[i - 1, x];
				red[i, x] += red[i - 1, x];
			}
		});
		r = red;
		g = grn;
		b = blu;
	}
}
