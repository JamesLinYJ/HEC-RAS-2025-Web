using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Utility.Extensions;
using Utility.Files;

namespace Utility.Systems;

public static class StreamableObjectHelper
{
	public const string TargetExtension = ".stream";
	public const int MaximumNamePacketLength = 999;
	public const int MaximumObjectPacketLength = 64 * 1024 * 1024;

	public static bool HasStreamableExtension(string filename)
	{
		return Path.GetExtension(filename).RoughlyEquals(".stream");
	}

	public static async Task WriteAsync(Stream stream, IStreamableObject iStreamableObject)
	{
		using MemoryStream ms = new MemoryStream();
		using BinaryWriter bw = new BinaryWriter(ms);
		string assemblyQualifiedName = iStreamableObject.GetType().AssemblyQualifiedName;
		bw.Write(assemblyQualifiedName.Length);
		bw.Write(Encoding.ASCII.GetBytes(assemblyQualifiedName));
		bw.Write(0);
		int msLength = (int)ms.Length;
		iStreamableObject.StreamOut(bw);
		int lengthAboveMsLength = (int)ms.Length - msLength;
		bw.Seek(msLength - 4, SeekOrigin.Begin);
		bw.Write(lengthAboveMsLength);
		await stream.WriteAsync(ms.GetBuffer().AsMemory(0, (int)ms.Length));
	}

	public static void Write(Stream stream, IStreamableObject iStreamableObject)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		string assemblyQualifiedName = iStreamableObject.GetType().AssemblyQualifiedName;
		binaryWriter.Write(assemblyQualifiedName.Length);
		binaryWriter.Write(Encoding.ASCII.GetBytes(assemblyQualifiedName));
		binaryWriter.Write(0);
		int memoryStreamLength = (int)memoryStream.Length;
		iStreamableObject.StreamOut(binaryWriter);
		int lengthAboveMemoryStreamLength = (int)memoryStream.Length - memoryStreamLength;
		binaryWriter.Seek(memoryStreamLength - 4, SeekOrigin.Begin);
		binaryWriter.Write(lengthAboveMemoryStreamLength);
		stream.Write(memoryStream.GetBuffer().AsMemory(0, (int)memoryStream.Length).Span);
	}

	public static Task<IStreamableObject> ReadAsync(Stream stream)
	{
		return ReadAsync(stream, null, default);
	}

	/// <summary>
	/// Reads one source streamable-object record. When an allowlist is supplied,
	/// the wire type name must match a retained assembly-qualified name exactly;
	/// no type named by the peer is loaded or activated.
	/// </summary>
	public static async Task<IStreamableObject> ReadAsync(Stream stream,
		IReadOnlyDictionary<string, Type> supportedTypes, System.Threading.CancellationToken cancellationToken = default)
	{
		if (stream == null)
		{
			throw new ArgumentNullException(nameof(stream));
		}
		byte[] header = new byte[sizeof(int)];
		if (!await ReadHeaderAsync(stream, header, allowCleanEof: true, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}
		int nameLength = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (nameLength <= 0)
		{
			throw new InvalidDataException("Expected name packet with positive length.");
		}
		if (nameLength > MaximumNamePacketLength)
		{
			throw new InvalidDataException("Exceeded maximum name-packet length.");
		}
		byte[] nameBytes = new byte[nameLength];
		await ReadPacketAsync(stream, nameBytes, cancellationToken).ConfigureAwait(false);
		string name = Encoding.ASCII.GetString(nameBytes);

		Type streamableType;
		if (supportedTypes != null)
		{
			if (!supportedTypes.TryGetValue(name, out streamableType))
			{
				throw new NotSupportedException("The streamable object type is not supported by this importer.");
			}
		}
		else
		{
			streamableType = Type.GetType(name);
			if (streamableType == null)
			{
				return null;
			}
		}

		if (!typeof(IStreamableObject).IsAssignableFrom(streamableType) || streamableType.IsAbstract || streamableType.ContainsGenericParameters)
		{
			throw new InvalidDataException("The stream type does not identify a concrete streamable object.");
		}
		if (!await ReadHeaderAsync(stream, header, allowCleanEof: false, cancellationToken).ConfigureAwait(false))
		{
			throw new EndOfStreamException("The stream ended before the object-packet length header.");
		}
		int objectLength = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (objectLength < 0)
		{
			throw new InvalidDataException("Object packet length cannot be negative.");
		}
		if (objectLength > MaximumObjectPacketLength)
		{
			throw new InvalidDataException("Object packet exceeds the maximum supported length.");
		}
		byte[] data = new byte[objectLength];
		await ReadPacketAsync(stream, data, cancellationToken).ConfigureAwait(false);

		object instance = Activator.CreateInstance(streamableType);
		if (instance is not IStreamableObject streamableObject)
		{
			throw new InvalidDataException("The stream type could not be constructed as a streamable object.");
		}
		using MemoryStream input = new MemoryStream(data, writable: false);
		using BinaryReader br = new BinaryReader(input);
		streamableObject.StreamIn(br);
		if (input.Position != input.Length)
		{
			throw new InvalidDataException("The streamable object did not consume its complete object packet.");
		}
		return streamableObject;
	}

	private static async Task<bool> ReadHeaderAsync(Stream stream, Memory<byte> header, bool allowCleanEof,
		System.Threading.CancellationToken cancellationToken)
	{
		int bytesRead = 0;
		while (bytesRead < header.Length)
		{
			int read = await stream.ReadAsync(header.Slice(bytesRead), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				if (bytesRead == 0 && allowCleanEof)
				{
					return false;
				}
				throw new EndOfStreamException("The stream ended inside a streamable-object header.");
			}
			bytesRead += read;
		}
		return true;
	}

	private static async Task ReadPacketAsync(Stream stream, Memory<byte> buffer,
		System.Threading.CancellationToken cancellationToken)
	{
		int bytesRead = 0;
		while (bytesRead < buffer.Length)
		{
			int read = await stream.ReadAsync(buffer.Slice(bytesRead), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				throw new EndOfStreamException("The stream did not provide the expected packet bytes.");
			}
			bytesRead += read;
		}
	}

	public static void DebugWriteToFile(IStreamableObject iStreamableObject)
	{
		string path = Path.Combine(Path.GetTempPath(), iStreamableObject.GetType().Name + "_" + Guid.NewGuid().ToString().Substring(0, 6) + ".stream");
		using FileStream stream = File.Create(path);
		Write(stream, iStreamableObject);
		FileHelpers.LaunchExplorerAndSelect(path);
	}
}
