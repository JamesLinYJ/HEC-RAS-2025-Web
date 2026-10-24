using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using H5Assist.Identifiers;

namespace H5Assist;

public static class H5Hasher
{
	private sealed class DatasetHasher : IDisposable
	{
		private const int Xx3HashSize = 8;

		private readonly H5HashAlgorithm _algorithm;

		private readonly IncrementalHash _incremental;

		private readonly List<byte> _rashBlockHashes;

		public DatasetHasher(H5HashAlgorithm algorithm)
		{
			_algorithm = algorithm;
			switch (algorithm)
			{
			case H5HashAlgorithm.SHA256:
				_incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
				break;
			case H5HashAlgorithm.MD5:
				_incremental = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
				break;
			case H5HashAlgorithm.RashXX3:
				_rashBlockHashes = new List<byte>();
				break;
			default:
				throw new ArgumentOutOfRangeException("algorithm");
			}
		}

		public void AppendBlock(ReadOnlySpan<byte> block)
		{
			if (_algorithm == H5HashAlgorithm.RashXX3)
			{
				Span<byte> byteSpan = stackalloc byte[8];
				XxHash3.Hash(block, byteSpan, 0L);
				_rashBlockHashes.AddRange(byteSpan);
			}
			else
			{
				_incremental.AppendData(block);
			}
		}

		public byte[] GetHashAndReset()
		{
			if (_algorithm == H5HashAlgorithm.RashXX3)
			{
				byte[] byteValues = new byte[8];
				XxHash3.Hash(CollectionsMarshal.AsSpan(_rashBlockHashes), byteValues, 0L);
				_rashBlockHashes.Clear();
				return byteValues;
			}
			return _incremental.GetHashAndReset();
		}

		public void Dispose()
		{
			_incremental?.Dispose();
		}
	}

	public static string HashDataset(H5Reader reader, string datasetPath, H5HashAlgorithm algorithm = H5HashAlgorithm.SHA256, bool compressed = true)
	{
		int rows;
		int cols;
		return HashDataset(reader, datasetPath, out rows, out cols, algorithm, compressed);
	}

	public static string HashDataset(H5Reader reader, string datasetPath, out int rows, out int cols, H5HashAlgorithm algorithm = H5HashAlgorithm.SHA256, bool compressed = true)
	{
		if (!reader.ExistsAsDataset(datasetPath))
		{
			throw new FileNotFoundException("Dataset not found: " + datasetPath);
		}
		long[] datasetDimensions = reader.GetDatasetDimensions(datasetPath);
		rows = (int)((datasetDimensions.Length != 0) ? datasetDimensions[0] : 0);
		cols = (int)((datasetDimensions.Length <= 1) ? 1 : datasetDimensions[1]);
		using H5DatasetID dataset = reader.OpenDataset(datasetPath);
		return Convert.ToHexString(compressed ? HashCompressedChunks(reader, dataset, algorithm) : HashDecompressedBytes(reader, dataset, algorithm));
	}

	private static byte[] HashCompressedChunks(H5Reader reader, H5DatasetID dataset, H5HashAlgorithm algorithm)
	{
		long[] cachedChunkSize = reader.GetCachedChunkSize(dataset);
		if (cachedChunkSize.Length == 0)
		{
			return HashDecompressedBytes(reader, dataset, algorithm);
		}
		long[] datasetDimensions = reader.GetDatasetDimensions(dataset);
		int datasetDimensionLength = datasetDimensions.Length;
		long[] longs = new long[datasetDimensionLength];
		for (int i = 0; i < datasetDimensionLength; i++)
		{
			longs[i] = (long)Math.Ceiling((double)datasetDimensions[i] / (double)cachedChunkSize[i]);
		}
		DatasetHasher hasher = new DatasetHasher(algorithm);
		try
		{
			int[] chunkIndex = new int[datasetDimensionLength];
			do
			{
				reader.ReadCompressedChunk(dataset, chunkIndex, (ReadOnlyMemory<byte> mem) =>
				{
					hasher.AppendBlock(mem.Span);
				});
			}
			while (IncrementChunkIndex(chunkIndex, longs));
			return hasher.GetHashAndReset();
		}
		finally
		{
			if (hasher != null)
			{
				((IDisposable)hasher).Dispose();
			}
		}
	}

	private static byte[] HashDecompressedBytes(H5Reader reader, H5DatasetID dataset, H5HashAlgorithm algorithm)
	{
		DatasetHasher hasher = new DatasetHasher(algorithm);
		try
		{
			reader.StreamDatasetBytes(dataset, (ReadOnlyMemory<byte> mem) =>
			{
				hasher.AppendBlock(mem.Span);
			});
			return hasher.GetHashAndReset();
		}
		finally
		{
			if (hasher != null)
			{
				((IDisposable)hasher).Dispose();
			}
		}
	}

	private static bool IncrementChunkIndex(int[] chunkIndex, long[] numChunksPerDim)
	{
		for (int lengthOffset = chunkIndex.Length - 1; lengthOffset >= 0; lengthOffset--)
		{
			if (chunkIndex[lengthOffset] + 1 < numChunksPerDim[lengthOffset])
			{
				chunkIndex[lengthOffset]++;
				return true;
			}
			chunkIndex[lengthOffset] = 0;
		}
		return false;
	}
}
