using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Hydraulics;
using Ras.Layers.Classic;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Ras.Layers.Terrains;

public static class ChannelMod
{
	private struct SectionResamplerArgs
	{
		public ChannelSectionResampler.InputCrossSection UpstreamCrossSection;

		public ChannelSectionResampler.InputCrossSection DownstreamCrossSection;

		public IReadOnlyList<Polyline> Riverlines;

		public IReadOnlyList<double> UpstreamM;

		public IReadOnlyList<double> DownstreamM;

		public IReadOnlyList<double> RiverM;

		public TinResampler Mask;

		public double SegmentLength;

		public RasterModType ModificationType;
	}

	private struct CrossSectionGroup
	{
		public IReadOnlyList<CrossSection> CrossSections;
	}

	private struct CrossSection
	{
		public IReadOnlyList<int> Arcs;

		public int ProfileIndex;
	}

	private struct ArcGroupIteration
	{
		public Arc Arc;

		public int ArcIdx;

		public bool ShouldReverseNodes;

		public bool IsFinal;
	}

	private struct ArcAndProfile
	{
		public int ArcIdx;

		public int ProfileIdx;
	}

	public static readonly Topology.TopologyMessageClass CrossSectionGenerateFailure = new Topology.TopologyMessageClass("ChannelMod_CrossSectionGenerateFailure", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "Failed to generate topology cross sections");

	public static readonly Topology.TopologyMessageClass NodelessArcFailure = new Topology.TopologyMessageClass("ChannelMod_CrossSectionNodelessArc", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "Arc {ArcIndex} is missing a node");

	public static readonly Topology.TopologyMessageClass RiverLineFailure = new Topology.TopologyMessageClass("ChannelMod_CrossSectionGenerateFailure", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "Failed to generate topology river lines between Arcs {ArcIndex} and {ArcIndex}");

	public static readonly Topology.TopologyMessageClass CornerFailure = new Topology.TopologyMessageClass("ChannelMod_CrossSectionNodelessArc", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "Node {NodeIndex} exists on topology without 4 corners");

	public static readonly Topology.TopologyMessageClass JunctionNodeFailure = new Topology.TopologyMessageClass("ChannelMod_CrossSectionJunction", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "Node {NodeIndex} has more than 2 cross section connections");

	public static readonly Topology.TopologyMessageClass TinSegmentLengthShort = new Topology.TopologyMessageClass("ChannelMod_CrossSectionNodelessArc", MessageSeverity.Error, Topology.RegionCompletionSeverity.Fatal, "TIN segment length must be > 0");

	private static double MAX_CROSS_SECTION_DISTANCE = 10.0;

	public static string MaskColumnName = "Mask";

	public static string ArcSourceName = "Source";

	public static Message GetRiverLineFailure(int arcId1, int arcId2)
	{
		return RiverLineFailure.Create(arcId1, arcId2);
	}

