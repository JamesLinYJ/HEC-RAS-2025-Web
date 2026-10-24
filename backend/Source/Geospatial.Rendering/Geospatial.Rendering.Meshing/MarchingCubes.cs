using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Meshing;

public static class MarchingCubes
{
	private enum EdgeDirection
	{
		X,
		Y,
		Z
	}

	public struct MarchingCubesMesh
	{
		public Point3D[] VertexPositions;

		public uint[] Indices;
	}

	private static readonly int[][] TriangleTable = new int[256][]
	{
		Array.Empty<int>(),
		new int[3] { 0, 3, 8 },
		new int[3] { 0, 9, 1 },
		new int[6] { 3, 8, 1, 1, 8, 9 },
		new int[3] { 2, 11, 3 },
		new int[6] { 8, 0, 11, 11, 0, 2 },
		new int[6] { 3, 2, 11, 1, 0, 9 },
		new int[9] { 11, 1, 2, 11, 9, 1, 11, 8, 9 },
		new int[3] { 1, 10, 2 },
		new int[6] { 0, 3, 8, 2, 1, 10 },
		new int[6] { 10, 2, 9, 9, 2, 0 },
		new int[9] { 8, 2, 3, 8, 10, 2, 8, 9, 10 },
		new int[6] { 11, 3, 10, 10, 3, 1 },
		new int[9] { 10, 0, 1, 10, 8, 0, 10, 11, 8 },
		new int[9] { 9, 3, 0, 9, 11, 3, 9, 10, 11 },
		new int[6] { 8, 9, 11, 11, 9, 10 },
		new int[3] { 4, 8, 7 },
		new int[6] { 7, 4, 3, 3, 4, 0 },
		new int[6] { 4, 8, 7, 0, 9, 1 },
		new int[9] { 1, 4, 9, 1, 7, 4, 1, 3, 7 },
		new int[6] { 8, 7, 4, 11, 3, 2 },
		new int[9] { 4, 11, 7, 4, 2, 11, 4, 0, 2 },
		new int[9] { 0, 9, 1, 8, 7, 4, 11, 3, 2 },
		new int[12]
		{
			7, 4, 11, 11, 4, 2, 2, 4, 9, 2,
			9, 1
		},
		new int[6] { 4, 8, 7, 2, 1, 10 },
		new int[9] { 7, 4, 3, 3, 4, 0, 10, 2, 1 },
		new int[9] { 10, 2, 9, 9, 2, 0, 7, 4, 8 },
		new int[12]
		{
			10, 2, 3, 10, 3, 4, 3, 7, 4, 9,
			10, 4
		},
		new int[9] { 1, 10, 3, 3, 10, 11, 4, 8, 7 },
		new int[12]
		{
			10, 11, 1, 11, 7, 4, 1, 11, 4, 1,
			4, 0
		},
		new int[12]
		{
			7, 4, 8, 9, 3, 0, 9, 11, 3, 9,
			10, 11
		},
		new int[9] { 7, 4, 11, 4, 9, 11, 9, 10, 11 },
		new int[3] { 9, 4, 5 },
		new int[6] { 9, 4, 5, 8, 0, 3 },
		new int[6] { 4, 5, 0, 0, 5, 1 },
		new int[9] { 5, 8, 4, 5, 3, 8, 5, 1, 3 },
		new int[6] { 9, 4, 5, 11, 3, 2 },
		new int[9] { 2, 11, 0, 0, 11, 8, 5, 9, 4 },
		new int[9] { 4, 5, 0, 0, 5, 1, 11, 3, 2 },
		new int[12]
		{
			5, 1, 4, 1, 2, 11, 4, 1, 11, 4,
			11, 8
		},
		new int[6] { 1, 10, 2, 5, 9, 4 },
		new int[9] { 9, 4, 5, 0, 3, 8, 2, 1, 10 },
		new int[9] { 2, 5, 10, 2, 4, 5, 2, 0, 4 },
		new int[12]
		{
			10, 2, 5, 5, 2, 4, 4, 2, 3, 4,
			3, 8
		},
		new int[9] { 11, 3, 10, 10, 3, 1, 4, 5, 9 },
		new int[12]
		{
			4, 5, 9, 10, 0, 1, 10, 8, 0, 10,
			11, 8
		},
		new int[12]
		{
			11, 3, 0, 11, 0, 5, 0, 4, 5, 10,
			11, 5
		},
		new int[9] { 4, 5, 8, 5, 10, 8, 10, 11, 8 },
		new int[6] { 8, 7, 9, 9, 7, 5 },
		new int[9] { 3, 9, 0, 3, 5, 9, 3, 7, 5 },
		new int[9] { 7, 0, 8, 7, 1, 0, 7, 5, 1 },
		new int[6] { 7, 5, 3, 3, 5, 1 },
		new int[9] { 5, 9, 7, 7, 9, 8, 2, 11, 3 },
		new int[12]
		{
			2, 11, 7, 2, 7, 9, 7, 5, 9, 0,
			2, 9
		},
		new int[12]
		{
			2, 11, 3, 7, 0, 8, 7, 1, 0, 7,
			5, 1
		},
		new int[9] { 2, 11, 1, 11, 7, 1, 7, 5, 1 },
		new int[9] { 8, 7, 9, 9, 7, 5, 2, 1, 10 },
		new int[12]
		{
			10, 2, 1, 3, 9, 0, 3, 5, 9, 3,
			7, 5
		},
		new int[12]
		{
			7, 5, 8, 5, 10, 2, 8, 5, 2, 8,
			2, 0
		},
		new int[9] { 10, 2, 5, 2, 3, 5, 3, 7, 5 },
		new int[12]
		{
			8, 7, 5, 8, 5, 9, 11, 3, 10, 3,
			1, 10
		},
		new int[9] { 5, 11, 7, 10, 11, 5, 1, 9, 0 },
		new int[9] { 11, 5, 10, 7, 5, 11, 8, 3, 0 },
		new int[6] { 5, 11, 7, 10, 11, 5 },
		new int[3] { 6, 7, 11 },
		new int[6] { 7, 11, 6, 3, 8, 0 },
		new int[6] { 6, 7, 11, 0, 9, 1 },
		new int[9] { 9, 1, 8, 8, 1, 3, 6, 7, 11 },
		new int[6] { 3, 2, 7, 7, 2, 6 },
		new int[9] { 0, 7, 8, 0, 6, 7, 0, 2, 6 },
		new int[9] { 6, 7, 2, 2, 7, 3, 9, 1, 0 },
		new int[12]
		{
			6, 7, 8, 6, 8, 1, 8, 9, 1, 2,
			6, 1
		},
		new int[6] { 11, 6, 7, 10, 2, 1 },
		new int[9] { 3, 8, 0, 11, 6, 7, 10, 2, 1 },
		new int[9] { 0, 9, 2, 2, 9, 10, 7, 11, 6 },
		new int[12]
		{
			6, 7, 11, 8, 2, 3, 8, 10, 2, 8,
			9, 10
		},
		new int[9] { 7, 10, 6, 7, 1, 10, 7, 3, 1 },
		new int[12]
		{
			8, 0, 7, 7, 0, 6, 6, 0, 1, 6,
			1, 10
		},
		new int[12]
		{
			7, 3, 6, 3, 0, 9, 6, 3, 9, 6,
			9, 10
		},
		new int[9] { 6, 7, 10, 7, 8, 10, 8, 9, 10 },
		new int[6] { 11, 6, 8, 8, 6, 4 },
		new int[9] { 6, 3, 11, 6, 0, 3, 6, 4, 0 },
		new int[9] { 11, 6, 8, 8, 6, 4, 1, 0, 9 },
		new int[12]
		{
			1, 3, 9, 3, 11, 6, 9, 3, 6, 9,
			6, 4
		},
		new int[9] { 2, 8, 3, 2, 4, 8, 2, 6, 4 },
		new int[6] { 4, 0, 6, 6, 0, 2 },
		new int[12]
		{
			9, 1, 0, 2, 8, 3, 2, 4, 8, 2,
			6, 4
		},
		new int[9] { 9, 1, 4, 1, 2, 4, 2, 6, 4 },
		new int[9] { 4, 8, 6, 6, 8, 11, 1, 10, 2 },
		new int[12]
		{
			1, 10, 2, 6, 3, 11, 6, 0, 3, 6,
			4, 0
		},
		new int[12]
		{
			11, 6, 4, 11, 4, 8, 10, 2, 9, 2,
			0, 9
		},
		new int[9] { 10, 4, 9, 6, 4, 10, 11, 2, 3 },
		new int[12]
		{
			4, 8, 3, 4, 3, 10, 3, 1, 10, 6,
			4, 10
		},
		new int[9] { 1, 10, 0, 10, 6, 0, 6, 4, 0 },
		new int[9] { 4, 10, 6, 9, 10, 4, 0, 8, 3 },
		new int[6] { 4, 10, 6, 9, 10, 4 },
		new int[6] { 6, 7, 11, 4, 5, 9 },
		new int[9] { 4, 5, 9, 7, 11, 6, 3, 8, 0 },
		new int[9] { 1, 0, 5, 5, 0, 4, 11, 6, 7 },
		new int[12]
		{
			11, 6, 7, 5, 8, 4, 5, 3, 8, 5,
			1, 3
		},
		new int[9] { 3, 2, 7, 7, 2, 6, 9, 4, 5 },
		new int[12]
		{
			5, 9, 4, 0, 7, 8, 0, 6, 7, 0,
			2, 6
		},
		new int[12]
		{
			3, 2, 6, 3, 6, 7, 1, 0, 5, 0,
			4, 5
		},
		new int[9] { 6, 1, 2, 5, 1, 6, 4, 7, 8 },
		new int[9] { 10, 2, 1, 6, 7, 11, 4, 5, 9 },
		new int[12]
		{
			0, 3, 8, 4, 5, 9, 11, 6, 7, 10,
			2, 1
		},
		new int[12]
		{
			7, 11, 6, 2, 5, 10, 2, 4, 5, 2,
			0, 4
		},
		new int[9] { 8, 4, 7, 5, 10, 6, 3, 11, 2 },
		new int[12]
		{
			9, 4, 5, 7, 10, 6, 7, 1, 10, 7,
			3, 1
		},
		new int[9] { 10, 6, 5, 7, 8, 4, 1, 9, 0 },
		new int[9] { 4, 3, 0, 7, 3, 4, 6, 5, 10 },
		new int[6] { 10, 6, 5, 8, 4, 7 },
		new int[9] { 9, 6, 5, 9, 11, 6, 9, 8, 11 },
		new int[12]
		{
			11, 6, 3, 3, 6, 0, 0, 6, 5, 0,
			5, 9
		},
		new int[12]
		{
			11, 6, 5, 11, 5, 0, 5, 1, 0, 8,
			11, 0
		},
		new int[9] { 11, 6, 3, 6, 5, 3, 5, 1, 3 },
		new int[12]
		{
			9, 8, 5, 8, 3, 2, 5, 8, 2, 5,
			2, 6
		},
		new int[9] { 5, 9, 6, 9, 0, 6, 0, 2, 6 },
		new int[9] { 1, 6, 5, 2, 6, 1, 3, 0, 8 },
		new int[6] { 1, 6, 5, 2, 6, 1 },
		new int[12]
		{
			2, 1, 10, 9, 6, 5, 9, 11, 6, 9,
			8, 11
		},
		new int[9] { 9, 0, 1, 3, 11, 2, 5, 10, 6 },
		new int[9] { 11, 0, 8, 2, 0, 11, 10, 6, 5 },
		new int[6] { 3, 11, 2, 5, 10, 6 },
		new int[9] { 1, 8, 3, 9, 8, 1, 5, 10, 6 },
		new int[6] { 6, 5, 10, 0, 1, 9 },
		new int[6] { 8, 3, 0, 5, 10, 6 },
		new int[3] { 6, 5, 10 },
		new int[3] { 10, 5, 6 },
		new int[6] { 0, 3, 8, 6, 10, 5 },
		new int[6] { 10, 5, 6, 9, 1, 0 },
		new int[9] { 3, 8, 1, 1, 8, 9, 6, 10, 5 },
		new int[6] { 2, 11, 3, 6, 10, 5 },
		new int[9] { 8, 0, 11, 11, 0, 2, 5, 6, 10 },
		new int[9] { 1, 0, 9, 2, 11, 3, 6, 10, 5 },
		new int[12]
		{
			5, 6, 10, 11, 1, 2, 11, 9, 1, 11,
			8, 9
		},
		new int[6] { 5, 6, 1, 1, 6, 2 },
		new int[9] { 5, 6, 1, 1, 6, 2, 8, 0, 3 },
		new int[9] { 6, 9, 5, 6, 0, 9, 6, 2, 0 },
		new int[12]
		{
			6, 2, 5, 2, 3, 8, 5, 2, 8, 5,
			8, 9
		},
		new int[9] { 3, 6, 11, 3, 5, 6, 3, 1, 5 },
		new int[12]
		{
			8, 0, 1, 8, 1, 6, 1, 5, 6, 11,
			8, 6
		},
		new int[12]
		{
			11, 3, 6, 6, 3, 5, 5, 3, 0, 5,
			0, 9
		},
		new int[9] { 5, 6, 9, 6, 11, 9, 11, 8, 9 },
		new int[6] { 5, 6, 10, 7, 4, 8 },
		new int[9] { 0, 3, 4, 4, 3, 7, 10, 5, 6 },
		new int[9] { 5, 6, 10, 4, 8, 7, 0, 9, 1 },
		new int[12]
		{
			6, 10, 5, 1, 4, 9, 1, 7, 4, 1,
			3, 7
		},
		new int[9] { 7, 4, 8, 6, 10, 5, 2, 11, 3 },
		new int[12]
		{
			10, 5, 6, 4, 11, 7, 4, 2, 11, 4,
			0, 2
		},
		new int[12]
		{
			4, 8, 7, 6, 10, 5, 3, 2, 11, 1,
			0, 9
		},
		new int[9] { 1, 2, 10, 11, 7, 6, 9, 5, 4 },
		new int[9] { 2, 1, 6, 6, 1, 5, 8, 7, 4 },
		new int[12]
		{
			0, 3, 7, 0, 7, 4, 2, 1, 6, 1,
			5, 6
		},
		new int[12]
		{
			8, 7, 4, 6, 9, 5, 6, 0, 9, 6,
			2, 0
		},
		new int[9] { 7, 2, 3, 6, 2, 7, 5, 4, 9 },
		new int[12]
		{
			4, 8, 7, 3, 6, 11, 3, 5, 6, 3,
			1, 5
		},
		new int[9] { 5, 0, 1, 4, 0, 5, 7, 6, 11 },
		new int[9] { 9, 5, 4, 6, 11, 7, 0, 8, 3 },
		new int[6] { 11, 7, 6, 9, 5, 4 },
		new int[6] { 6, 10, 4, 4, 10, 9 },
		new int[9] { 6, 10, 4, 4, 10, 9, 3, 8, 0 },
		new int[9] { 0, 10, 1, 0, 6, 10, 0, 4, 6 },
		new int[12]
		{
			6, 10, 1, 6, 1, 8, 1, 3, 8, 4,
			6, 8
		},
		new int[9] { 9, 4, 10, 10, 4, 6, 3, 2, 11 },
		new int[12]
		{
			2, 11, 8, 2, 8, 0, 6, 10, 4, 10,
			9, 4
		},
		new int[12]
		{
			11, 3, 2, 0, 10, 1, 0, 6, 10, 0,
			4, 6
		},
		new int[9] { 6, 8, 4, 11, 8, 6, 2, 10, 1 },
		new int[9] { 4, 1, 9, 4, 2, 1, 4, 6, 2 },
		new int[12]
		{
			3, 8, 0, 4, 1, 9, 4, 2, 1, 4,
			6, 2
		},
		new int[6] { 6, 2, 4, 4, 2, 0 },
		new int[9] { 3, 8, 2, 8, 4, 2, 4, 6, 2 },
		new int[12]
		{
			4, 6, 9, 6, 11, 3, 9, 6, 3, 9,
			3, 1
		},
		new int[9] { 8, 6, 11, 4, 6, 8, 9, 0, 1 },
		new int[9] { 11, 3, 6, 3, 0, 6, 0, 4, 6 },
		new int[6] { 8, 6, 11, 4, 6, 8 },
		new int[9] { 10, 7, 6, 10, 8, 7, 10, 9, 8 },
		new int[12]
		{
			3, 7, 0, 7, 6, 10, 0, 7, 10, 0,
			10, 9
		},
		new int[12]
		{
			6, 10, 7, 7, 10, 8, 8, 10, 1, 8,
			1, 0
		},
		new int[9] { 6, 10, 7, 10, 1, 7, 1, 3, 7 },
		new int[12]
		{
			3, 2, 11, 10, 7, 6, 10, 8, 7, 10,
			9, 8
		},
		new int[9] { 2, 9, 0, 10, 9, 2, 6, 11, 7 },
		new int[9] { 0, 8, 3, 7, 6, 11, 1, 2, 10 },
		new int[6] { 7, 6, 11, 1, 2, 10 },
		new int[12]
		{
			2, 1, 9, 2, 9, 7, 9, 8, 7, 6,
			2, 7
		},
		new int[9] { 2, 7, 6, 3, 7, 2, 0, 1, 9 },
		new int[9] { 8, 7, 0, 7, 6, 0, 6, 2, 0 },
		new int[6] { 7, 2, 3, 6, 2, 7 },
		new int[9] { 8, 1, 9, 3, 1, 8, 11, 7, 6 },
		new int[6] { 11, 7, 6, 1, 9, 0 },
		new int[6] { 6, 11, 7, 0, 8, 3 },
		new int[3] { 11, 7, 6 },
		new int[6] { 7, 11, 5, 5, 11, 10 },
		new int[9] { 10, 5, 11, 11, 5, 7, 0, 3, 8 },
		new int[9] { 7, 11, 5, 5, 11, 10, 0, 9, 1 },
		new int[12]
		{
			7, 11, 10, 7, 10, 5, 3, 8, 1, 8,
			9, 1
		},
		new int[9] { 5, 2, 10, 5, 3, 2, 5, 7, 3 },
		new int[12]
		{
			5, 7, 10, 7, 8, 0, 10, 7, 0, 10,
			0, 2
		},
		new int[12]
		{
			0, 9, 1, 5, 2, 10, 5, 3, 2, 5,
			7, 3
		},
		new int[9] { 9, 7, 8, 5, 7, 9, 10, 1, 2 },
		new int[9] { 1, 11, 2, 1, 7, 11, 1, 5, 7 },
		new int[12]
		{
			8, 0, 3, 1, 11, 2, 1, 7, 11, 1,
			5, 7
		},
		new int[12]
		{
			7, 11, 2, 7, 2, 9, 2, 0, 9, 5,
			7, 9
		},
		new int[9] { 7, 9, 5, 8, 9, 7, 3, 11, 2 },
		new int[6] { 3, 1, 7, 7, 1, 5 },
		new int[9] { 8, 0, 7, 0, 1, 7, 1, 5, 7 },
		new int[9] { 0, 9, 3, 9, 5, 3, 5, 7, 3 },
		new int[6] { 9, 7, 8, 5, 7, 9 },
		new int[9] { 8, 5, 4, 8, 10, 5, 8, 11, 10 },
		new int[12]
		{
			0, 3, 11, 0, 11, 5, 11, 10, 5, 4,
			0, 5
		},
		new int[12]
		{
			1, 0, 9, 8, 5, 4, 8, 10, 5, 8,
			11, 10
		},
		new int[9] { 10, 3, 11, 1, 3, 10, 9, 5, 4 },
		new int[12]
		{
			3, 2, 8, 8, 2, 4, 4, 2, 10, 4,
			10, 5
		},
		new int[9] { 10, 5, 2, 5, 4, 2, 4, 0, 2 },
		new int[9] { 5, 4, 9, 8, 3, 0, 10, 1, 2 },
		new int[6] { 2, 10, 1, 4, 9, 5 },
		new int[12]
		{
			8, 11, 4, 11, 2, 1, 4, 11, 1, 4,
			1, 5
		},
		new int[9] { 0, 5, 4, 1, 5, 0, 2, 3, 11 },
		new int[9] { 0, 11, 2, 8, 11, 0, 4, 9, 5 },
		new int[6] { 5, 4, 9, 2, 3, 11 },
		new int[9] { 4, 8, 5, 8, 3, 5, 3, 1, 5 },
		new int[6] { 0, 5, 4, 1, 5, 0 },
		new int[6] { 5, 4, 9, 3, 0, 8 },
		new int[3] { 5, 4, 9 },
		new int[9] { 11, 4, 7, 11, 9, 4, 11, 10, 9 },
		new int[12]
		{
			0, 3, 8, 11, 4, 7, 11, 9, 4, 11,
			10, 9
		},
		new int[12]
		{
			11, 10, 7, 10, 1, 0, 7, 10, 0, 7,
			0, 4
		},
		new int[9] { 3, 10, 1, 11, 10, 3, 7, 8, 4 },
		new int[12]
		{
			3, 2, 10, 3, 10, 4, 10, 9, 4, 7,
			3, 4
		},
		new int[9] { 9, 2, 10, 0, 2, 9, 8, 4, 7 },
		new int[9] { 3, 4, 7, 0, 4, 3, 1, 2, 10 },
		new int[6] { 7, 8, 4, 10, 1, 2 },
		new int[12]
		{
			7, 11, 4, 4, 11, 9, 9, 11, 2, 9,
			2, 1
		},
		new int[9] { 1, 9, 0, 4, 7, 8, 2, 3, 11 },
		new int[9] { 7, 11, 4, 11, 2, 4, 2, 0, 4 },
		new int[6] { 4, 7, 8, 2, 3, 11 },
		new int[9] { 9, 4, 1, 4, 7, 1, 7, 3, 1 },
		new int[6] { 7, 8, 4, 1, 9, 0 },
		new int[6] { 3, 4, 7, 0, 4, 3 },
		new int[3] { 7, 8, 4 },
		new int[6] { 11, 10, 8, 8, 10, 9 },
		new int[9] { 0, 3, 9, 3, 11, 9, 11, 10, 9 },
		new int[9] { 1, 0, 10, 0, 8, 10, 8, 11, 10 },
		new int[6] { 10, 3, 11, 1, 3, 10 },
		new int[9] { 3, 2, 8, 2, 10, 8, 10, 9, 8 },
		new int[6] { 9, 2, 10, 0, 2, 9 },
		new int[6] { 8, 3, 0, 10, 1, 2 },
		new int[3] { 2, 10, 1 },
		new int[9] { 2, 1, 11, 1, 9, 11, 9, 8, 11 },
		new int[6] { 11, 2, 3, 9, 0, 1 },
		new int[6] { 11, 0, 8, 2, 0, 11 },
		new int[3] { 3, 11, 2 },
		new int[6] { 1, 8, 3, 9, 8, 1 },
		new int[3] { 1, 9, 0 },
		new int[3] { 8, 3, 0 },
		Array.Empty<int>()
	};

