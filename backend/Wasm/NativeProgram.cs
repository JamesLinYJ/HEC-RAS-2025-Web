using System;
using System.Threading.Tasks;
using Ras.Wasm;

string requestJson = args.Length > 0 ? args[0] : "{}";
Console.WriteLine(await EngineExports.RunCompute(requestJson));
