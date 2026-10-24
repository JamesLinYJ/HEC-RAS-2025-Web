using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RASEdgeLines : PolylineFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/River Edge Lines";

	private const string sPlotComputedEdgeLines = "Plot Computed Edge Lines ";

	public const string HashAttributeName = "Source Data Hash";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanRename { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Edge Lines";
string IGeometryLayer.HDFGroupName=> "Geometry/River Edge Lines";

	public RASEdgeLines(RASGeometry geometry)
		: base("Edge Lines", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		CanRename = false;
		SimpleFeatureTypeName = "Edge Line";
		Geometry = geometry;
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/River Edge Lines/Polyline");
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/River Edge Lines");
			return;
		}
		hw.CreateGroup("Geometry/River Edge Lines");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/River Edge Lines/Polyline");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline(i).Stream(bw);
			}
		}
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot Computed Edge Lines "));
		return staticPlotOptions;
	}

	private void PlotComputedEdgeLines()
	{
		PlotOption plotOption = AvailablePlotOptions.Where([SpecialName] (PlotOption plotopt) => Operators.CompareString(plotopt.Name, "Plot Computed Edge Lines ", TextCompare: false) == 0).FirstOrDefault();
		if ((object)plotOption != null)
		{
			SelectedPlotOptions.Add(plotOption);
		}
	}

	public static RASEdgeLines Stream(BinaryReader br, RASGeometry geom)
	{
		RASEdgeLines rASEdgeLines = new RASEdgeLines(geom);
		rASEdgeLines.InitializeEmptyFeatureTable();
		rASEdgeLines.SuppressTableReload(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline feature = RasMapperLib.Polyline.Stream(br);
				rASEdgeLines.AddFeature(feature);
			}
			return rASEdgeLines;
		}
	}

	public bool EnsureEdgeLines()
	{
		if (NeedToRecompute())
		{
			return ComputeEdgeLines();
		}
		return true;
	}

	private bool NeedToRecompute()
	{
		byte[] sourceDataHash = ComputeSourceDataHash();
		if (sourceDataHash == null)
		{
			return false;
		}
		byte[] hash = ReadStoredHash();
		if (Hash.HashEquals(sourceDataHash, hash))
		{
			return false;
		}
		return true;
	}

	private byte[] ComputeSourceDataHash()
	{
		RASGeometry geometry = Geometry;
		if (geometry.XS.FeatureCount() == 0)
		{
			return null;
		}
		if (geometry.IsResultFile)
		{
			return null;
		}
		Hash hash = new Hash();
		geometry.XS.MD5HashAppend(hash);
		return hash.HashBytes();
	}

	private byte[] ReadStoredHash()
	{
		if (!File.Exists(SourceFilename))
		{
			return null;
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		if (h5Reader.ExistsAsGroup("Geometry/River Edge Lines") && h5Reader.ExistsAsAttribute("Geometry/River Edge Lines", "Source Data Hash"))
		{
			byte[] values = null;
			if (h5Reader.TryReadAttribute("Geometry/River Edge Lines", "Source Data Hash", ref values))
			{
				return values;
			}
		}
		return null;
	}

	private void ComputeEdgeLines(object sender, EventArgs e)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			ComputeEdgeLines();
		}
		SharedData.RASMapperReplot();
	}

	public bool ComputeEdgeLines(bool saveToHDF = true)
	{
		checked
		{
			bool result;
			try
			{
				Console.WriteLine("LAYER=" + Name);
				using (new FeatureTableReloadSuppressor(this))
				{
					ResetFeatureTable();
					RASGeometry geometry = Geometry;
					RASRiver river = geometry.River;
					RASXS xS = geometry.XS;
					RASBankLines bankLines = geometry.BankLines;
					bankLines.EnsureBankLines();
					DataTable dataTable = xS.FeatureTable();
					DataTable dataTable2 = river.FeatureTable();
					if ((dataTable.Rows.Count == 0) | (dataTable2.Rows.Count == 0))
					{
						result = true;
					}
					else
					{
						double filterToleranceXY = geometry.FilterToleranceXY;
						OffsetStreamLinesFactory offsetStreamLinesFactory = new OffsetStreamLinesFactory(filterToleranceXY * 2.0);
						PointMs pointMs = new PointMs();
						PointMs pointMs2 = new PointMs();
						PointMs pointMs3 = new PointMs();
						PointMs pointMs4 = new PointMs();
						PointMs pointMs5 = new PointMs();
						PointMs pointMs6 = new PointMs();
						SharedData.ReportMessage("Computing Edge Lines");
						Stopwatch stopwatch = new Stopwatch();
						stopwatch.Start();
						int count = river.FeatureCount() - 1;
						int index = default;
						int index2 = default;
						int index3 = default;
						int index4 = default;
						for (int i = 0; i <= count; i++)
						{
							Console.WriteLine("PROGRESS=" + Conversions.ToString((double)(i + 1) / (double)river.FeatureCount()));
							Polyline polyline = river.Feature(i) as Polyline;
							List<PolylineIntersection> riverXSIntersections = geometry.RiverXSIntersections(i);
							pointMs5.Clear();
							pointMs6.Clear();
							int count2 = riverXSIntersections.Count - 1;
							for (int j = 1; j <= count2; j++)
							{
								PolylineIntersection polylineIntersection = riverXSIntersections[j - 1];
								PolylineIntersection polylineIntersection2 = riverXSIntersections[j];
								PointOnPolyline aPointOnPolyline = polylineIntersection.APointOnPolyline;
								PointOnPolyline aPointOnPolyline2 = polylineIntersection2.APointOnPolyline;
								PointOnPolyline bPointOnPolyline = polylineIntersection.BPointOnPolyline;
								PointOnPolyline bPointOnPolyline2 = polylineIntersection2.BPointOnPolyline;
								polyline.PointSubsection(aPointOnPolyline, aPointOnPolyline2, pointMs);
								int bPolylineIndex = polylineIntersection.BPolylineIndex;
								int bPolylineIndex2 = polylineIntersection2.BPolylineIndex;
								if (bPolylineIndex == bPolylineIndex2)
								{
									continue;
								}
								_ = dataTable.Rows[bPolylineIndex];
								_ = dataTable.Rows[bPolylineIndex2];
								Polyline polyline2 = xS.Polyline(bPolylineIndex);
								Polyline polyline3 = xS.Polyline(bPolylineIndex2);
								List<PolylineIntersection> xsIntersectBanks = geometry.XSIntersectBanks(bPolylineIndex);
								List<PolylineIntersection> xsIntersectBanksList = geometry.XSIntersectBanks(bPolylineIndex2);
								xsIntersectBanks.Sort(PolylineIntersection.CompareB);
								xsIntersectBanksList.Sort(PolylineIntersection.CompareB);
								double maxValue = double.MaxValue;
								double maxValue2 = double.MaxValue;
								int count3 = xsIntersectBanks.Count - 1;
								for (int k = 0; k <= count3; k++)
								{
									int count4 = xsIntersectBanksList.Count - 1;
									for (int l = 0; l <= count4; l++)
									{
										if (xsIntersectBanks[k].APolylineIndex != xsIntersectBanksList[l].APolylineIndex)
										{
											continue;
										}
										PointOnPolyline bPointOnPolyline3 = xsIntersectBanks[k].BPointOnPolyline;
										PointOnPolyline bPointOnPolyline4 = xsIntersectBanksList[l].BPointOnPolyline;
										double distanceToPointMTerm = bPointOnPolyline3.PointM.DistanceTo(bPointOnPolyline.PointM) + bPointOnPolyline4.PointM.DistanceTo(bPointOnPolyline2.PointM);
										if (bPointOnPolyline3.LessThan(bPointOnPolyline) && bPointOnPolyline4.LessThan(bPointOnPolyline2))
										{
											if (distanceToPointMTerm < maxValue)
											{
												index = k;
												index2 = l;
												maxValue = distanceToPointMTerm;
											}
										}
										else if (bPointOnPolyline.LessThan(bPointOnPolyline3) && bPointOnPolyline2.LessThan(bPointOnPolyline4) && distanceToPointMTerm < maxValue2)
										{
											index3 = k;
											index4 = l;
											maxValue2 = distanceToPointMTerm;
										}
									}
								}
								PointOnPolyline usxsPt = polyline2.FirstPointOnPolyline();
								PointOnPolyline dsxsPt = polyline3.FirstPointOnPolyline();
								if (maxValue < double.MaxValue)
								{
									bankLines.Polyline(xsIntersectBanks[index].APolylineIndex).PointSubsection(xsIntersectBanks[index].APointOnPolyline, xsIntersectBanksList[index2].APointOnPolyline, pointMs3);
									offsetStreamLinesFactory.ComputeOffset(pointMs3, xsIntersectBanks[index].BPointOnPolyline, xsIntersectBanksList[index2].BPointOnPolyline, polyline2, usxsPt, polyline3, dsxsPt, pointMs2);
									pointMs5.AddRange(pointMs2, filterToleranceXY);
								}
								else
								{
									offsetStreamLinesFactory.ComputeOffset(pointMs, bPointOnPolyline, bPointOnPolyline2, polyline2, usxsPt, polyline3, dsxsPt, pointMs2);
									pointMs5.AddRange(pointMs2, filterToleranceXY);
								}
								PointOnPolyline usxsPt2 = polyline2.LastPointOnPolyline();
								PointOnPolyline dsxsPt2 = polyline3.LastPointOnPolyline();
								if (maxValue2 < double.MaxValue)
								{
									bankLines.Polyline(xsIntersectBanks[index3].APolylineIndex).PointSubsection(xsIntersectBanks[index3].APointOnPolyline, xsIntersectBanksList[index4].APointOnPolyline, pointMs4);
									offsetStreamLinesFactory.ComputeOffset(pointMs4, xsIntersectBanks[index3].BPointOnPolyline, xsIntersectBanksList[index4].BPointOnPolyline, polyline2, usxsPt2, polyline3, dsxsPt2, pointMs2);
									pointMs6.AddRange(pointMs2, filterToleranceXY);
								}
								else
								{
									offsetStreamLinesFactory.ComputeOffset(pointMs, bPointOnPolyline, bPointOnPolyline2, polyline2, usxsPt2, polyline3, dsxsPt2, pointMs2);
									pointMs6.AddRange(pointMs2, filterToleranceXY);
								}
							}
							pointMs5.TrimSelfIntersections();
							pointMs6.TrimSelfIntersections();
							if (pointMs5.Count > 0)
							{
								AddFeature(new Polyline(pointMs5));
							}
							if (pointMs6.Count > 0)
							{
								AddFeature(new Polyline(pointMs6));
							}
						}
						stopwatch.Stop();
						SharedData.ReportMessage("Edge Lines generated in " + Conversions.ToString(stopwatch.ElapsedMilliseconds) + " ms", replacePreviousIfPossible: true);
						if (saveToHDF)
						{
							SaveFeatureTable();
							// Saving creates the group for generated features, or
							// removes it when no edge lines were produced.
							if (FeatureCount() > 0)
							{
								using (H5Writer h5Writer = new H5Writer(SourceFilename))
								{
									byte[] sourceDataHash = ComputeSourceDataHash();
									if (sourceDataHash != null)
									{
										h5Writer.WriteAttribute("Geometry/River Edge Lines", "Source Data Hash", sourceDataHash);
									}
								}
							}
						}
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				SharedData.ReportMessage("Error generating edge lines");
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}
}
