# Style catalog checks

Run with the .NET 10 SDK and Python 3:

```sh
dotnet run --project backend/Checks/StyleCatalog/StyleCatalogChecks.csproj --configuration Release
```

These checks use controlled HTTP responses and a fake worker process. They cover response-body deadlines, cancellation, real-sized preview payloads, response limits, independent style scheduling, and the existing serialized foreground lane. They do not establish external OSM availability or native hydraulic-engine behavior.

The React counterpart is `npm run test:osm-style-picker`. Real server and browser validation remains separate from both suites. This check project and its worker are not referenced by the product server.