	public static IResample<float> CreateChannelResampler(Topology topology, IReadOnlyList<ChannelProfile> profiles, double tinMaxSegmentLength, RasterModType modificationType = RasterModType.SetValue, OperationResult result = null)
	{
		if (result == null)
		{
			result = new OperationResult(success: true);
		}
		if (tinMaxSegmentLength <= 0.0)
		{
			result.AddFailure(TinSegmentLengthShort.Create());
			return null;
		}
		for (int i = 0; i < topology.Arcs.Count; i++)
		{
			Arc arc = topology.Arcs[i];
			if (arc.StartNode == -1 || arc.EndNode == -1)
			{
				result.AddFailure(NodelessArcFailure.Create(i));
			}
		}
		if (!result)
		{
			return null;
		}
		IReadOnlyList<CrossSectionGroup> readOnlyList = null;
		IReadOnlyList<int> outFailingCorners = null;
		IReadOnlyList<int> outJunctionNodes = null;
		try
		{
			readOnlyList = GetCrossSectionGroups(topology, profiles, out outFailingCorners, out outJunctionNodes);
		}
		catch
		{
			result.AddFailure(CrossSectionGenerateFailure.Create());
		}
		if (outFailingCorners.Count != 0)
		{
			foreach (int outFailingCorner in outFailingCorners)
			{
				result.AddFailure(CornerFailure.Create(outFailingCorner));
			}
		}
		if (outJunctionNodes.Count != 0)
		{
			foreach (int outJunctionNode in outJunctionNodes)
			{
				result.AddFailure(JunctionNodeFailure.Create(outJunctionNode));
			}
		}
		if (!result)
		{
			return null;
		}
		IReadOnlyList<IReadOnlyList<int>> maskPerimeters = GetMaskPerimeters(topology);
		TinResampler mask = null;
		if (maskPerimeters != null)
		{
			DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
			delaunayTinFactory.KeepDeletedTriangles = false;
			foreach (IReadOnlyList<int> maskPerimeter in maskPerimeters)
			{
				Polygon polygon = ArcGroupToPolygon(topology, maskPerimeter, 1.0);
				if (polygon.Area < 0.0)
				{
					polygon.Reverse();
				}
				delaunayTinFactory.AddPerimeter(polygon);
			}
			mask = new TinResampler(delaunayTinFactory.ComputeTIN(), renderZ: true);
		}
		List<SectionResamplerArgs> resamplerArgs = new List<SectionResamplerArgs>();
		foreach (CrossSectionGroup readOnlyList2 in readOnlyList)
		{
			for (int j = 0; j < readOnlyList2.CrossSections.Count - 1; j++)
			{
				IReadOnlyList<int> arcs = readOnlyList2.CrossSections[j].Arcs;
				IReadOnlyList<int> arcs2 = readOnlyList2.CrossSections[j + 1].Arcs;
				GetRiverArcPolylines(topology, arcs, arcs2, out var outMValuesA, out var outMValuesB, out var outMValuesRiver, out var outRiverPolylines);
				if (outRiverPolylines.Count < 2)
				{
					_ = Point.Origin;
					int arcId = ((arcs.Count != 0) ? arcs[arcs.Count / 2] : (-1));
					int arcId2 = ((arcs2.Count != 0) ? arcs2[arcs2.Count / 2] : (-1));
					result.AddFailure(GetRiverLineFailure(arcId, arcId2));
					return null;
				}
				ChannelSectionResampler.InputCrossSection upstreamCrossSection = new ChannelSectionResampler.InputCrossSection
				{
					Lines = ArcGroupToPolylines(topology, arcs),
					Profile = profiles[readOnlyList2.CrossSections[j].ProfileIndex].Profile
				};
				ChannelSectionResampler.InputCrossSection downstreamCrossSection = new ChannelSectionResampler.InputCrossSection
				{
					Lines = ArcGroupToPolylines(topology, arcs2),
					Profile = profiles[readOnlyList2.CrossSections[j + 1].ProfileIndex].Profile
				};
				resamplerArgs.Add(new SectionResamplerArgs
				{
					UpstreamCrossSection = upstreamCrossSection,
					DownstreamCrossSection = downstreamCrossSection,
					Riverlines = outRiverPolylines,
					UpstreamM = outMValuesA,
					DownstreamM = outMValuesB,
					RiverM = outMValuesRiver,
					Mask = mask,
					SegmentLength = tinMaxSegmentLength,
					ModificationType = modificationType
				});
			}
		}
		object resamplerLock = new object();
		List<IResample<float>> resamplers = new List<IResample<float>>();
		Parallel.For(0, resamplerArgs.Count, (int index) =>
		{
			SectionResamplerArgs sectionResamplerArgs = resamplerArgs[index];
			ChannelSectionResampler channelSectionResampler = new ChannelSectionResampler(sectionResamplerArgs.UpstreamCrossSection, sectionResamplerArgs.DownstreamCrossSection, sectionResamplerArgs.Riverlines, sectionResamplerArgs.UpstreamM, sectionResamplerArgs.DownstreamM, sectionResamplerArgs.RiverM, sectionResamplerArgs.Mask, sectionResamplerArgs.SegmentLength, sectionResamplerArgs.ModificationType);
			lock (resamplerLock)
			{
				resamplers.Add(channelSectionResampler);
			}
		});
		return new UnionResampler<float>(resamplers);
	}

	private static bool IsArcAlignedToPolyline(Topology topo, int arcIdx, Polyline line)
	{
		Arc arc = topo.Arcs[arcIdx];
		if (line.DistanceToPoint(topo.Nodes[arc.StartNode].Point) > 0.01)
		{
			return false;
		}
		if (line.DistanceToPoint(topo.Nodes[arc.EndNode].Point) > 0.01)
		{
			return false;
		}
		if (arc.InternalPointCount > 0)
		{
			foreach (Point internalPoint in arc.InternalPoints)
			{
				if (line.DistanceToPoint(internalPoint) > 0.01)
				{
					return false;
				}
			}
		}
		return true;
	}

	public static void EnsureTopologyColumns(Topology topo)
	{
		topo.ControlPointMetadata.TryEnsureColumn(MaskColumnName, typeof(bool), DuplicateColumnBehavior.Skip, out var idx);
		topo.ArcMetadata.TryEnsureColumn(ArcSourceName, typeof(string), DuplicateColumnBehavior.Skip, out idx);
	}

