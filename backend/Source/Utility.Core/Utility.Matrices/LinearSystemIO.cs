using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Utility.Extensions;
using Utility.Units;

namespace Utility.Matrices;

public static class LinearSystemIO
{
	public const string MatrixExtension = ".matrix";

	public const string RhsExtension = ".rhs";

	public const string SolutionExtension = ".sol";

	public const string MetadataExtension = ".meta.txt";

	public static void Save(string stemPath, SparseMatrix<double> matrix, double[] rhs, double[] solution, double elapsedSeconds, DateTime calendarTime, int outerIteration, int innerIteration, long stepIndex, UnitSystem unitSystem, LinearSystemDumpMode mode)
	{
		WriteMatrix(stemPath + ".matrix", matrix);
		WriteVector(stemPath + ".rhs", rhs);
		WriteVector(stemPath + ".sol", solution);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("cellCount: ");
		handler.AppendFormatted(rhs.Length);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("matrixRows: ");
		handler.AppendFormatted(matrix.RowCount);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("nonZeroCount: ");
		handler.AppendFormatted(matrix.NonZeroCount);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("elapsedSeconds: ");
		handler.AppendFormatted(elapsedSeconds, "R");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("calendar: ");
		handler.AppendFormatted(calendarTime, "yyyy-MM-dd HH:mm:ss.fff");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("outerIteration: ");
		handler.AppendFormatted(outerIteration);
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("innerIteration: ");
		handler.AppendFormatted(innerIteration);
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("stepIndex: ");
		handler.AppendFormatted(stepIndex);
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("unitSystem: ");
		handler.AppendFormatted(unitSystem);
		stringBuilder11.AppendLine(ref handler);
		stringBuilder.AppendLine("symmetric: true");
		stringBuilder.AppendLine("spd: true");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder2);
		handler.AppendLiteral("mode: ");
		handler.AppendFormatted(mode);
		stringBuilder12.AppendLine(ref handler);
		File.WriteAllText(stemPath + ".meta.txt", stringBuilder.ToString());
	}

	public static void WriteMatrix(string path, SparseMatrix<double> matrix)
	{
		using FileStream stream = File.Create(path);
		using ZLibStream output = new ZLibStream(stream, CompressionLevel.SmallestSize);
		using BinaryWriter writer = new BinaryWriter(output);
		matrix.StreamOut(writer);
	}

	public static void WriteVector(string path, double[] vector)
	{
		using FileStream stream = File.Create(path);
		using ZLibStream output = new ZLibStream(stream, CompressionLevel.SmallestSize);
		using BinaryWriter bw = new BinaryWriter(output);
		bw.WriteBuffer(vector.AsMemory());
	}

	public static SparseMatrix<double> ReadMatrix(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using ZLibStream input = new ZLibStream(stream, CompressionMode.Decompress);
		using BinaryReader reader = new BinaryReader(input);
		SparseMatrix<double> sparseMatrix = new SparseMatrix<double>();
		sparseMatrix.StreamIn(reader);
		return sparseMatrix;
	}

	public static double[] ReadVector(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using ZLibStream input = new ZLibStream(stream, CompressionMode.Decompress);
		using BinaryReader br = new BinaryReader(input);
		return br.ReadBuffer<double>();
	}
}
