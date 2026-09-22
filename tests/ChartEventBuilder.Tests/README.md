Run from the repository root:

```powershell
dotnet run --project tests/ChartEventBuilder.Tests/ChartEventBuilder.Tests.csproj
```

This .NET 6 console test suite links the production `ChartEventBuilder.cs` and
`LaneAllocator.cs` directly. It requires neither Unity nor game assemblies.
The fixtures exercise manual-input semantics: starting tiles, omitted automatic
sections, zero-time midspin transitions, hold release and consecutive holds,
multitap requirements, incoming timing geometry, and preservation of absolute
song time. Small 4K/6K/8K integration checks ensure held requirements survive lane
assignment. A nonzero exit code reports failures.

These checks validate the extracted timing model. Automatic holds are checked
for ordinary or multitap release endpoints and the following manual input.
Automatic-hold-to-manual-hold transitions are not claimed to be fully verified:
the new manual hold still needs a useful holding prompt even if its arrival is
automatic. Live Unity rendering, level loading, and the game's optional
hold/input behavior still require game testing.
