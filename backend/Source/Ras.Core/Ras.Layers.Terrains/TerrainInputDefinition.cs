using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.ORM;
using Utility.Reflection;

namespace Ras.Layers.Terrains;

[DebuggerDisplay("{Name}: {Type}")]
public class TerrainInputDefinition
{
	private struct TerrainTypeDef(Type t, Func<Terrain, H5Reader, string, ITerrainInput> gen)
	{
		public Type Type = t;

		public Func<Terrain, H5Reader, string, ITerrainInput> Generate = gen;
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("TerrainInputDefinition");

	private static readonly object _typeLoadLock = new object();

	private static Dictionary<string, TerrainTypeDef> _terrainInputTypes;

	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Type")]
	public string Type { get; set; }

	[Metadata("Location")]
	public string Location { get; set; }

	private static void RegisterOnce()
	{
		LazyThreadsafe<Dictionary<string, TerrainTypeDef>>.AllocateOnce(ref _terrainInputTypes, _typeLoadLock, () =>
		{
			List<Type> allImplementationsOf = ReflectionHelpers.FindAllImplementationsOf<ITerrainInput>(AssemblyExcludeCriteria.AllSystemAndMicrosoft);
			Dictionary<string, TerrainTypeDef> terrainTypeDefByString = new Dictionary<string, TerrainTypeDef>();
			foreach (Type allImplementationsOf2 in allImplementationsOf)
			{
				if (!allImplementationsOf2.IsAbstract
					&& !typeof(INonPersistableTerrainInput).IsAssignableFrom(allImplementationsOf2))
				{
					try
					{
						ConstructorInfo emptyCtor = allImplementationsOf2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, System.Type.EmptyTypes);
						if (emptyCtor == null)
						{
							_logger.LogWarning("No empty constructor for reflection-loading on {TypeName}", allImplementationsOf2.FullName);
						}
						else if (!(emptyCtor.Invoke(null) is ITerrainInput { TypeName: var typeName }))
						{
							_logger.LogWarning("Error invoking empty constructor on {TypeName}", allImplementationsOf2.FullName);
						}
						else if (string.IsNullOrEmpty(typeName))
						{
							_logger.LogWarning("Caller returned empty TypeName: {TypeName}", allImplementationsOf2.FullName);
						}
						else
						{
							Func<Terrain, H5Reader, string, ITerrainInput> gen = (Terrain tr, H5Reader hr, string path) =>
							{
								try
								{
									ITerrainInput terrainInput2 = emptyCtor.Invoke(null) as ITerrainInput;
									if (terrainInput2.TryInit(tr, hr, path))
									{
										return terrainInput2;
									}
									return (ITerrainInput)null;
								}
								catch (Exception exception2)
								{
									_logger.LogWarning(exception2, "Runtime exception executing TryInit");
									return (ITerrainInput)null;
								}
							};
							terrainTypeDefByString[typeName] = new TerrainTypeDef(allImplementationsOf2, gen);
						}
					}
					catch (Exception exception)
					{
						_logger.LogWarning(exception, "Reflection failure for ITerrainInput, {TypeName}", allImplementationsOf2.FullName);
					}
				}
			}
			return terrainTypeDefByString;
		});
	}

	public static List<TerrainInputDefinition> LoadLayers(H5Reader hr, string dset)
	{
		return FeatureIO.ReadColumns<TerrainInputDefinition>(hr, dset);
	}

	internal static bool IsTypeRegisteredForLoad(string typeName)
	{
		RegisterOnce();
		return _terrainInputTypes.ContainsKey(typeName);
	}

	public ITerrainInput MakeInput(Terrain tr, H5Reader hr)
	{
		RegisterOnce();
		if (!_terrainInputTypes.TryGetValue(Type, out var localValue))
		{
			return null;
		}
		return localValue.Generate(tr, hr, Location);
	}

	public static TerrainInputDefinition[] FromInputs(string rootDir, IEnumerable<ITerrainInput> inputs)
	{
		ITerrainInput[] inputArray = inputs.ToArray();
		foreach (ITerrainInput input in inputArray)
		{
			if (input is INonPersistableTerrainInput)
			{
				throw new InvalidOperationException(
					$"Terrain input type '{input.GetType().FullName}' cannot be written to an HDF terrain definition.");
			}
		}
		TerrainInputDefinition[] terrainInputDefinitions = new TerrainInputDefinition[inputArray.Length];
		int ordinal = 0;
		foreach (ITerrainInput input in inputArray)
		{
			if (input is DebugTerrainInput)
			{
				terrainInputDefinitions[ordinal++] = new TerrainInputDefinition
				{
					Name = "DEBUG",
					Type = "Debug Terrain Input",
					Location = "DNE_ERROR"
				};
			}
			else
			{
				string sanitizeObjectName = H5Reader.SanitizeObjectName(input.Name);
				terrainInputDefinitions[ordinal++] = new TerrainInputDefinition
				{
					Location = H5Reader.PathCombine(rootDir, sanitizeObjectName),
					Name = input.Name,
					Type = input.TypeName
				};
			}
		}
		return terrainInputDefinitions;
	}

	public static bool CheckValidGroup(Dictionary<string, object> attributes)
	{
		if (attributes.TryGetValue("Type", out var localValue))
		{
			string textValue = localValue.ToString();
			if (textValue == "Polygon" || textValue == "Polyline")
			{
				return false;
			}
		}
		return true;
	}
}
