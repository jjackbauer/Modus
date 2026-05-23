.PHONY: run-host run-telemetry run-telemetry-live build test test-no-build pack publish publish-symbols release help

SOLUTION=Modus.slnx
HOST_PROJECT=src/Modus.Host/Modus.Host.csproj
CORE_PROJECT=src/Modus.Core/Modus.Core.csproj
TELEMETRY_PLUGIN_PROJECT=plugins/Plugin.Host.Telemetry.csproj
MACHINE_TELEMETRY_PLUGIN_PROJECT=plugins/Plugin.Machine.Telemetry.csproj
PLUGINS_PATH=$(CURDIR)/plugins
ARTIFACTS_DIR=$(CURDIR)/artifacts/nuget
ARTIFACTS_DIR_WIN=$(subst /,\,$(ARTIFACTS_DIR))
NUGET_SOURCE=https://api.nuget.org/v3/index.json
VERSION=

help:
	@echo "Available targets:"
	@echo "  make run-host       # Run host with plugin directory"
	@echo "  make run-telemetry  # Build telemetry plugin and run host"
	@echo "  make run-telemetry-live  # Build telemetry plugins and run host continuously"
	@echo "  make build          # Build full solution"
	@echo "  make test           # Run full solution tests"
	@echo "  make test-no-build  # Run tests without rebuilding"
	@echo "  make pack           # Pack Modus.Core + Modus.Host into artifacts/nuget"
	@echo "  make publish        # Push .nupkg to NuGet (requires NUGET_API_KEY)"
	@echo "  make publish-symbols # Push .snupkg to NuGet (requires NUGET_API_KEY)"
	@echo "  make release        # Pack + publish + publish-symbols"
	@echo "  Optional: VERSION=1.2.3 to override PackageVersion when packing"

pack:
	dotnet pack $(CORE_PROJECT) -c Release -o $(ARTIFACTS_DIR) $(if $(VERSION),/p:PackageVersion=$(VERSION),)
	dotnet pack $(HOST_PROJECT) -c Release -o $(ARTIFACTS_DIR) $(if $(VERSION),/p:PackageVersion=$(VERSION),)

publish:
	@if "$(NUGET_API_KEY)"=="" (echo NUGET_API_KEY is required && exit /b 1)
	@dir /b "$(ARTIFACTS_DIR_WIN)\*.nupkg" >nul 2>nul || (echo No .nupkg files found in $(ARTIFACTS_DIR_WIN) && exit /b 1)
	@for %%F in ("$(ARTIFACTS_DIR_WIN)\*.nupkg") do dotnet nuget push "%%~fF" --source $(NUGET_SOURCE) --api-key $(NUGET_API_KEY) --skip-duplicate --no-symbols

publish-symbols:
	@if "$(NUGET_API_KEY)"=="" (echo NUGET_API_KEY is required && exit /b 1)
	@dir /b "$(ARTIFACTS_DIR_WIN)\*.snupkg" >nul 2>nul || (echo No .snupkg files found in $(ARTIFACTS_DIR_WIN) && exit /b 1)
	@for %%F in ("$(ARTIFACTS_DIR_WIN)\*.snupkg") do dotnet nuget push "%%~fF" --source $(NUGET_SOURCE) --api-key $(NUGET_API_KEY) --skip-duplicate

release: pack publish publish-symbols

run-host:
	dotnet run --project $(HOST_PROJECT) -- $(PLUGINS_PATH)

run-telemetry:
	dotnet build $(TELEMETRY_PLUGIN_PROJECT)
	dotnet build $(MACHINE_TELEMETRY_PLUGIN_PROJECT)
	dotnet run --project $(HOST_PROJECT) -- --run-once $(PLUGINS_PATH)

run-telemetry-live:
	dotnet build $(TELEMETRY_PLUGIN_PROJECT)
	dotnet build $(MACHINE_TELEMETRY_PLUGIN_PROJECT)
	dotnet run --project $(HOST_PROJECT) -- $(PLUGINS_PATH)

build:
	dotnet build $(SOLUTION)

test:
	dotnet test $(SOLUTION)

test-no-build:
	dotnet test $(SOLUTION) --no-build
