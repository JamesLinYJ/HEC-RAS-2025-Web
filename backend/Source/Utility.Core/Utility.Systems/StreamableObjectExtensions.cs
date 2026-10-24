using System;
using System.Collections.Generic;
using System.IO;

namespace Utility.Systems;

public static class StreamableObjectExtensions
{
	public static void StreamCollectionOut<T>(this BinaryWriter bw, IList<T> elements) where T : IStreamableObject
	{
		if (elements == null)
		{
			bw.Write(0);
			return;
		}
		bw.Write(elements.Count);
		for (int i = 0; i < elements.Count; i++)
		{
			elements[i].StreamOut(bw);
		}
	}

	public static T[] StreamCollectionIn<T>(this BinaryReader br) where T : IStreamableObject
	{
		int readInt32 = br.ReadInt32();
		if (readInt32 == 0)
		{
			return null;
		}
		T[] values = new T[readInt32];
		for (int i = 0; i < readInt32; i++)
		{
			T instance = Activator.CreateInstance<T>();
			instance.StreamIn(br);
			values[i] = instance;
		}
		return values;
	}

	public static void StreamOutToFile(this IStreamableObject iStreamableObject, string filename)
	{
		if (File.Exists(filename))
		{
			File.Delete(filename);
		}
		using FileStream output = new FileStream(filename, FileMode.Create, FileAccess.Write);
		using BinaryWriter bw = new BinaryWriter(output);
		iStreamableObject.StreamOut(bw);
	}

	public static void StreamInFromFile(this IStreamableObject iStreamableObject, string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		using FileStream input = new FileStream(filename, FileMode.Open, FileAccess.Read);
		using BinaryReader br = new BinaryReader(input);
		iStreamableObject.StreamIn(br);
	}
}
