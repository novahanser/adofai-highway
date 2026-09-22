# Settings serialization checks

Run from the repository root:

```powershell
dotnet run --project tests/Settings.Tests/Settings.Tests.csproj --configuration Release
```

This dependency-free .NET 6 console project compiles the production `Settings.cs`
directly. A minimal UMM stub only supplies the base class and method signatures;
its `Save` methods throw if called. Tests use the real .NET `XmlSerializer` to
load a representative 0.6.0 XML fixture and round-trip the new settings, including
language, note and hit-line thickness, hit-line positioning, and non-finite XML
float values. Boundary checks cover the expanded scroll-speed and hit-line offset
ranges. Layout checks use the renderer's effective gap and retain an exact
80-pixel 8K / two-pixel gap regression. No game, UI, UMM runtime or key injection
is loaded.
