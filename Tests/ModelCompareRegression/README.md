# Model Compare Regression Harness

This package-free console harness validates snapshot JSON loading and Model Compare behavior without connecting to ETABS.

From the repository root:

```powershell
dotnet build "CSI Modelling Tools.slnx"
dotnet run --project "Tests\ModelCompareRegression\ModelCompareRegression.csproj" --no-build
```

If Windows Application Control blocks the generated test executable, run the managed assembly directly:

```powershell
dotnet "Tests\ModelCompareRegression\bin\Debug\net8.0-windows\ModelCompareRegression.dll"
```

Success is reported as:

```text
38/38 Model Compare regression tests passed.
```