	private static bool IsArcAlignedToAnyPolylines(Topology topo, int arcIdx, IEnumerable<Polyline> lines)
	{
		_ = topo.Arcs[arcIdx];
		foreach (Polyline line in lines)
		{
			if (IsArcAlignedToPolyline(topo, arcIdx, line))
			{
				return true;
			}
		}
		return false;
	}

	public static void AssignArcSources(Topology topo, ClassicGeometry geo)
	{
		for (int i = 0; i < topo.ArcCount; i++)
		{
			if (IsArcAlignedToAnyPolylines(topo, i, geo.RiverEdgeLineLayer.Features.Select((Edgeline f) => f.Polyline)))
			{
				topo.ArcMetadata[i].SetValue(ArcSourceName, "Edge");
			}
			else if (IsArcAlignedToAnyPolylines(topo, i, geo.BanklineLayer.Features.Select((Bankline f) => f.Polyline)))
			{
				topo.ArcMetadata[i].SetValue(ArcSourceName, "Bank");
			}
			else if (IsArcAlignedToAnyPolylines(topo, i, geo.RiverLayer.Features.Select((River f) => f.Polyline)))
			{
				topo.ArcMetadata[i].SetValue(ArcSourceName, "River");
			}
			else if (IsArcAlignedToAnyPolylines(topo, i, geo.CrossSectionLayer.Features.Select((Ras.Hydraulics.CrossSection f) => f.Polyline)))
			{
				topo.ArcMetadata[i].SetValue(ArcSourceName, "Cross Section");
			}
		}
	}

	private static IReadOnlyList<int> GetNodesOfArcGroup(Topology topo, IReadOnlyList<int> arcGroup)
	{
		HashSet<int> intSet = new HashSet<int>();
		foreach (int arcGroup2 in arcGroup)
		{
			Arc arc = topo.Arcs[arcGroup2];
			if (arc.StartNode != -1)
			{
				intSet.Add(arc.StartNode);
			}
			if (arc.EndNode != -1)
			{
				intSet.Add(arc.EndNode);
			}
		}
		return intSet.ToArray();
	}

	private static IReadOnlyList<CrossSectionGroup> GetCrossSectionGroups(Topology topo, IReadOnlyList<ChannelProfile> profiles, out IReadOnlyList<int> outFailingCorners, out IReadOnlyList<int> outJunctionNodes)
	{
		List<int> outFailingCornersList = new List<int>();
		List<int> outJunctionNodesList = new List<int>();
		outFailingCorners = outFailingCornersList;
		outJunctionNodes = outJunctionNodesList;
		List<CrossSectionGroup> crossSectionGroups = new List<CrossSectionGroup>();
		foreach (IReadOnlyList<int> connectedArcGroup in GetConnectedArcGroups(topo))
		{
			CrossSectionGroup crossSectionGroup = GetCrossSectionGroup(topo, connectedArcGroup, profiles, outJunctionNodesList);
			HashSet<int> intSet = new HashSet<int>();
			foreach (CrossSection crossSection in crossSectionGroup.CrossSections)
			{
				foreach (int arc in crossSection.Arcs)
				{
					intSet.Add(arc);
				}
			}
			List<int> intList = new List<int>();
			foreach (int connectedArcGroup2 in connectedArcGroup)
			{
				if (!intSet.Contains(connectedArcGroup2))
				{
					intList.Add(connectedArcGroup2);
				}
			}
			List<int> intListList = new List<int>();
			foreach (int nodesOfArcGroup in GetNodesOfArcGroup(topo, intList))
			{
				if (topo.Nodes[nodesOfArcGroup].Arcs.Count == 2)
				{
					intListList.Add(nodesOfArcGroup);
				}
			}
			if (intListList.Count != 4)
			{
				outFailingCornersList.AddRange(intListList);
			}
			crossSectionGroups.Add(crossSectionGroup);
		}
		if (outFailingCornersList.Count != 0)
		{
			return null;
		}
		if (outJunctionNodesList.Count != 0)
		{
			return null;
		}
		return crossSectionGroups;
	}

	private static void MakeCrossSectionArcGroupRecursive(Topology topo, int arcIdx, HashSet<int> crossSectionArcIndices, List<int> outArcGroup)
	{
		if (!crossSectionArcIndices.Contains(arcIdx))
		{
			return;
		}
		outArcGroup.Add(arcIdx);
		crossSectionArcIndices.Remove(arcIdx);
		Arc arc = topo.Arcs[arcIdx];
		Node node = topo.Nodes[arc.StartNode];
		Node node2 = topo.Nodes[arc.EndNode];
		foreach (NodeArcConnection arc2 in node.Arcs)
		{
			MakeCrossSectionArcGroupRecursive(topo, arc2.ArcID, crossSectionArcIndices, outArcGroup);
		}
		foreach (NodeArcConnection arc3 in node2.Arcs)
		{
			MakeCrossSectionArcGroupRecursive(topo, arc3.ArcID, crossSectionArcIndices, outArcGroup);
		}
	}