	public static MarchingCubesMesh GenerateMarchingCubesMesh(double minX, double maxX, double minY, double maxY, double minZ, double maxZ, double cellSize, SignedDistanceFunction distanceFunction)
	{
		List<Point3D> point3Ds = new List<Point3D>();
		List<uint> uints = new List<uint>();
		int nodesX = (int)Math.Ceiling((maxX - minX) / cellSize) + 1;
		int nodesY = (int)Math.Ceiling((maxY - minY) / cellSize) + 1;
		int nodesZ = (int)Math.Ceiling((maxZ - minZ) / cellSize) + 1;
		int count = nodesX * nodesY * nodesZ;
		int cellsX = nodesX - 1;
		int cellsY = nodesY - 1;
		int cellsZ = nodesZ - 1;
		int count2 = cellsX * cellsY * cellsZ;
		double[] distances = new double[count];
		Parallel.For(0, count, (int i) =>
		{
			int x = i % nodesX;
			int y = i / nodesX % nodesY;
			int z = i / (nodesX * nodesY);
			Point3D nodePosition = GetNodePosition(x, y, z, minX, maxX, minY, maxY, minZ, maxZ, cellSize);
			distances[i] = distanceFunction(nodePosition.X, nodePosition.Y, nodePosition.Z);
		});
		Point3D?[] edgeVertices = new Point3D?[count2 * 3];
		Parallel.For(0, count2, (int i) =>
		{
			int cellsXTerm = i % cellsX;
			int cellsXCellsYTerm = i / cellsX % cellsY;
			int cellsXCellsYTerm2 = i / (cellsX * cellsY);
			Point3D nodePosition = GetNodePosition(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2, minX, maxX, minY, maxY, minZ, maxZ, cellSize);
			Point3D nodePosition2 = GetNodePosition(cellsXTerm + 1, cellsXCellsYTerm, cellsXCellsYTerm2, minX, maxX, minY, maxY, minZ, maxZ, cellSize);
			Point3D nodePosition3 = GetNodePosition(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2 + 1, minX, maxX, minY, maxY, minZ, maxZ, cellSize);
			Point3D nodePosition4 = GetNodePosition(cellsXTerm, cellsXCellsYTerm + 1, cellsXCellsYTerm2, minX, maxX, minY, maxY, minZ, maxZ, cellSize);
			double nodeDistance9 = GetNodeDistance(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2, nodesX, nodesY, nodesZ, distances);
			double nodeDistance10 = GetNodeDistance(cellsXTerm + 1, cellsXCellsYTerm, cellsXCellsYTerm2, nodesX, nodesY, nodesZ, distances);
			double nodeDistance11 = GetNodeDistance(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2 + 1, nodesX, nodesY, nodesZ, distances);
			double nodeDistance12 = GetNodeDistance(cellsXTerm, cellsXCellsYTerm + 1, cellsXCellsYTerm2, nodesX, nodesY, nodesZ, distances);
			int edgeVertIndex = GetEdgeVertIndex(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2, cellsX, cellsY, cellsZ, EdgeDirection.X);
			int edgeVertIndex2 = GetEdgeVertIndex(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2, cellsX, cellsY, cellsZ, EdgeDirection.Y);
			int edgeVertIndex3 = GetEdgeVertIndex(cellsXTerm, cellsXCellsYTerm, cellsXCellsYTerm2, cellsX, cellsY, cellsZ, EdgeDirection.Z);
			if (nodeDistance9 * nodeDistance10 <= 0.0)
			{
				edgeVertices[edgeVertIndex] = Lerp(nodePosition, nodePosition2, (0.0 - nodeDistance9) / (nodeDistance10 - nodeDistance9));
			}
			if (nodeDistance9 * nodeDistance12 <= 0.0)
			{
				edgeVertices[edgeVertIndex2] = Lerp(nodePosition, nodePosition4, (0.0 - nodeDistance9) / (nodeDistance12 - nodeDistance9));
			}
			if (nodeDistance9 * nodeDistance11 <= 0.0)
			{
				edgeVertices[edgeVertIndex3] = Lerp(nodePosition, nodePosition3, (0.0 - nodeDistance9) / (nodeDistance11 - nodeDistance9));
			}
		});
		int[] intValues = new int[count2 * 3];
		for (int intervalIndex = 0; intervalIndex < edgeVertices.Length; intervalIndex++)
		{
			if (edgeVertices[intervalIndex].HasValue)
			{
				intValues[intervalIndex] = point3Ds.Count;
				point3Ds.Add(edgeVertices[intervalIndex].Value);
			}
			else
			{
				intValues[intervalIndex] = -1;
			}
		}
		Span<int> intSpan = stackalloc int[12];
		for (int ordinal = 0; ordinal < count2; ordinal++)
		{
			int cellsXTerm2 = ordinal % cellsX;
			int cellsXCellsYTerm3 = ordinal / cellsX % cellsY;
			int cellsXCellsYTerm4 = ordinal / (cellsX * cellsY);
			if (cellsXTerm2 < cellsX - 2 && cellsXCellsYTerm3 <= cellsY - 2 && cellsXCellsYTerm4 < cellsZ - 2)
			{
				double nodeDistance = GetNodeDistance(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4, nodesX, nodesY, nodesZ, distances);
				double nodeDistance2 = GetNodeDistance(cellsXTerm2 + 1, cellsXCellsYTerm3, cellsXCellsYTerm4, nodesX, nodesY, nodesZ, distances);
				double nodeDistance3 = GetNodeDistance(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4 + 1, nodesX, nodesY, nodesZ, distances);
				double nodeDistance4 = GetNodeDistance(cellsXTerm2 + 1, cellsXCellsYTerm3, cellsXCellsYTerm4 + 1, nodesX, nodesY, nodesZ, distances);
				double nodeDistance5 = GetNodeDistance(cellsXTerm2, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4, nodesX, nodesY, nodesZ, distances);
				double nodeDistance6 = GetNodeDistance(cellsXTerm2 + 1, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4, nodesX, nodesY, nodesZ, distances);
				double nodeDistance7 = GetNodeDistance(cellsXTerm2, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4 + 1, nodesX, nodesY, nodesZ, distances);
				double nodeDistance8 = GetNodeDistance(cellsXTerm2 + 1, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4 + 1, nodesX, nodesY, nodesZ, distances);
				intSpan[0] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.X)];
				intSpan[2] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4 + 1, cellsX, cellsY, cellsZ, EdgeDirection.X)];
				intSpan[4] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.X)];
				intSpan[6] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4 + 1, cellsX, cellsY, cellsZ, EdgeDirection.X)];
				intSpan[8] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Y)];
				intSpan[9] = intValues[GetEdgeVertIndex(cellsXTerm2 + 1, cellsXCellsYTerm3, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Y)];
				intSpan[10] = intValues[GetEdgeVertIndex(cellsXTerm2 + 1, cellsXCellsYTerm3, cellsXCellsYTerm4 + 1, cellsX, cellsY, cellsZ, EdgeDirection.Y)];
				intSpan[11] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4 + 1, cellsX, cellsY, cellsZ, EdgeDirection.Y)];
				intSpan[1] = intValues[GetEdgeVertIndex(cellsXTerm2 + 1, cellsXCellsYTerm3, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Z)];
				intSpan[3] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Z)];
				intSpan[5] = intValues[GetEdgeVertIndex(cellsXTerm2 + 1, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Z)];
				intSpan[7] = intValues[GetEdgeVertIndex(cellsXTerm2, cellsXCellsYTerm3 + 1, cellsXCellsYTerm4, cellsX, cellsY, cellsZ, EdgeDirection.Z)];
				byte b = 0;
				b = (byte)((uint)b | ((nodeDistance < 0.0) ? 1u : 0u));
				b |= (byte)(((nodeDistance2 < 0.0) ? 1u : 0u) << 1);
				b |= (byte)(((nodeDistance3 < 0.0) ? 1u : 0u) << 2);
				b |= (byte)(((nodeDistance4 < 0.0) ? 1u : 0u) << 3);
				b |= (byte)(((nodeDistance5 < 0.0) ? 1u : 0u) << 4);
				b |= (byte)(((nodeDistance6 <= 0.0) ? 1u : 0u) << 5);
				b |= (byte)(((nodeDistance7 <= 0.0) ? 1u : 0u) << 6);
				b |= (byte)(((nodeDistance8 <= 0.0) ? 1u : 0u) << 7);
				int[] triangleTable = TriangleTable[b];
				foreach (int index in triangleTable)
				{
					uints.Add((uint)intSpan[index]);
				}
			}
		}
		return new MarchingCubesMesh
		{
			VertexPositions = point3Ds.ToArray(),
			Indices = uints.ToArray()
		};
	}

	private static int GetNodeIndex(int x, int y, int z, int nx, int ny, int nz)
	{
		return z * nx * ny + y * nx + x;
	}

	private static double GetNodeDistance(int x, int y, int z, int nx, int ny, int nz, Span<double> distances)
	{
		return distances[GetNodeIndex(x, y, z, nx, ny, nz)];
	}

	private static Point3D GetNodePosition(int x, int y, int z, double minX, double maxX, double minY, double maxY, double minZ, double maxZ, double cellSize)
	{
		double x2 = minX + (double)x * cellSize;
		double y2 = minY + (double)y * cellSize;
		double z2 = minZ + (double)z * cellSize;
		return new Point3D(x2, y2, z2);
	}

	private static int GetEdgeVertIndex(int x, int y, int z, int nx, int ny, int nz, EdgeDirection direction)
	{
		return (int)(3 * z * nx * ny + 3 * y * nx + 3 * x + direction);
	}

	private static Point3D Lerp(Point3D a, Point3D b, double factor)
	{
		return new Point3D(a.X + (b.X - a.X) * factor, a.Y + (b.Y - a.Y) * factor, a.Z + (b.Z - a.Z) * factor);
	}
}
