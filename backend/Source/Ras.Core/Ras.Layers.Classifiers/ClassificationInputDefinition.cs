using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Geospatial.IO.ORM;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.ORM;
using Utility.Reflection;

namespace Ras.Layers.Classifiers;

public class ClassificationInputDefinition
{
	private struct ClassifierTypeDef(Type t, Func<ClassificationLayer, H5Reader, string, IClassificationInput> gen)
	{
		public Type Type = t;

		public Func<ClassificationLayer, H5Reader, string, IClassificationInput> Generate = gen;
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ClassificationInputDefinition");

	private static readonly object _typeLoadLock = new object();

	private static Dictionary<string, ClassifierTypeDef> _classifierInputTypes;

	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Type")]
	public string Type { get; set; }

	[Metadata("Location")]
	public string Location { get; set; }

	private static void RegisterOnce()
	{
		LazyThreadsafe<Dictionary<string, ClassifierTypeDef>>.AllocateOnce(ref _classifierInputTypes, _typeLoadLock, () =>
		{
			List<Type> allImplementationsOf = ReflectionHelpers.FindAllImplementationsOf<IClassificationInput>(AssemblyExcludeCriteria.AllSystemAndMicrosoft);
			Dictionary<string, ClassifierTypeDef> classifierTypeDefByString = new Dictionary<string, ClassifierTypeDef>();
			foreach (Type allImplementationsOf2 in allImplementationsOf)
			{
				if (!allImplementationsOf2.IsAbstract)
				{
					try
					{
						ConstructorInfo emptyCtor = allImplementationsOf2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, System.Type.EmptyTypes);
						if (emptyCtor == null)
						{
							_logger.LogWarning("No empty constructor for reflection-loading on {TypeName}", allImplementationsOf2.FullName);
						}
						else if (!(emptyCtor.Invoke(null) is IClassificationInput { TypeName: var typeName }))
						{
							_logger.LogWarning("Error invoking empty constructor on {TypeName}", allImplementationsOf2.FullName);
						}
						else if (string.IsNullOrEmpty(typeName))
						{
							_logger.LogWarning("Caller returned empty TypeName: {TypeName}", allImplementationsOf2.FullName);
						}
						else
						{
							Func<ClassificationLayer, H5Reader, string, IClassificationInput> gen = (ClassificationLayer cl, H5Reader hr, string path) =>
							{
								try
								{
									IClassificationInput classificationInput2 = emptyCtor.Invoke(null) as IClassificationInput;
									if (classificationInput2.TryInit(cl, hr, path))
									{
										return classificationInput2;
									}
									return (IClassificationInput)null;
								}
								catch (Exception exception2)
								{
									_logger.LogWarning(exception2, "Runtime exception executing TryInit");
									return (IClassificationInput)null;
								}
							};
							classifierTypeDefByString[typeName] = new ClassifierTypeDef(allImplementationsOf2, gen);
						}
					}
					catch (Exception exception)
					{
						_logger.LogWarning(exception, "Reflection failure for IClassificationInput, {TypeName}", allImplementationsOf2.FullName);
					}
				}
			}
			return classifierTypeDefByString;
		});
	}

	public static List<ClassificationInputDefinition> LoadLayers(H5Reader hr, string dset)
	{
		return FeatureIO.ReadColumns<ClassificationInputDefinition>(hr, dset);
	}

	public IClassificationInput MakeInput(ClassificationLayer tr, H5Reader hr)
	{
		RegisterOnce();
		if (!_classifierInputTypes.TryGetValue(Type, out var localValue))
		{
			return null;
		}
		return localValue.Generate(tr, hr, Location);
	}

	public static ClassificationInputDefinition[] FromInputs(string rootDir, IEnumerable<IClassificationInput> inputs)
	{
		ClassificationInputDefinition[] classificationInputDefinitions = new ClassificationInputDefinition[inputs.Count()];
		HashSet<string> locations = new HashSet<string>(StringComparer.Ordinal);
		int ordinal = 0;
		foreach (IClassificationInput input in inputs)
		{
			string sanitizeObjectName = H5Reader.SanitizeObjectName(input.Name);
			if (string.IsNullOrEmpty(sanitizeObjectName) || !locations.Add(sanitizeObjectName))
				throw new InvalidOperationException("Classification input names must map to distinct HDF groups.");
			classificationInputDefinitions[ordinal++] = new ClassificationInputDefinition
			{
				Location = H5Reader.PathCombine(rootDir, sanitizeObjectName),
				Name = input.Name,
				Type = input.TypeName
			};
		}
		return classificationInputDefinitions;
	}
}
