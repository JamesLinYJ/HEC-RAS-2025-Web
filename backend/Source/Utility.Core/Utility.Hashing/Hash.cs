using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.Attributes;
using Utility.Logging;

namespace Utility.Hashing;

public class Hash
{
	private static readonly ILogger _logger = LogHelper.GetLogger("Hash");

	private List<byte[]> _buffers;

	public Hash()
	{
		_buffers = new List<byte[]>();
	}

	public static bool HashEquals(byte[] hash1, byte[] hash2)
	{
		if (hash1 == null || hash2 == null)
		{
			return false;
		}
		if (hash1.Length != hash2.Length)
		{
			return false;
		}
		for (int i = 0; i < hash1.Length; i++)
		{
			if (hash1[i] != hash2[i])
			{
				return false;
			}
		}
		return true;
	}

	public static int Combine(params object[] hashInputs)
	{
		int hashInputsTerm = 17;
		for (int i = 0; i < hashInputs.Length; i++)
		{
			hashInputsTerm = hashInputsTerm * 31 + hashInputs[i].GetHashCode();
		}
		return hashInputsTerm;
	}

	public static int Combine<T>(T[] hashInputs)
	{
		int hashInputsTerm = 17;
		for (int i = 0; i < hashInputs.Length; i++)
		{
			hashInputsTerm = hashInputsTerm * 31 + hashInputs[i].GetHashCode();
		}
		return hashInputsTerm;
	}

	public void Add<T>(T[] buf) where T : struct
	{
		byte[] byteValues = new byte[buf.Length * Marshal.SizeOf(new T())];
		Buffer.BlockCopy(buf, 0, byteValues, 0, byteValues.Length);
		_buffers.Add(byteValues);
	}

	public void Add(string stringValue)
	{
		_buffers.Add(Encoding.ASCII.GetBytes(stringValue));
	}

	public void Clear()
	{
		_buffers.Clear();
	}

	public byte[] HashBytes()
	{
		MemoryStream memoryStream = new MemoryStream();
		foreach (byte[] buffer in _buffers)
		{
			memoryStream.Write(buffer, 0, buffer.Length);
		}
		return SHA256.Create().ComputeHash(memoryStream.GetBuffer());
	}

