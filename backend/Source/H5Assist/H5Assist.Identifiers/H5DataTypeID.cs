using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HDF.PInvoke;

namespace H5Assist.Identifiers;

public class H5DataTypeID : IDisposable
{
	private const long NULLID = long.MinValue;

	protected static Dictionary<Type, long> _typeDictionary;

	public static Dictionary<Type, long> BuiltinTypeMap;

	public long TypeId = long.MinValue;

	public CompoundTypeDefinition CompoundType;

	static H5DataTypeID()
	{
		_typeDictionary = new Dictionary<Type, long>();
		_typeDictionary.Add(typeof(float), H5T.copy(H5T.IEEE_F32LE));
		_typeDictionary.Add(typeof(double), H5T.copy(H5T.IEEE_F64LE));
		_typeDictionary.Add(typeof(short), H5T.copy(H5T.NATIVE_SHORT));
		_typeDictionary.Add(typeof(int), H5T.copy(H5T.NATIVE_INT));
		_typeDictionary.Add(typeof(long), H5T.copy(H5T.NATIVE_LLONG));
		_typeDictionary.Add(typeof(byte), H5T.copy(H5T.NATIVE_B8));
		_typeDictionary.Add(typeof(bool), H5T.copy(H5T.NATIVE_HBOOL));
		_typeDictionary.Add(typeof(string), H5T.copy(H5T.C_S1));
		BuiltinTypeMap = new Dictionary<Type, long>
		{
			{
				typeof(float),
				H5T.IEEE_F32LE
			},
			{
				typeof(double),
				H5T.IEEE_F64LE
			},
			{
				typeof(short),
				H5T.NATIVE_SHORT
			},
			{
				typeof(int),
				H5T.NATIVE_INT
			},
			{
				typeof(long),
				H5T.NATIVE_LLONG
			},
			{
				typeof(byte),
				H5T.NATIVE_B8
			},
			{
				typeof(bool),
				H5T.NATIVE_HBOOL
			}
		};
	}

	public static long TryPeekTypeID<T>()
	{
		if (_typeDictionary.TryGetValue(typeof(T), out var localValue))
		{
			return localValue;
		}
		return -1L;
	}

	public static bool TypeMatch<T>(long typeId)
	{
		Type typeFromHandle = typeof(T);
		if (!_typeDictionary.ContainsKey(typeFromHandle))
		{
			throw new Exception("Type " + typeFromHandle.ToString() + " not supported.");
		}
		long type_id = _typeDictionary[typeFromHandle];
		if (H5T.equal(typeId, type_id) > 0)
		{
			return true;
		}
		if (typeof(T) == typeof(string))
		{
			return true;
		}
		return false;
	}

	public static Type TypeConvert(long typeId, int ndims)
	{
		foreach (KeyValuePair<Type, long> typeDictionary in _typeDictionary)
		{
			if (H5T.equal(typeId, typeDictionary.Value) > 0)
			{
				if (ndims == 0)
				{
					return typeDictionary.Key;
				}
				return typeDictionary.Key.MakeArrayType(ndims);
			}
		}
		if (H5T.get_class(typeId) == H5T.class_t.COMPOUND)
		{
			return typeof(CompoundFieldDefinition);
		}
		_ = _typeDictionary[typeof(string)];
		if (ndims == 0)
		{
			return typeof(string);
		}
		return typeof(string).MakeArrayType(ndims);
	}

	public static Type TypeConvert(H5DataTypeID typeId, int ndims)
	{
		return TypeConvert(typeId.TypeId, ndims);
	}

	public static Type TypeConvert(H5DatasetID datasetId, int ndims)
	{
		return TypeConvert(datasetId.DataType.TypeId, ndims);
	}

	public static H5DataTypeID Open(H5DatasetID d)
	{
		return new H5DataTypeID(H5D.get_type(d.DatasetId));
	}

	public static H5DataTypeID Open(H5AttributeID a)
	{
		return new H5DataTypeID(H5A.get_type(a.AttributeId));
	}

	public static H5DataTypeID CreateCompound(CompoundTypeDefinition compoundType)
	{
		int sizeSum = 0;
		foreach (CompoundFieldDefinition field in compoundType.Fields)
		{
			sizeSum += field.Size;
		}
		long create = H5T.create(H5T.class_t.COMPOUND, sizeSum);
		int sizeSum2 = 0;
		foreach (CompoundFieldDefinition field2 in compoundType.Fields)
		{
			long h5TTypeDictionaryTerm = H5T.copy(_typeDictionary[field2.Type]);
			if (field2.Type == typeof(string))
			{
				H5T.set_size(h5TTypeDictionaryTerm, field2.Size);
				H5T.set_cset(h5TTypeDictionaryTerm, H5T.cset_t.UTF8);
			}
			H5T.insert(create, field2.Name, sizeSum2, h5TTypeDictionaryTerm);
			sizeSum2 += field2.Size;
			H5T.close(h5TTypeDictionaryTerm);
		}
		return new H5DataTypeID(create, compoundType);
	}

	public H5DataTypeID(Type t, int setSize = 0)
	{
		TypeId = H5T.copy(_typeDictionary[t]);
		if (t == typeof(string)) H5T.set_cset(TypeId, H5T.cset_t.UTF8);
		if (setSize != 0)
		{
			H5T.set_size(TypeId, setSize);
		}
	}

	protected H5DataTypeID(long typeID)
	{
		TypeId = typeID;
	}

	protected H5DataTypeID(long typeID, CompoundTypeDefinition compoundType)
	{
		TypeId = typeID;
		CompoundType = compoundType;
	}

	public static H5DataTypeID CreateVLen<T>()
	{
		return new H5DataTypeID(H5T.vlen_create(TryPeekTypeID<T>()));
	}

	public int GetSize()
	{
		return (int)H5T.get_size(TypeId);
	}

	public bool TypeMatches<T>()
	{
		return TypeMatch<T>(TypeId);
	}

	public CompoundTypeDefinition GetCompoundDataType()
	{
		if (CompoundType != null)
		{
			return CompoundType;
		}
		int get_nmembers = H5T.get_nmembers(TypeId);
		CompoundType = new CompoundTypeDefinition();
		for (int i = 0; i < get_nmembers; i++)
		{
			string name = Marshal.PtrToStringAnsi(H5T.get_member_name(TypeId, (uint)i));
			int offset = (int)H5T.get_member_offset(TypeId, (uint)i);
			long get_member_type = H5T.get_member_type(TypeId, (uint)i);
			int size = (int)H5T.get_size(get_member_type);
			Type t = TypeConvert(get_member_type, 0);
			CompoundType.Fields.Add(new CompoundFieldDefinition(t, name, size, offset));
			H5T.close(get_member_type);
		}
		CompoundType.SortFields();
		return CompoundType;
	}

	public void Dispose()
	{
		H5T.close(TypeId);
	}
}
