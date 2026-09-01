# Validation status

This source was authored against the current TShock 6.1.0 / .NET 9 API information available on 2026-08-27 and includes a GitHub Actions build workflow.

The creation environment used to assemble this package does not have the .NET SDK installed, so a local `dotnet build` could not be executed here. The first required validation step after extraction is therefore:

```bash
dotnet restore
dotnet build -c Release
```

Do not deploy to a production economy until the build passes and the test checklist in `docs/INSTALLATION.md` has been completed on a staging TShock server.