	private static void MakeRiverArcGroupRecursive(Topology topo, int arcIdx, HashSet<int> passedArcs, HashSet<int> crossSectionArcIndices, List<int> outArcGroup)
	{
		if (passedArcs.Contains(arcIdx) || crossSectionArcIndices.Contains(arcIdx))
		{
			return;
		}
		outArcGroup.Add(arcIdx);
		passedArcs.Add(arcIdx);
		Arc arc = topo.Arcs[arcIdx];
		Node node = topo.Nodes[arc.StartNode];
		Node node2 = topo.Nodes[arc.EndNode];
		foreach (NodeArcConnection arc2 in node.Arcs)
		{
			MakeRiverArcGroupRecursive(topo, arc2.ArcID, passedArcs, crossSectionArcIndices, outArcGroup);
		}
		foreach (NodeArcConnection arc3 in node2.Arcs)
		{
			MakeRiverArcGroupRecursive(topo, arc3.ArcID, passedArcs, crossSectionArcIndices, outArcGroup);
		}
	}

	private static bool IsValidCrossSectionStart(Topology topo, int arcIdx, HashSet<int> crossSectionArcIndices)
	{
		if (!crossSectionArcIndices.Contains(arcIdx))
		{
			return false;
		}
		Arc arc = topo.Arcs[arcIdx];
		int ordinal = 0;
		Node node = topo.Nodes[arc.StartNode];
		Node node2 = topo.Nodes[arc.EndNode];
		foreach (NodeArcConnection arc2 in node.Arcs)
		{
			if (arc2.ArcID != arcIdx && crossSectionArcIndices.Contains(arc2.ArcID))
			{
				ordinal++;
			}
		}
		foreach (NodeArcConnection arc3 in node2.Arcs)
		{
			if (arc3.ArcID != arcIdx && crossSectionArcIndices.Contains(arc3.ArcID))
			{
				ordinal++;
			}
		}
		return ordinal <= 1;
	}

	private static HashSet<int> GetNodesOfCrossSections(Topology topo, IReadOnlyList<ArcAndProfile> crossArcs)
	{
		HashSet<int> nodesOfCrossSections = new HashSet<int>();
		foreach (ArcAndProfile crossArc in crossArcs)
		{
			Arc arc = topo.Arcs[crossArc.ArcIdx];
			nodesOfCrossSections.Add(arc.StartNode);
			nodesOfCrossSections.Add(arc.EndNode);
		}
		return nodesOfCrossSections;
	}

