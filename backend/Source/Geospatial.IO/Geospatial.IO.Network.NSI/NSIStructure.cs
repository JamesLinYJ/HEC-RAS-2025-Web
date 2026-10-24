using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Utility.Memory;

namespace Geospatial.IO.Network.NSI;

[DebuggerDisplay("{OccType} | FdId={FdId}")]
public class NSIStructure
{
	[JsonPropertyName("fd_id")]
	public long FdId { get; set; }

	[JsonPropertyName("bid")]
	public string Bid { get; set; }

	[JsonPropertyName("occtype")]
	public string OccType { get; set; }

	[JsonPropertyName("st_damcat")]
	public string StDamCat { get; set; }

	[JsonPropertyName("bldgtype")]
	public string BldgType { get; set; }

	[JsonPropertyName("found_type")]
	public string FoundType { get; set; }

	[JsonPropertyName("cbfips")]
	public string CbFips { get; set; }

	[JsonPropertyName("pop2amu65")]
	public int Pop2Amu65 { get; set; }

	[JsonPropertyName("pop2amo65")]
	public int Pop2Amo65 { get; set; }

	[JsonPropertyName("pop2pmu65")]
	public int Pop2Pmu65 { get; set; }

	[JsonPropertyName("pop2pmo65")]
	public int Pop2Pmo65 { get; set; }

	[JsonPropertyName("sqft")]
	public double SqFt { get; set; }

	[JsonPropertyName("num_story")]
	public int NumStory { get; set; }

	[JsonPropertyName("ftprntid")]
	public string FtPrntId { get; set; }

	[JsonPropertyName("ftprntsrc")]
	public string FtPrntSrc { get; set; }

	[JsonPropertyName("students")]
	public int Students { get; set; }

	[JsonPropertyName("found_ht")]
	public double FoundHt { get; set; }

	[JsonPropertyName("val_struct")]
	public double ValStruct { get; set; }

	[JsonPropertyName("val_cont")]
	public double ValCont { get; set; }

	[JsonPropertyName("val_vehic")]
	public double ValVehic { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("med_yr_blt")]
	public int MedYrBlt { get; set; }

	[JsonPropertyName("firmzone")]
	public string FirmZone { get; set; }

	[JsonPropertyName("o65disable")]
	public double O65Disable { get; set; }

	[JsonPropertyName("u65disable")]
	public double U65Disable { get; set; }

	[JsonPropertyName("x")]
	public double X { get; set; }

	[JsonPropertyName("y")]
	public double Y { get; set; }

	[JsonPropertyName("ground_elv")]
	public double GroundElv { get; set; }

	[JsonPropertyName("ground_elv_m")]
	public double GroundElvM { get; set; }

	public static Table BuildAttributeTable(IReadOnlyCollection<NSIStructure> structures)
	{
		PropertyInfo[] propertyInfos = (from p in typeof(NSIStructure).GetProperties(BindingFlags.Instance | BindingFlags.Public)
			where p.GetCustomAttribute<JsonPropertyNameAttribute>() != null
			select p).ToArray();
		Table table = new Table();
		PropertyInfo[] propertyInfosArray = propertyInfos;
		foreach (PropertyInfo propertyInfo in propertyInfosArray)
		{
			table.AddColumn(propertyInfo.Name, propertyInfo.PropertyType);
		}
		foreach (NSIStructure structure in structures)
		{
			object[] objectValues = new object[propertyInfos.Length];
			for (int intervalIndex = 0; intervalIndex < propertyInfos.Length; intervalIndex++)
			{
				objectValues[intervalIndex] = propertyInfos[intervalIndex].GetValue(structure) ?? "";
			}
			table.AddNewRow(objectValues);
		}
		return table;
	}
}
