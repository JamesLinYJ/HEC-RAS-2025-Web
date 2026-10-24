using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class RASBankLines : PolylineFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/River Bank Lines";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Bank Lines";
string IGeometryLayer.HDFGroupName=> "Geometry/River Bank Lines";

	public RASBankLines(RASGeometry geometry)
		: base("Bank Lines", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Bankline";
		ForwardEditRequestsToParent = true;
		CanRename = false;
		Geometry = geometry;
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/River Bank Lines"))
		{
			if (hr.ExistsAsDataset("Geometry/River Bank Lines/Polyline Info"))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/River Bank Lines/Polyline");
			}
			else
			{
				HDFLoadFeatureTable_503(hr);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFLoadFeatureTable_503(H5Reader hr)
	{
		HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/River Bank Lines/Bank Lines");
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/River Bank Lines");
			return;
		}
		hw.CreateGroup("Geometry/River Bank Lines");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/River Bank Lines/Polyline");
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

	public static RASBankLines Stream(BinaryReader br, RASGeometry geom)
	{
		RASBankLines rASBankLines = new RASBankLines(geom);
		rASBankLines.InitializeEmptyFeatureTable();
		rASBankLines.SuppressTableReload(boolValue: true);
		rASBankLines.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline feature = RasMapperLib.Polyline.Stream(br);
				rASBankLines.AddFeature(feature);
			}
			return rASBankLines;
		}
	}

	public bool EnsureBankLines()
	{
		if (NeedToRecompute())
		{
			return ComputeBankLines();
		}
		return true;
	}

	private bool NeedToRecompute()
	{
		RASGeometry geometry = Geometry;
		if (geometry.IsResultFile)
		{
			return false;
		}
		if (geometry.XS.FeatureCount() == 0)
		{
			return false;
		}
		return FeatureCount() == 0;
	}

	public bool ComputeBankLines(List<BankProperties> overrideBankPropertiesList = null, bool combineBankLinesOnSameRiver = true)
	{
		if (overrideBankPropertiesList != null && overrideBankPropertiesList.Count != Geometry.XS.FeatureCount())
		{
			throw new ArgumentException("The override bank properties list must be the same length as the feature count of XS layer.");
		}
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
						if (overrideBankPropertiesList == null)
						{
							SharedData.ReportMessage("Computing Bank Lines");
						}
						Geometry.Errors.ClearErrors();
						string proc = "Checking for Bank Stations";
						int count = xS.FeatureCount() - 1;
						for (int i = 0; i <= count; i++)
						{
							BankProperties bankProperties;
							if (overrideBankPropertiesList != null)
							{
								bankProperties = overrideBankPropertiesList[i];
								if (bankProperties == null)
								{
									continue;
								}
							}
							else
							{
								bankProperties = xS.GetBankProperties(i);
							}
							if (float.IsNaN(bankProperties.LeftBankFraction) && float.IsNaN(bankProperties.RightBankFraction))
							{
								Geometry.Errors.AddError(new GeometryErrorCollection(this, i, new GeometryError[1]
								{
									new GeometryError("Missing both left and right bank stations", proc)
								}));
							}
							else if (float.IsNaN(bankProperties.LeftBankFraction))
							{
								Geometry.Errors.AddError(new GeometryErrorCollection(this, i, new GeometryError[1]
								{
									new GeometryError("Missing left bank station", proc)
								}));
							}
							else if (float.IsNaN(bankProperties.RightBankFraction))
							{
								Geometry.Errors.AddError(new GeometryErrorCollection(this, i, new GeometryError[1]
								{
									new GeometryError("Missing right bank station", proc)
								}));
							}
						}
						Stopwatch stopwatch = new Stopwatch();
						stopwatch.Start();
						HashSet<int> intSet = new HashSet<int>();
						int count2 = dataTable2.Rows.Count - 1;
						double actualDistance = default;
						for (int j = 0; j <= count2; j++)
						{
							Console.WriteLine("PROGRESS=" + Conversions.ToString((double)(j + 1) / (double)river.FeatureCount()));
							if (intSet.Contains(j))
							{
								continue;
							}
							List<Polyline> polylines = new List<Polyline>();
							int ordinal = 0;
							polylines.Add((Polyline)river.Feature(j));
							List<PolylineIntersection> riverXSIntersections = new List<PolylineIntersection>(geometry.RiverXSIntersections(j));
							if (combineBankLinesOnSameRiver)
							{
								string riverName = geometry.River.GetRiverName(j);
								int ordinal2 = j + 1;
								int count3 = dataTable2.Rows.Count - 1;
								for (int k = ordinal2; k <= count3; k++)
								{
									if (Operators.CompareString(geometry.River.GetRiverName(k), riverName, TextCompare: false) == 0)
									{
										riverXSIntersections.AddRange(geometry.RiverXSIntersections(k));
										polylines.Add((Polyline)river.Feature(k));
										intSet.Add(k);
									}
								}
							}
							pointMs3.Clear();
							pointMs4.Clear();
							int count4 = riverXSIntersections.Count - 1;
							for (int l = 1; l <= count4; l++)
							{
								PolylineIntersection polylineIntersection = riverXSIntersections[l - 1];
								PolylineIntersection polylineIntersection2 = riverXSIntersections[l];
								PointOnPolyline aPointOnPolyline = polylineIntersection.APointOnPolyline;
								PointOnPolyline aPointOnPolyline2 = polylineIntersection2.APointOnPolyline;
								PointOnPolyline bPointOnPolyline = polylineIntersection.BPointOnPolyline;
								PointOnPolyline bPointOnPolyline2 = polylineIntersection2.BPointOnPolyline;
								polylines[ordinal].NearestPointOnPolyline(aPointOnPolyline2.PointM, ref actualDistance);
								if (actualDistance >= 0.0 && actualDistance < 0.01)
								{
									polylines[ordinal].PointSubsection(aPointOnPolyline, aPointOnPolyline2, pointMs);
								}
								else
								{
									pointMs.Clear();
									pointMs.Add(aPointOnPolyline.PointM);
									pointMs.Add(aPointOnPolyline2.PointM);
									ordinal++;
								}
								int bPolylineIndex = polylineIntersection.BPolylineIndex;
								int bPolylineIndex2 = polylineIntersection2.BPolylineIndex;
								if (bPolylineIndex == bPolylineIndex2)
								{
									continue;
								}
								_ = dataTable.Rows[bPolylineIndex];
								_ = dataTable.Rows[bPolylineIndex2];
								Polyline usxsPolyline = xS.Feature(bPolylineIndex) as Polyline;
								Polyline dsxsPolyline = xS.Feature(bPolylineIndex2) as Polyline;
								BankProperties bankProperties2;
								if (overrideBankPropertiesList != null)
								{
									bankProperties2 = overrideBankPropertiesList[bPolylineIndex];
									if (bankProperties2 == null)
									{
										continue;
									}
								}
								else
								{
									bankProperties2 = xS.GetBankProperties(bPolylineIndex);
								}
								BankProperties bankProperties3;
								if (overrideBankPropertiesList != null)
								{
									bankProperties3 = overrideBankPropertiesList[bPolylineIndex2];
									if (bankProperties3 == null)
									{
										continue;
									}
								}
								else
								{
									bankProperties3 = xS.GetBankProperties(bPolylineIndex2);
								}
								if (float.IsNaN(bankProperties2.LeftBankFraction) || float.IsNaN(bankProperties3.LeftBankFraction))
								{
									if (pointMs3.Count > 0)
									{
										AddFeature(new Polyline(pointMs3));
										pointMs3.Clear();
									}
								}
								else
								{
									offsetStreamLinesFactory.ComputeOffset(pointMs, bPointOnPolyline, bPointOnPolyline2, usxsPolyline, bankProperties2.LeftBankPoint, dsxsPolyline, bankProperties3.LeftBankPoint, pointMs2);
									pointMs3.AddRange(pointMs2, filterToleranceXY);
								}
								if (float.IsNaN(bankProperties2.RightBankFraction) || float.IsNaN(bankProperties3.RightBankFraction))
								{
									if (pointMs4.Count > 0)
									{
										AddFeature(new Polyline(pointMs4));
										pointMs4.Clear();
									}
								}
								else
								{
									offsetStreamLinesFactory.ComputeOffset(pointMs, bPointOnPolyline, bPointOnPolyline2, usxsPolyline, bankProperties2.RightBankPoint, dsxsPolyline, bankProperties3.RightBankPoint, pointMs2);
									pointMs4.AddRange(pointMs2, filterToleranceXY);
								}
							}
							if (pointMs3.Count > 0)
							{
								AddFeature(new Polyline(pointMs3));
							}
							if (pointMs4.Count > 0)
							{
								AddFeature(new Polyline(pointMs4));
							}
							intSet.Add(j);
						}
						stopwatch.Stop();
						if (overrideBankPropertiesList == null)
						{
							SharedData.ReportMessage("Bank lines generated in " + Conversions.ToString(stopwatch.ElapsedMilliseconds) + " ms", replacePreviousIfPossible: true);
							SaveFeatureTable();
						}
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				SharedData.ReportMessage("Error generating Bank lines");
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}
}