	private static CrossSectionGroup GetCrossSectionGroup(Topology topo, IReadOnlyList<int> arcGroup, IReadOnlyList<ChannelProfile> profiles, List<int> junctionNodes)
	{
		List<CrossSection> crossSections = new List<CrossSection>();
		IReadOnlyList<ArcAndProfile> crossSectionArcs = GetCrossSectionArcs(topo, arcGroup, profiles);
		HashSet<int> intSet = new HashSet<int>();
		foreach (ArcAndProfile crossSectionArc in crossSectionArcs)
		{
			intSet.Add(crossSectionArc.ArcIdx);
		}
		foreach (ArcAndProfile crossSectionArc2 in crossSectionArcs)
		{
			if (IsValidCrossSectionStart(topo, crossSectionArc2.ArcIdx, intSet))
			{
				List<int> arcs = new List<int>();
				MakeCrossSectionArcGroupRecursive(topo, crossSectionArc2.ArcIdx, intSet, arcs);
				Point other = profiles[crossSectionArc2.ProfileIdx].Line.Points[0];
				int startNode = topo.Arcs[arcs[0]].StartNode;
				int endNode = topo.Arcs[arcs[0]].EndNode;
				double distanceSquaredToPointTerm = Math.Min(topo.Nodes[startNode].Point.DistanceSquaredTo(other), topo.Nodes[endNode].Point.DistanceSquaredTo(other));
				int startNode2 = topo.Arcs[arcs[arcs.Count - 1]].StartNode;
				int endNode2 = topo.Arcs[arcs[arcs.Count - 1]].EndNode;
				double maxValue = double.MaxValue;
				maxValue = Math.Min(topo.Nodes[startNode2].Point.DistanceSquaredTo(other), topo.Nodes[endNode2].Point.DistanceSquaredTo(other));
				if (distanceSquaredToPointTerm > maxValue)
				{
					arcs.Reverse();
				}
				crossSections.Add(new CrossSection
				{
					ProfileIndex = crossSectionArc2.ProfileIdx,
					Arcs = arcs
				});
			}
		}
		intSet.Clear();
		foreach (ArcAndProfile crossSectionArc3 in crossSectionArcs)
		{
			intSet.Add(crossSectionArc3.ArcIdx);
		}
		HashSet<int> nodesOfCrossSections = GetNodesOfCrossSections(topo, crossSectionArcs);
		foreach (int nodesOfCrossSection in nodesOfCrossSections)
		{
			Node node = topo.Nodes[nodesOfCrossSection];
			int ordinal = 0;
			foreach (NodeArcConnection arc3 in node.Arcs)
			{
				if (intSet.Contains(arc3.ArcID))
				{
					continue;
				}
				Arc arc = topo.Arcs[arc3.ArcID];
				if (arc.StartNode == nodesOfCrossSection)
				{
					if (nodesOfCrossSections.Contains(arc.EndNode))
					{
						ordinal++;
					}
				}
				else if (arc.EndNode == nodesOfCrossSection && nodesOfCrossSections.Contains(arc.StartNode))
				{
					ordinal++;
				}
			}
			if (ordinal > 2)
			{
				junctionNodes.Add(nodesOfCrossSection);
			}
		}
		int unity = -1;
		foreach (CrossSection crossSection in crossSections)
		{
			Arc arc2 = topo.Arcs[crossSection.Arcs[0]];
			if (topo.Nodes[arc2.StartNode].Arcs.Count < 3)
			{
				int ordinal2 = 0;
				foreach (NodeArcConnection arc4 in topo.Nodes[arc2.StartNode].Arcs)
				{
					if (!intSet.Contains(arc4.ArcID))
					{
						ordinal2++;
					}
				}
				if (ordinal2 == 1)
				{
					unity = arc2.StartNode;
					break;
				}
			}
			if (topo.Nodes[arc2.EndNode].Arcs.Count >= 3)
			{
				continue;
			}
			int ordinal3 = 0;
			foreach (NodeArcConnection arc5 in topo.Nodes[arc2.EndNode].Arcs)
			{
				if (!intSet.Contains(arc5.ArcID))
				{
					ordinal3++;
				}
			}
			if (ordinal3 == 1)
			{
				unity = arc2.EndNode;
				break;
			}
		}
		if (unity == -1)
		{
			return new CrossSectionGroup
			{
				CrossSections = Array.Empty<CrossSection>()
			};
		}
		int arcIdx = -1;
		foreach (NodeArcConnection arc6 in topo.Nodes[unity].Arcs)
		{
			if (!intSet.Contains(arc6.ArcID))
			{
				arcIdx = arc6.ArcID;
			}
		}
		List<int> intList = new List<int>();
		HashSet<int> passedArcs = new HashSet<int>();
		MakeRiverArcGroupRecursive(topo, arcIdx, passedArcs, intSet, intList);
		IReadOnlyList<int> orderNodes = ArcGroupToNodeList(topo, intList);
		crossSections.Sort((CrossSection a, CrossSection b) =>
		{
			int indexOf = orderNodes.IndexOf(topo.Arcs[a.Arcs[0]].StartNode);
			if (indexOf == -1)
			{
				indexOf = orderNodes.IndexOf(topo.Arcs[a.Arcs[0]].EndNode);
			}
			int indexOf2 = orderNodes.IndexOf(topo.Arcs[b.Arcs[0]].StartNode);
			if (indexOf2 == -1)
			{
				indexOf2 = orderNodes.IndexOf(topo.Arcs[b.Arcs[0]].EndNode);
			}
			return indexOf.CompareTo(indexOf2);
		});
		return new CrossSectionGroup
		{
			CrossSections = crossSections
		};
	}

	private static IReadOnlyList<ArcAndProfile> GetCrossSectionArcs(Topology topo, IReadOnlyList<int> arcGroup, IReadOnlyList<ChannelProfile> profiles)
	{
		List<ArcAndProfile> arcAndProfiles = new List<ArcAndProfile>();
		foreach (int arcGroup2 in arcGroup)
		{
			Point[] arcPoints = GetArcPoints(topo, arcGroup2);
			int profileIdx = -1;
			for (int i = 0; i < profiles.Count; i++)
			{
				ChannelProfile channelProfile = profiles[i];
				bool condition = true;
				Point[] arcPointsArray = arcPoints;
				foreach (Point pt in arcPointsArray)
				{
					if (channelProfile.Line.DistanceToPoint(pt) > MAX_CROSS_SECTION_DISTANCE)
					{
						condition = false;
						break;
					}
				}
				if (condition)
				{
					profileIdx = i;
					break;
				}
			}
			if (profileIdx != -1)
			{
				arcAndProfiles.Add(new ArcAndProfile
				{
					ArcIdx = arcGroup2,
					ProfileIdx = profileIdx
				});
			}
		}
		return arcAndProfiles;
	}

