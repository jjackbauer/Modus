.PHONY: run-host run-telemetry run-telemetry-live build test test-no-build help

SOLUTION=Modus.slnx
HOST_PROJECT=src/Modus.Host/Modus.Host.csproj
TELEMETRY_PLUGIN_PROJECT=plugins/Plugin.Host.Telemetry.csproj
MACHINE_TELEMETRY_PLUGIN_PROJECT=plugins/Plugin.Machine.Telemetry.csproj
PLUGINS_PATH=$(CURDIR)/plugins

help:
	@echo "Available targets:"
	@echo "  make run-host       # Run host with plugin directory"
	@echo "  make run-telemetry  # Build telemetry plugin and run host"
	@echo "  make run-telemetry-live  # Build telemetry plugins and run host continuously"
	@echo "  make build          # Build full solution"
	@echo "  make test           # Run full solution tests"
	@echo "  make test-no-build  # Run tests without rebuilding"

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
