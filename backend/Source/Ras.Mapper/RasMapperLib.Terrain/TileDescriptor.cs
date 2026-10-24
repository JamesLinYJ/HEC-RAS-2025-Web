using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Terrain;

[DebuggerDisplay("{FileWithoutPath} : {Level} : {TileIdx}")]
public class TileDescriptor
{
	[CompilerGenerated]
	private string _Filename;

	[CompilerGenerated]
	private string _Level;

	[CompilerGenerated]
	private string _TileIdx;

	public string Filename
	{
		[CompilerGenerated]
		get
		{
			return _Filename;
		}
	}

	public string Level
	{
		[CompilerGenerated]
		get
		{
			return _Level;
		}
	}

	public string TileIdx
	{
		[CompilerGenerated]
		get
		{
			return _TileIdx;
		}
	}

	public string FileWithoutPath => Path.GetFileNameWithoutExtension(Filename);

	public TileDescriptor(string filename, int level, int tileidx)
	{
		_Filename = filename;
		_Level = Conversions.ToString(level);
		_TileIdx = Conversions.ToString(tileidx);
	}

	public override int GetHashCode()
	{
		return checked((int)((((17L * 23L + _Filename.GetHashCode()) * 23 + _Level.GetHashCode()) * 23 + _TileIdx.GetHashCode()) & 0x7FFFFFFF));
	}

	public override bool Equals(object obj)
	{
		if (obj == null || (object)GetType() != obj.GetType())
		{
			return false;
		}
		TileDescriptor tileDescriptor = (TileDescriptor)obj;
		return Operators.CompareString(tileDescriptor.Filename, Filename, TextCompare: false) == 0 && Operators.CompareString(tileDescriptor.Level, Level, TextCompare: false) == 0 && Operators.CompareString(tileDescriptor.TileIdx, TileIdx, TextCompare: false) == 0;
	}
}