	private static void GetConnectedArcGroupsRecursive(Topology topo, int arcIdx, HashSet<int> passedArcs, List<int> outArcGroup)
	{
		if (passedArcs.Contains(arcIdx))
		{
			return;
		}
		passedArcs.Add(arcIdx);
		outArcGroup.Add(arcIdx);
		Arc arc = topo.Arcs[arcIdx];
		if (arc.StartNode != -1)
		{
			foreach (NodeArcConnection arc2 in topo.Nodes[arc.StartNode].Arcs)
			{
				GetConnectedArcGroupsRecursive(topo, arc2.ArcID, passedArcs, outArcGroup);
			}
		}
		if (arc.EndNode == -1)
		{
			return;
		}
		foreach (NodeArcConnection arc3 in topo.Nodes[arc.EndNode].Arcs)
		{
			GetConnectedArcGroupsRecursive(topo, arc3.ArcID, passedArcs, outArcGroup);
		}
	}

	private static IReadOnlyList<IReadOnlyList<int>> GetConnectedArcGroups(Topology topo)
	{
		HashSet<int> intSet = new HashSet<int>();
		List<IReadOnlyList<int>> connectedArcGroups = new List<IReadOnlyList<int>>();
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			if (!intSet.Contains(i))
			{
				List<int> intList = new List<int>();
				GetConnectedArcGroupsRecursive(topo, i, intSet, intList);
				connectedArcGroups.Add(intList);
			}
		}
		return connectedArcGroups;
	}

	private static IEnumerable<ArcGroupIteration> GetArcGroupEnumerator(Topology topo, IReadOnlyList<int> arcGroup)
	{
		new List<int>();
		int unity = -1;
		for (int i = 0; i < arcGroup.Count - 1; i++)
		{
			Arc curArc = topo.Arcs[arcGroup[i]];
			Arc arc = topo.Arcs[arcGroup[i + 1]];
			if (curArc.StartNode == arc.StartNode || curArc.StartNode == arc.EndNode)
			{
				yield return new ArcGroupIteration
				{
					Arc = curArc,
					ArcIdx = arcGroup[i],
					ShouldReverseNodes = true,
					IsFinal = false
				};
				unity = curArc.StartNode;
			}
			else if (curArc.EndNode == arc.StartNode || curArc.EndNode == arc.EndNode)
			{
				yield return new ArcGroupIteration
				{
					Arc = curArc,
					ArcIdx = arcGroup[i],
					ShouldReverseNodes = false,
					IsFinal = false
				};
				unity = curArc.EndNode;
			}
		}
		Arc arc2 = topo.Arcs[arcGroup[arcGroup.Count - 1]];
		if (arc2.StartNode == unity)
		{
			yield return new ArcGroupIteration
			{
				Arc = arc2,
				ArcIdx = arcGroup[arcGroup.Count - 1],
				ShouldReverseNodes = false,
				IsFinal = true
			};
		}
		else
		{
			yield return new ArcGroupIteration
			{
				Arc = arc2,
				ArcIdx = arcGroup[arcGroup.Count - 1],
				ShouldReverseNodes = true,
				IsFinal = true
			};
		}
	}

	private static IReadOnlyList<Polyline> ArcGroupToPolylines(Topology topo, IReadOnlyList<int> arcGroup)
	{
		List<Polyline> polylines = new List<Polyline>();
		foreach (ArcGroupIteration arcGroupEnumerator in GetArcGroupEnumerator(topo, arcGroup))
		{
			Point[] arcPoints = GetArcPoints(topo, arcGroupEnumerator.ArcIdx);
			if (arcGroupEnumerator.ShouldReverseNodes)
			{
				arcPoints.AsSpan().Reverse();
			}
			polylines.Add(new Polyline(arcPoints));
		}
		return polylines;
	}

	private static Polygon ArcGroupToPolygon(Topology topo, IReadOnlyList<int> arcGroup, double? z = null)
	{
		List<Point> points = new List<Point>();
		foreach (ArcGroupIteration arcGroupEnumerator in GetArcGroupEnumerator(topo, arcGroup))
		{
			Point[] arcPoints = GetArcPoints(topo, arcGroupEnumerator.ArcIdx);
			if (arcGroupEnumerator.ShouldReverseNodes)
			{
				arcPoints.AsSpan().Reverse();
			}
			for (int i = 0; i < arcPoints.Length - 1; i++)
			{
				points.Add(arcPoints[i]);
			}
			if (arcGroupEnumerator.IsFinal)
			{
				points.Add(arcPoints[^1]);
			}
		}
		if (z.HasValue)
		{
			double[] doubleValues = new double[points.Count];
			doubleValues.Fill(1.0);
			return new Polygon(points.ToArray(), doubleValues);
		}
		return new Polygon(points);
	}

	private static void GetRiverArcPolylines(Topology topo, IReadOnlyList<int> crossSectionA, IReadOnlyList<int> crossSectionB, out IReadOnlyList<double> outMValuesA, out IReadOnlyList<double> outMValuesB, out IReadOnlyList<double> outMValuesRiver, out IReadOnlyList<Polyline> outRiverPolylines)
	{
		IReadOnlyList<int> readOnlyList = ArcGroupToNodeList(topo, crossSectionA);
		IReadOnlyList<int> readOnlyList2 = ArcGroupToNodeList(topo, crossSectionB);
		List<Polyline> outRiverPolylinesList = new List<Polyline>();
		int[] intValues = new int[readOnlyList.Count];
		int[] intValuesArray = new int[readOnlyList2.Count];
		List<int> intList = new List<int>();
		int ordinal = 0;
		int ordinal2 = 0;
		int curIdx = 0;
		while (ordinal < readOnlyList.Count && ordinal2 < readOnlyList2.Count)
		{
			int index = readOnlyList[ordinal];
			int index2 = readOnlyList2[ordinal2];
			Node node = topo.Nodes[index];
			Node node2 = topo.Nodes[index2];
			int unity = -1;
			int unity2 = -1;
			foreach (NodeArcConnection arc3 in node.Arcs)
			{
				Arc arc = topo.Arcs[arc3.ArcID];
				if (readOnlyList2.Contains(arc.StartNode))
				{
					unity = arc3.ArcID;
					break;
				}
				if (readOnlyList2.Contains(arc.EndNode))
				{
					unity = arc3.ArcID;
					break;
				}
			}
			foreach (NodeArcConnection arc4 in node2.Arcs)
			{
				Arc arc2 = topo.Arcs[arc4.ArcID];
				if (readOnlyList.Contains(arc2.StartNode))
				{
					unity2 = arc4.ArcID;
					break;
				}
				if (readOnlyList.Contains(arc2.EndNode))
				{
					unity2 = arc4.ArcID;
					break;
				}
			}
			if (unity == -1 && unity2 == -1)
			{
				intValues[ordinal++] = curIdx;
				intValuesArray[ordinal2++] = curIdx;
			}
			else if (unity == unity2)
			{
				intValues[ordinal++] = curIdx;
				intValuesArray[ordinal2++] = curIdx;
				Point[] arcPoints = GetArcPoints(topo, unity);
				if (readOnlyList2.Contains(topo.Arcs[unity].StartNode))
				{
					arcPoints.AsSpan().Reverse();
				}
				outRiverPolylinesList.Add(new Polyline(arcPoints));
				intList.Add(curIdx);
			}
			else if (unity == -1)
			{
				intValues[ordinal++] = curIdx;
			}
			else if (unity2 == -1)
			{
				intValuesArray[ordinal2++] = curIdx;
			}
			curIdx++;
		}
		outMValuesA = intValues.Select((int ordinal3) => (double)ordinal3 / (double)(curIdx - 1)).ToArray();
		outMValuesB = intValuesArray.Select((int ordinal4) => (double)ordinal4 / (double)(curIdx - 1)).ToArray();
		outMValuesRiver = intList.Select((int ordinal5) => (double)ordinal5 / (double)(curIdx - 1)).ToArray();
		outRiverPolylines = outRiverPolylinesList;
	}

	private static Point[] GetArcPoints(Topology topo, int arcIdx)
	{
		Point[] points = new Point[topo.Arcs[arcIdx].InternalPointCount + 2];
		for (int i = 0; i < topo.Arcs[arcIdx].InternalPointCount; i++)
		{
			points[i + 1] = topo.Arcs[arcIdx].InternalPoints[i];
		}
		points[0] = topo.Nodes[topo.Arcs[arcIdx].StartNode].Point;
		points[^1] = topo.Nodes[topo.Arcs[arcIdx].EndNode].Point;
		return points;
	}

	private static IReadOnlyList<int> ArcGroupToNodeList(Topology topo, IReadOnlyList<int> arcGroup)
	{
		List<int> intList = new List<int>();
		foreach (ArcGroupIteration arcGroupEnumerator in GetArcGroupEnumerator(topo, arcGroup))
		{
			if (!arcGroupEnumerator.ShouldReverseNodes)
			{
				intList.Add(arcGroupEnumerator.Arc.StartNode);
				if (arcGroupEnumerator.IsFinal)
				{
					intList.Add(arcGroupEnumerator.Arc.EndNode);
				}
			}
			else
			{
				intList.Add(arcGroupEnumerator.Arc.EndNode);
				if (arcGroupEnumerator.IsFinal)
				{
					intList.Add(arcGroupEnumerator.Arc.StartNode);
				}
			}
		}
		return intList;
	}

	private static IReadOnlyList<IReadOnlyList<int>> GetMaskPerimeters(Topology topo)
	{
		if (topo.ControlPointMetadata.GetColumn(MaskColumnName) == null)
		{
			return null;
		}
		HashSet<int> allMaskPerimeterArcs = new HashSet<int>(GetAllMaskPerimeterArcs(topo));
		if (allMaskPerimeterArcs.Count <= 0)
		{
			return null;
		}
		List<int> intList = new List<int>();
		List<IReadOnlyList<int>> intListList = new List<IReadOnlyList<int>>();
		intListList.Add(intList);
		int allMaskPerimeterArcsTerm = allMaskPerimeterArcs.First();
		intList.Add(allMaskPerimeterArcsTerm);
		allMaskPerimeterArcs.Remove(allMaskPerimeterArcsTerm);
		while (allMaskPerimeterArcs.Count > 0)
		{
			Arc arc = topo.Arcs[allMaskPerimeterArcsTerm];
			bool condition = false;
			foreach (NodeArcConnection arc2 in topo.Nodes[arc.StartNode].Arcs)
			{
				if (allMaskPerimeterArcs.Contains(arc2.ArcID))
				{
					allMaskPerimeterArcsTerm = arc2.ArcID;
					intList.Add(allMaskPerimeterArcsTerm);
					allMaskPerimeterArcs.Remove(allMaskPerimeterArcsTerm);
					condition = true;
					break;
				}
			}
			if (condition)
			{
				continue;
			}
			foreach (NodeArcConnection arc3 in topo.Nodes[arc.EndNode].Arcs)
			{
				if (allMaskPerimeterArcs.Contains(arc3.ArcID))
				{
					allMaskPerimeterArcsTerm = arc3.ArcID;
					intList.Add(allMaskPerimeterArcsTerm);
					allMaskPerimeterArcs.Remove(allMaskPerimeterArcsTerm);
					condition = true;
					break;
				}
			}
			if (!condition && allMaskPerimeterArcs.Count > 0)
			{
				intList = new List<int>();
				intListList.Add(intList);
				allMaskPerimeterArcsTerm = allMaskPerimeterArcs.First();
				allMaskPerimeterArcs.Remove(allMaskPerimeterArcsTerm);
			}
		}
		return intListList;
	}

	public static bool GetRegionIsMask(Topology topo, int regionIdx)
	{
		int controlPointIndex = topo.CompletedRegions[regionIdx].ControlPointIndex;
		if (controlPointIndex == -1)
		{
			return false;
		}
		return topo.ControlPointMetadata[controlPointIndex].ValueAs(MaskColumnName, fallbackValue: false);
	}

	public static void SetRegionIsMask(Topology topo, int regionIdx, bool mask)
	{
		int controlPointIndex = topo.CompletedRegions[regionIdx].ControlPointIndex;
		if (controlPointIndex != -1)
		{
			topo.ControlPointMetadata[controlPointIndex].SetValue(MaskColumnName, mask);
		}
	}

	private static IReadOnlyList<int> GetAllMaskPerimeterArcs(Topology topo)
	{
		List<int> allMaskPerimeterArcs = new List<int>();
		for (int i = 0; i < topo.Arcs.Count; i++)
		{
			if (GetArcMaskRegionCount(topo, i) == 1)
			{
				allMaskPerimeterArcs.Add(i);
			}
		}
		return allMaskPerimeterArcs;
	}

	private static int GetArcMaskRegionCount(Topology topo, int arcIdx)
	{
		Arc arc = topo.Arcs[arcIdx];
		int arcMaskRegionCount = 0;
		if (arc.RegionA != -1 && GetRegionIsMask(topo, arc.RegionA))
		{
			arcMaskRegionCount++;
		}
		if (arc.RegionB != -1 && GetRegionIsMask(topo, arc.RegionB))
		{
			arcMaskRegionCount++;
		}
		return arcMaskRegionCount;
	}

	public static Polygon RegionToPolygon(Topology topo, int regionIdx)
	{
		int[] arcGroup = topo.CompletedRegions[regionIdx].OuterRing.Arcs.Select((ArcWithDirection a) => a.ArcID).ToArray();
		return ArcGroupToPolygon(topo, arcGroup);
	}
}
