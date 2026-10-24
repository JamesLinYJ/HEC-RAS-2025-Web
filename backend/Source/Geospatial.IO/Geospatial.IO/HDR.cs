using System;
using System.IO;
using Geospatial.Vectors;

namespace Geospatial.IO;

public class HDR
{
	private static readonly string ncols = "ncols";

	private static readonly string nrows = "nrows";

	private static readonly string cellsize = "cellsize";

	private static readonly string xllCorner = "xllcorner";

	private static readonly string yllCorner = "yllcorner";

	private static readonly string nodataval = "NODATA_value";

	public int NColumns { get; private set; }

	public int NRows { get; private set; }

	public double XLLCorner { get; private set; }

	public double XULCorner => XLLCorner;

	public double XLRCorner => XLLCorner + (double)((float)NColumns * CellSize);

	public double XURCorner => XLRCorner;

	public double YLLCorner { get; private set; }

	public double YULCorner => YLLCorner + (double)((float)NRows * CellSize);

	public double YLRCorner => YLLCorner;

	public double YURCorner => YULCorner;

	public float CellSize { get; private set; }

	public float NoDataValue { get; private set; }

	internal HDR(string HDRFilename)
	{
		if (!File.Exists(HDRFilename))
		{
			throw new Exception(HDRFilename + " not detected.");
		}
		string[] allLines = File.ReadAllLines(HDRFilename);
		for (int i = 0; i < allLines.Length; i++)
		{
			string allLine = allLines[i];
			if (allLine.Length >= ncols.Length && allLine.Substring(0, ncols.Length) == ncols)
			{
				allLine = allLine.Substring(ncols.Length).Trim();
				NColumns = Convert.ToInt32(allLine);
			}
			else if (allLine.Length >= nrows.Length && allLine.Substring(0, nrows.Length) == nrows)
			{
				allLine = allLine.Substring(nrows.Length).Trim();
				NRows = Convert.ToInt32(allLine);
			}
			else if (allLine.Length >= nodataval.Length && allLine.Substring(0, nodataval.Length) == nodataval)
			{
				allLine = allLine.Substring(nodataval.Length).Trim();
				NoDataValue = Convert.ToSingle(allLine);
			}
			else if (allLine.Length >= xllCorner.Length && allLine.Substring(0, xllCorner.Length) == xllCorner)
			{
				allLine = allLine.Substring(xllCorner.Length).Trim();
				XLLCorner = Convert.ToDouble(allLine);
			}
			else if (allLine.Length >= yllCorner.Length && allLine.Substring(0, yllCorner.Length) == yllCorner)
			{
				allLine = allLine.Substring(yllCorner.Length).Trim();
				YLLCorner = Convert.ToDouble(allLine);
			}
			else if (allLine.Length >= cellsize.Length && allLine.Substring(0, cellsize.Length) == cellsize)
			{
				allLine = allLine.Substring(cellsize.Length).Trim();
				CellSize = Convert.ToSingle(allLine);
			}
		}
	}

	public static void MakeHDR(Extent ext, double cellLen, int nCols, int nRows, string HDRName, float NoDataValue)
	{
		File.Delete(HDRName);
		string ncolsNColsTerm = ncols + " " + nCols + "\n";
		ncolsNColsTerm = ncolsNColsTerm + nrows + " " + nRows + "\n";
		ncolsNColsTerm = ncolsNColsTerm + xllCorner + " " + ext.LowerLeft.X + "\n";
		ncolsNColsTerm = ncolsNColsTerm + yllCorner + " " + ext.LowerLeft.Y + "\n";
		ncolsNColsTerm = ncolsNColsTerm + cellsize + " " + cellLen + "\n";
		ncolsNColsTerm = ncolsNColsTerm + nodataval + " " + NoDataValue;
		File.WriteAllText(HDRName, ncolsNColsTerm);
	}
}
