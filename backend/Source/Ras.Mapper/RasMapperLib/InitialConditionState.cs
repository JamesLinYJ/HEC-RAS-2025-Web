using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mesh;
using RasMapperLib.Render;

namespace RasMapperLib;

public class InitialConditionState
{
	public RASGeometry Geometry;

	public float[] CrossSectionWaterSurfaces;

	public float[] CrossSectionFlows;

	public float[] StorageAreaWaterSurfaces;

	public float[][] MeshCellWaterSurfaces;

	public float[][] MeshFaceVelocities;

	private InitialConditionState(RASGeometry geo, float[] xsws, float[] xsf, float[] saws, float[][] cellws, float[][] facevel)
	{
		Geometry = geo;
		CrossSectionWaterSurfaces = xsws;
		CrossSectionFlows = xsf;
		StorageAreaWaterSurfaces = saws;
		MeshCellWaterSurfaces = cellws;
		MeshFaceVelocities = facevel;
	}

	public static InitialConditionState ComputeFromExistingResult(RASResults result, int pfIdx, RASGeometry forGeometry)
	{
		checked
		{
			InitialConditionState result2;
			try
			{
				result.GetPostProcessor().EnsurePostProcess();
				WaterSurfaceRenderer waterSurfaceRenderer = new WaterSurfaceRenderer(result);
				VelocityRenderer velocityRenderer = new VelocityRenderer(result);
				float[][] floatValues = new float[forGeometry.D2FlowArea.FeatureCount() - 1 + 1][];
				float[][] floatValuesArray = new float[forGeometry.D2FlowArea.FeatureCount() - 1 + 1][];
				if (forGeometry.D2FlowArea != null && forGeometry.D2FlowArea.FeatureCount() > 0)
				{
					int count = forGeometry.D2FlowArea.FeatureCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						int fid = i;
						MeshFV2D meshFV2D = forGeometry.D2FlowArea.Mesh(fid);
						PointMs pointMs = new PointMs();
						int count2 = meshFV2D.CellCount - 1;
						for (int j = 0; j <= count2; j++)
						{
							pointMs.Add(meshFV2D.Cell(j).Point.ToPointM());
						}
						RASGeometryMapPoints mps = result.Geometry.MapPixels(pointMs);
						PointMs pointMs2 = new PointMs();
						int count3 = meshFV2D.FaceCount - 1;
						for (int k = 0; k <= count3; k++)
						{
							Point2Double point = meshFV2D.FacePoints[meshFV2D.Faces[k].fpA].Point;
							Point2Double point2 = meshFV2D.FacePoints[meshFV2D.Faces[k].fpB].Point;
							PointM pointM2 = new PointM((point.X + point2.X) / 2.0, (point.Y + point2.Y) / 2.0);
							pointMs2.Add(pointM2);
						}
						RASGeometryMapPoints mps2 = result.Geometry.MapPixels(pointMs2);
						float[] waterSurfaces = null;
						float[] outputTimes = null;
						waterSurfaceRenderer.Compute(pfIdx, mps, ref waterSurfaces, null, ref outputTimes);
						float[] terrain = forGeometry.Terrain.ComputePointElevations(pointMs2);
						float[] outVelocityX = null;
						float[] outVelocityY = null;
						outputTimes = null;
						float[] outDepths = null;
						velocityRenderer.Compute(pfIdx, mps2, terrain, ref outVelocityX, ref outVelocityY, ref outputTimes, ref outDepths);
						meshFV2D.EnsureFaceNormals();
						float[] floatValues2 = new float[outVelocityX.Count() - 1 + 1];
						int count4 = floatValues2.Length - 1;
						for (int l = 0; l <= count4; l++)
						{
							Vector2D vector2D = meshFV2D.FaceNormal(l);
							if (outVelocityX[l] != -9999f && outVelocityY[l] != -9999f && vector2D.X != -9999f && vector2D.Y != -9999f)
							{
								floatValues2[l] = outVelocityX[l] * vector2D.X + outVelocityY[l] * vector2D.Y;
							}
						}
						floatValues[i] = waterSurfaces;
						floatValuesArray[i] = floatValues2;
					}
				}
				float[] xsf = null;
				float[] waterSurfaces2 = null;
				if (forGeometry.River != null && forGeometry.XS != null && forGeometry.XS.FeatureCount() > 0)
				{
					PointMs pointMs3 = new PointMs();
					int featureCountXsTerm = forGeometry.XS.FeatureCount() - 1;
					int ordinal = 0;
					if (ordinal <= featureCountXsTerm)
					{
						xsf = new float[forGeometry.XS.FeatureCount() - 1 + 1];
						List<PolylineIntersection> spatialIntersect = forGeometry.River.SpatialIntersect(forGeometry.XS.Polyline(ordinal));
						int count5 = spatialIntersect.Count - 1;
						for (int m = 0; m <= count5; m++)
						{
							pointMs3.Add(spatialIntersect[m].Point);
						}
						throw new NotImplementedException();
					}
					RASGeometryMapPoints mps3 = result.Geometry.MapPixels(pointMs3);
					float[] outDepths = null;
					waterSurfaceRenderer.Compute(pfIdx, mps3, ref waterSurfaces2, null, ref outDepths);
				}
				List<float> floatList = new List<float>();
				if (forGeometry.StorageArea != null && forGeometry.StorageArea.FeatureCount() > 0)
				{
					int[] coefficients = new int[3] { 10000, 100000, 1000000 };
					int count6 = forGeometry.StorageArea.FeatureCount() - 1;
					int r = default;
					int c = default;
					for (int n = 0; n <= count6; n++)
					{
						Extent extent = forGeometry.StorageArea.Polygon(n).GetExtent();
						bool condition = false;
						int count7 = coefficients.Length - 1;
						for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
						{
							RasterM rasterM = RasterM.ComputeRasterM(coefficients[intervalIndex], extent);
							PointMs pointMs4 = new PointMs();
							int count8 = rasterM.CellCount - 1;
							for (int ordinal2 = 0; ordinal2 <= count8; ordinal2++)
							{
								rasterM.Cell2DIndex(ordinal2, ref r, ref c);
								PointM pointM = rasterM.CellCenter(r, c);
								if (forGeometry.StorageArea.Polygon(n).Contains(pointM))
								{
									pointMs4.Add(pointM);
								}
							}
							RASGeometryMapPoints mps4 = result.Geometry.MapPixels(pointMs4);
							float[] waterSurfaces3 = null;
							float[] outDepths = null;
							waterSurfaceRenderer.Compute(pfIdx, mps4, ref waterSurfaces3, null, ref outDepths);
							float waterSurfaces3Sum = 0f;
							int ordinal3 = 0;
							int count9 = waterSurfaces3.Length - 1;
							for (int intervalIndex2 = 0; intervalIndex2 <= count9; intervalIndex2++)
							{
								if (waterSurfaces3[intervalIndex2] != -9999f)
								{
									waterSurfaces3Sum += waterSurfaces3[intervalIndex2];
									ordinal3++;
								}
							}
							if (ordinal3 != 0)
							{
								float waterSurfaces3SumTerm = waterSurfaces3Sum / (float)ordinal3;
								floatList.Add(waterSurfaces3SumTerm);
								condition = true;
								break;
							}
						}
						if (!condition)
						{
							floatList.Add(-9999f);
						}
					}
				}
				result2 = new InitialConditionState(forGeometry, waterSurfaces2, xsf, floatList.ToArray(), floatValues, floatValuesArray);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result2 = null;
				ProjectData.ClearProjectError();
			}
			return result2;
		}
	}

	public static InitialConditionState LoadFromComputedResult(RASResults result)
	{
		throw new NotImplementedException();
	}

	public static InitialConditionState LoadFromICPoints(IList<PointM> pts, RASGeometry forGeometry)
	{
		throw new NotImplementedException();
	}

	public void Save(H5Writer h5w, string groupName = "Event Conditions/Unsteady/Initial Conditions/")
	{
		checked
		{
			if (MeshCellWaterSurfaces != null && MeshCellWaterSurfaces.Length > 0)
			{
				int count = MeshCellWaterSurfaces.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					string groupNameGetFeatureNameTerm = groupName + "2D Flow Areas/" + Geometry.D2FlowArea.GetFeatureName(i).ToString() + "/";
					h5w.CreateGroup(groupNameGetFeatureNameTerm);
					string datasetName = groupNameGetFeatureNameTerm + "Face Velocity";
					h5w.Create1dExtendibleDataset<float>(datasetName, MeshFaceVelocities[i].Length);
					h5w.AddElements(datasetName, MeshFaceVelocities[i]);
					string datasetName2 = groupNameGetFeatureNameTerm + "Water Surface";
					h5w.Create1dExtendibleDataset<float>(datasetName2, MeshCellWaterSurfaces[i].Length);
					h5w.AddElements(datasetName2, MeshCellWaterSurfaces[i]);
				}
			}
			if (CrossSectionFlows != null && CrossSectionFlows.Length > 0)
			{
				string groupNameTerm = groupName + "Cross Sections/";
				h5w.CreateGroup(groupNameTerm);
				string datasetName3 = groupNameTerm + "Flow";
				h5w.Create2dExtendibleDataset<float>(datasetName3, 1, CrossSectionFlows.Length);
				h5w.AddRow(datasetName3, CrossSectionFlows);
				string datasetName4 = groupNameTerm + "Water Surface";
				h5w.Create2dExtendibleDataset<float>(datasetName4, 1, CrossSectionWaterSurfaces.Length);
				h5w.AddRow(datasetName4, CrossSectionWaterSurfaces);
			}
			if (StorageAreaWaterSurfaces != null && StorageAreaWaterSurfaces.Length > 0)
			{
				string groupNameTerm2 = groupName + "Storage Areas/";
				h5w.CreateGroup(groupNameTerm2);
				string datasetName5 = groupNameTerm2 + "Water Surface";
				h5w.Create2dExtendibleDataset<float>(datasetName5, 1, StorageAreaWaterSurfaces.Count());
				int count2 = StorageAreaWaterSurfaces.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					h5w.AddColumn(datasetName5, new float[1] { StorageAreaWaterSurfaces[j] });
				}
			}
		}
	}
}