	public string HashString()
	{
		byte[] bytes = HashBytes();
		return Encoding.ASCII.GetString(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Report(PropertyInfo member, int resultHash)
	{
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ReportStart(Type objectType)
	{
	}

	public static int GetDefaultDataHash(IDataHash el)
	{
		Type type = el.GetType();
		List<MemberInfo> membersWithAttribute = type.GetMembersWithAttribute(typeof(HashContributorAttribute), true);
		ReportStart(type);
		int combine = 0;
		foreach (MemberInfo membersWithAttribute2 in membersWithAttribute)
		{
			PropertyInfo propertyInfo = membersWithAttribute2 as PropertyInfo;
			object getValue = propertyInfo.GetValue(el);
			if (getValue != null)
			{
				if (getValue is IDataHash dataHash)
				{
					combine = StableHash.Combine(combine, dataHash.GetDataHash());
				}
				else if (getValue is IEnumerable enumerable && !(propertyInfo.PropertyType == typeof(string)))
				{
					if (enumerable == null)
					{
						continue;
					}
					Type type2 = enumerable.GetType();
					bool isPrimitive = false;
					if (type2.IsArray)
					{
						isPrimitive = type2.GetElementType().IsPrimitive;
					}
					int dataHashFromArrayT = 0;
					if (isPrimitive)
					{
						dataHashFromArrayT = TryGetDataHashFromArrayT(getValue);
					}
					if (dataHashFromArrayT == -1)
					{
						combine = StableHash.Combine(combine, dataHashFromArrayT);
					}
					else
					{
						bool condition = false;
						foreach (object enumerable2 in enumerable)
						{
							if (enumerable2 is IDataHash dataHash2)
							{
								combine = StableHash.Combine(combine, dataHash2.GetDataHash());
							}
							else if (isPrimitive)
							{
								combine = StableHash.Combine(combine, enumerable2.GetHashCode());
							}
							else if (!condition)
							{
								_logger.LogWarning("Cannot hash {Type}, skipping. Other elements in this container will not be reported.", enumerable2.GetType());
								condition = true;
							}
						}
					}
				}
				else if (propertyInfo.PropertyType.IsGenericType && (propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Memory<>) || propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>)))
				{
					if (TryGetDataHashFromMemoryT(getValue, out var hs))
					{
						combine = StableHash.Combine(combine, hs);
					}
					else
					{
						_logger.LogWarning("Cannot hash {PropertyType} on {PropertyName}", propertyInfo.PropertyType, propertyInfo.Name);
					}
				}
				else if (propertyInfo.PropertyType.IsValueType)
				{
					combine = StableHash.Combine(combine, getValue);
				}
				else if (propertyInfo.PropertyType == typeof(string))
				{
					if (string.IsNullOrEmpty(getValue as string))
					{
						continue;
					}
					combine = StableHash.Combine(combine, getValue);
				}
				else
				{
					_logger.LogWarning("Cannot hash {PropertyType} on {PropertyName}", propertyInfo.PropertyType, propertyInfo.Name);
				}
			}
			Report(propertyInfo, combine);
		}
		return combine;
	}

	private static bool TryGetDataHashFromMemoryT(object localValue, out int hs)
	{
		if (localValue is Memory<byte> byteSpan)
		{
			hs = HashMemoryT(byteSpan);
			return true;
		}
		if (localValue is Memory<short> shortSpan)
		{
			hs = HashMemoryT(shortSpan);
			return true;
		}
		if (localValue is Memory<int> intSpan)
		{
			hs = HashMemoryT(intSpan);
			return true;
		}
		if (localValue is Memory<long> longSpan)
		{
			hs = HashMemoryT(longSpan);
			return true;
		}
		if (localValue is Memory<float> floatSpan)
		{
			hs = HashMemoryT(floatSpan);
			return true;
		}
		if (localValue is Memory<double> doubleSpan)
		{
			hs = HashMemoryT(doubleSpan);
			return true;
		}
		hs = -1;
		return false;
	}

	private static int TryGetDataHashFromArrayT(object localValue)
	{
		if (localValue is byte[] byteValues)
		{
			return HashMemoryT(byteValues.AsMemory());
		}
		if (localValue is short[] shortValues)
		{
			return HashMemoryT(shortValues.AsMemory());
		}
		if (localValue is int[] intValues)
		{
			return HashMemoryT(intValues.AsMemory());
		}
		if (localValue is long[] longValues)
		{
			return HashMemoryT(longValues.AsMemory());
		}
		if (localValue is float[] floatValues)
		{
			return HashMemoryT(floatValues.AsMemory());
		}
		if (localValue is double[] doubleValues)
		{
			return HashMemoryT(doubleValues.AsMemory());
		}
		return -1;
	}

	private static int HashMemoryT<T>(Memory<T> buffer) where T : unmanaged
	{
		int combine = 0;
		Span<T> bufferSpan = buffer.Span;
		for (int i = 0; i < bufferSpan.Length; i++)
		{
			combine = StableHash.Combine(combine, bufferSpan[i].GetHashCode());
		}
		return combine;
	}

	public static string DebugCompareHashContributions(IDataHash el)
	{
		StringBuilder stringBuilder = new StringBuilder();
		DebugRecurseHash(el, stringBuilder, "");
		return stringBuilder.ToString();
	}

	private static void DebugRecurseHash(IDataHash el, StringBuilder sb, string prefix)
	{
		Type type = el.GetType();
		foreach (MemberInfo membersWithAttribute in type.GetMembersWithAttribute(typeof(HashContributorAttribute), true))
		{
			PropertyInfo propertyInfo = membersWithAttribute as PropertyInfo;
			object getValue = propertyInfo.GetValue(el);
			if (getValue == null)
			{
				sb.AppendLine(prefix + type.Name + "::" + propertyInfo.Name + " (NULL)");
			}
			else if (getValue is IDataHash el2)
			{
				sb.AppendLine(prefix + type.Name + "::" + propertyInfo.Name + " ->");
				DebugRecurseHash(el2, sb, prefix + "  ");
			}
			else if (propertyInfo.PropertyType.IsValueType || propertyInfo.PropertyType == typeof(string))
			{
				sb.AppendLine(prefix + type.Name + "::" + propertyInfo.Name + "=" + getValue.GetHashCode());
			}
			else
			{
				sb.AppendLine(prefix + type.Name + "::" + propertyInfo.Name + " (SKIPPED - DOES NOT IMPLEMENT IDataHash)");
			}
		}
		if (!(el is IEnumerable<IDataHash> enumerable))
		{
			return;
		}
		if (enumerable.Count() > 0)
		{
			sb.AppendLine(prefix + "Collection[] ->");
			string prefixTerm = prefix + "  ";
			int ordinal = 0;
			{
				foreach (IDataHash enumerable2 in enumerable)
				{
					sb.AppendLine(prefixTerm + enumerable2.GetType().Name + "[" + ordinal + "] ->");
					DebugRecurseHash(enumerable2, sb, prefixTerm + "  ");
					ordinal++;
				}
				return;
			}
		}
		sb.AppendLine(prefix + "Collection[] -> (Empty)");
	}
}
