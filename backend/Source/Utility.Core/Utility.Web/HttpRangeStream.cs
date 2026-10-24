using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Utility.Memory;

namespace Utility.Web;

public class HttpRangeStream : Stream
{
	private readonly HttpClient _client;

	private readonly string _url;

	private readonly int _bufferSize;

	private long _position;

	private readonly long _length;

	private byte[] _buffer;

	private int _bufferOffset;

	private int _bufferLength;

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => false;

	public override long Length
	{
		get
		{
			if (_length < 0)
			{
				throw new NotSupportedException("Length is not supported.");
			}
			return _length;
		}
	}

	public override long Position
	{
		get
		{
			return _position;
		}
		set
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException("value");
			}
			_position = value;
			_buffer = null;
			_bufferOffset = 0;
			_bufferLength = 0;
		}
	}

	private HttpRangeStream(HttpClient client, string url, int bufferSize, long length)
	{
		_client = client ?? throw new ArgumentNullException("client");
		_url = url ?? throw new ArgumentNullException("url");
		_bufferSize = bufferSize;
		_length = length;
		_position = 0L;
		_buffer = null;
		_bufferOffset = 0;
		_bufferLength = 0;
	}

	public static async Task<HttpRangeStream> CreateAsync(HttpClient client, string url, int bufferSize = 4096)
	{
		if (client == null)
		{
			throw new ArgumentNullException("client");
		}
		if (url == null)
		{
			throw new ArgumentNullException("url");
		}
		return new HttpRangeStream(client, url, bufferSize, await GetContentLengthAsync(client, url));
	}

	public static async Task<(Pool<Stream>, long)> CreatePoolAsync(HttpClient client, string url, int bufferSize = 4096)
	{
		if (client == null)
		{
			throw new ArgumentNullException("client");
		}
		if (url == null)
		{
			throw new ArgumentNullException("url");
		}
		long length = await GetContentLengthAsync(client, url);
		return (new Pool<Stream>(() => new HttpRangeStream(client, url, bufferSize, length)), length);
	}

	private static async Task<long> GetContentLengthAsync(HttpClient client, string url)
	{
		using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url))
		{
			HttpResponseMessage httpResponseMessage = await client.SendAsync(request);
			httpResponseMessage.EnsureSuccessStatusCode();
			if (httpResponseMessage.Content.Headers.ContentLength.HasValue)
			{
				return httpResponseMessage.Content.Headers.ContentLength.Value;
			}
		}
		return -1L;
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		if (buffer == null)
		{
			throw new ArgumentNullException("buffer");
		}
		if (offset < 0 || count < 0 || offset + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException();
		}
		int ordinal = 0;
		while (count > 0 && ((_buffer != null && _bufferOffset < _bufferLength) || LoadBufferAsync(_position, _bufferSize).GetAwaiter().GetResult()))
		{
			int bufferLengthAboveBufferOffset = _bufferLength - _bufferOffset;
			int countTerm = Math.Min(count, bufferLengthAboveBufferOffset);
			Array.Copy(_buffer, _bufferOffset, buffer, offset, countTerm);
			_bufferOffset += countTerm;
			offset += countTerm;
			count -= countTerm;
			ordinal += countTerm;
			_position += countTerm;
		}
		return ordinal;
	}

	private async Task<bool> LoadBufferAsync(long start, int size)
	{
		long startSizeTerm = start + size - 1;
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _url);
		request.Headers.Range = new RangeHeaderValue(start, startSizeTerm);
		HttpResponseMessage httpResponseMessage = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			return false;
		}
		Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync();
		_buffer = new byte[size];
		_bufferOffset = 0;
		_bufferLength = 0;
		int bytesRead;
		int readAsync;
		for (bytesRead = 0; bytesRead < size; bytesRead += readAsync)
		{
			readAsync = await stream.ReadAsync(_buffer, bytesRead, size - bytesRead);
			if (readAsync == 0)
			{
				break;
			}
		}
		_bufferLength = bytesRead;
		return _bufferLength > 0;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		long position = _position;
		switch (origin)
		{
		case SeekOrigin.Begin:
			position = offset;
			break;
		case SeekOrigin.Current:
			position = _position + offset;
			break;
		case SeekOrigin.End:
			if (_length < 0)
			{
				throw new NotSupportedException("Cannot seek from end when length is unknown.");
			}
			position = _length + offset;
			break;
		}
		if (position < 0)
		{
			throw new IOException("Cannot seek to a negative position.");
		}
		Position = position;
		return position;
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Flush()
	{
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException("This stream is read-only.");
	}
}
