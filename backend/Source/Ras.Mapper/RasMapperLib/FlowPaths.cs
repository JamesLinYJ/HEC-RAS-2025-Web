using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class FlowPaths : PolylineFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/River Flow Paths";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Flow Paths";
string IGeometryLayer.HDFGroupName=> "Geometry/River Flow Paths";

	public FlowPaths(RASGeometry geometry)
		: base("Flow Paths", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "Flow Path";
		ForwardEditRequestsToParent = true;
		CanRename = false;
		Geometry = geometry;
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/River Flow Paths/Flow Path Lines");
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
			hw.Delete("Geometry/River Flow Paths");
			return;
		}
		hw.CreateGroup("Geometry/River Flow Paths");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/River Flow Paths/Flow Path Lines");
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

	public static FlowPaths Stream(BinaryReader br, RASGeometry geom)
	{
		FlowPaths flowPaths = new FlowPaths(geom);
		flowPaths.InitializeEmptyFeatureTable();
		flowPaths.SuppressTableReload(boolValue: true);
		flowPaths.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline feature = RasMapperLib.Polyline.Stream(br);
				flowPaths.AddFeature(feature);
			}
			return flowPaths;
		}
	}

	private void ComputeFlowPaths(object sender, EventArgs e)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
		}
		MessageBox.Show("Not implemented yet");
		SharedData.RASMapperReplot();
	}

	public bool ComputeFlowPathLines()
	{
		checked
		{
			bool result;
			try
			{
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
						SharedData.ReportMessage("Computing Bank Lines");
						Geometry.Errors.ClearErrors();
						string proc = "Checking for Bank Stations";
						int count = xS.FeatureCount() - 1;
						for (int i = 0; i <= count; i++)
						{
							BankProperties bankProperties = xS.GetBankProperties(i);
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
						int count2 = dataTable2.Rows.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							Console.WriteLine("PROGRESS=" + Conversions.ToString((double)(j + 1) / (double)river.FeatureCount()));
							Polyline polyline = (Polyline)river.Feature(j);
							List<PolylineIntersection> riverXSIntersections = geometry.RiverXSIntersections(j);
							pointMs3.Clear();
							pointMs4.Clear();
							int count3 = riverXSIntersections.Count - 1;
							for (int k = 1; k <= count3; k++)
							{
								PolylineIntersection polylineIntersection = riverXSIntersections[k - 1];
								PolylineIntersection polylineIntersection2 = riverXSIntersections[k];
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
								Polyline usxsPolyline = xS.Feature(bPolylineIndex) as Polyline;
								Polyline dsxsPolyline = xS.Feature(bPolylineIndex2) as Polyline;
								BankProperties bankProperties2 = xS.GetBankProperties(bPolylineIndex);
								BankProperties bankProperties3 = xS.GetBankProperties(bPolylineIndex2);
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
						}
						stopwatch.Stop();
						SharedData.ReportMessage("Bank lines generated in " + Conversions.ToString(stopwatch.ElapsedMilliseconds) + " ms", replacePreviousIfPossible: true);
						SaveFeatureTable();
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
