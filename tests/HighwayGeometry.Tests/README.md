Run from the repository root:

```powershell
dotnet run --project tests/HighwayGeometry.Tests/HighwayGeometry.Tests.csproj
```

The .NET 6 console suite links the production `HighwayGeometry.cs` directly and
needs no Unity or game DLLs. It checks pixel and percentage positioning, line
edges, tiny viewports, 4320px height, 4K/6K/8K cell gaps, adjustable thickness,
nonfinite values, and sizes from 480p through 8K.

`percent` is a 0..1 fraction measured from the top. Invalid dimensions become
zero; invalid positions center the line; invalid line thickness uses 2px;
invalid note thickness uses 6px; invalid gaps become zero. Draw line thickness
clipped to the available viewport height to match the geometry calculation.
