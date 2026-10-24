using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Communication;

public class S3BucketStream : Stream
{
	private static readonly ILogger _logger = LogHelper.GetLogger("S3BucketStream");

	private readonly string _bucketName;

	private readonly string _objectName;

	private readonly S3Client _client;

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => false;

	public override long Length { get; }

	public override long Position { get; set; }

	public S3BucketStream(S3Client client, string bktName, string objectName)
	{
		_client = client;
		_bucketName = bktName;
		_objectName = objectName;
		Length = client.GetLength(bktName, objectName);
	}

	private S3BucketStream(S3Client client, string bktName, string objectName, long length)
	{
		_client = client;
		_bucketName = bktName;
		_objectName = objectName;
		Length = length;
	}

	public static async Task<S3BucketStream> CreateAsync(S3Client client, string bktName, string objectName, CancellationToken ct = default(CancellationToken))
	{
		return new S3BucketStream(client, bktName, objectName, await client.GetLengthAsync(bktName, objectName, ct));
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		if (Position + count > Length)
		{
			throw new Exception("Cannot read past end of stream.");
		}
		Memory<byte> buffer2 = buffer.AsMemory(offset, count);
		_client.GetRange(_bucketName, _objectName, Position, buffer2);
		Position += count;
		return count;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default(CancellationToken))
	{
		if (Position + buffer.Length > Length)
		{
			throw new InvalidOperationException("Cannot read past end of stream.");
		}
		await _client.GetRangeAsync(_bucketName, _objectName, Position, buffer);
		Position += buffer.Length;
		return buffer.Length;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default(CancellationToken))
	{
		return ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotImplementedException();
	}

	public override void SetLength(long value)
	{
		throw new NotImplementedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotImplementedException();
	}

	public override void Flush()
	{
		throw new NotImplementedException();
	}
}
