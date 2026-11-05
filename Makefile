# ── Repository (solution) targets ──
# Generic build/test/run targets for the whole Modus solution, used by operators and
# CI tooling documentation. `make <target>` prints no secrets.
SOLUTION=Modus.slnx
HOST_PROJECT=src/Modus.Host/Modus.Host.csproj
PLUGINS_PATH=$(CURDIR)/plugins

.PHONY: build
build: ## Build the whole solution
	dotnet build $(SOLUTION)

.PHONY: test
test: ## Run every test project in the solution
	dotnet test $(SOLUTION)

.PHONY: test-no-build
test-no-build: ## Run tests without rebuilding (fast validation after build)
	dotnet test $(SOLUTION) --no-build

.PHONY: run-host
run-host: ## Start the Modus host with the repository plugins folder
	dotnet run --project $(HOST_PROJECT) -- $(PLUGINS_PATH)

# ── Bones Learning Loop ──
# Usage: make <target>
#
# Prerequisites:
#   - .NET 10 SDK
#   - DeepSeek API key: set DEEPSEEK_API_KEY env var
#
# On Windows, run from Git Bash or WSL. For PowerShell, use .\bones.ps1 instead.

PROJECT   := WIP/Wip.Bones.Host
RECOVERY  := WIP/BonesLibraryRecovery
DATA_DIR  := WIP/Wip.Bones.Host/bones-data
LIBRARY   := $(DATA_DIR)/.bones/strategy-library
URL       := http://127.0.0.1:8080

# ── Defaults ──
BONES_MAX_GAMES_PER_RUN  ?= 100
BONES_RESUME_FROM_BEST   ?= true
BONES_EVALUATION_MATCH_COUNT ?= 20

# ═══════════════════════════════════════════════════════════════════
# Run
# ═══════════════════════════════════════════════════════════════════

.PHONY: run
run: ## Start the learning loop (resumes from library if present)
	@echo "=== Starting Bones (resume=$(BONES_RESUME_FROM_BEST), max_games=$(BONES_MAX_GAMES_PER_RUN)) ==="
	BONES_RESUME_FROM_BEST=$(BONES_RESUME_FROM_BEST) \
	BONES_MAX_GAMES_PER_RUN=$(BONES_MAX_GAMES_PER_RUN) \
	dotnet run --project $(PROJECT)

.PHONY: resume
resume: ## Resume with library strategies (100 games per run)
	@$(MAKE) run BONES_RESUME_FROM_BEST=true BONES_MAX_GAMES_PER_RUN=100

.PHONY: cold
cold: ## Cold start — ignore library, re-ponder from scratch
	@$(MAKE) run BONES_RESUME_FROM_BEST=false BONES_MAX_GAMES_PER_RUN=100

.PHONY: quick
quick: ## Quick run with low game cap for testing
	@$(MAKE) run BONES_RESUME_FROM_BEST=true BONES_MAX_GAMES_PER_RUN=20

.PHONY: stub
stub: ## Offline stub mode (no API key needed)
	@echo "=== Starting Bones in stub mode ==="
	BonesHost__ModelProvider=stub \
	BONES_MAX_GAMES_PER_RUN=10 \
	dotnet run --project $(PROJECT)

# ═══════════════════════════════════════════════════════════════════
# Status & control
# ═══════════════════════════════════════════════════════════════════

.PHONY: status
status: ## Show current loop status
	@curl -s $(URL)/api/bones/status | python3 -m json.tool 2>/dev/null || curl -s $(URL)/api/bones/status

.PHONY: stop
stop: ## Graceful stop after current iteration
	@curl -s -X POST $(URL)/api/bones/stop

.PHONY: start
start: ## Resume a stopped loop
	@curl -s -X POST $(URL)/api/bones/start

.PHONY: health
health: ## Liveness probe
	@curl -s $(URL)/health

# ═══════════════════════════════════════════════════════════════════
# Library management
# ═══════════════════════════════════════════════════════════════════

.PHONY: library
library: ## Show library contents
	@echo "=== Strategy Library ==="
	@for f in $(LIBRARY)/seat-*.json; do \
		echo "--- $$(basename $$f) ---"; \
		python3 -c "import json; d=json.load(open('$$f')); print(f\"  {d['strategyId']} ({d['kind']}) W:{d['wins']}/{d['matchesPlayed']} diff:{d['cumulativeScoreDifferential']}\")" 2>/dev/null || cat $$f; \
	done

.PHONY: recover
recover: ## Re-scan sessions and rebuild library from promoted strategies
	@echo "=== Recovering library from session artifacts ==="
	dotnet run --project $(RECOVERY)

.PHONY: reset-library
reset-library: ## Wipe library (next run will re-ponder from scratch)
	@echo "=== Wiping library ==="
	rm -rf $(LIBRARY)
	@echo "Library wiped. Use 'make cold' to start fresh."

# ═══════════════════════════════════════════════════════════════════
# Dev
# ═══════════════════════════════════════════════════════════════════

.PHONY: build
build: ## Build all bones projects
	dotnet build WIP/Wip.Bones.Agents
	dotnet build WIP/Wip.Bones.Host
	dotnet build $(RECOVERY)

.PHONY: test
test: ## Run agent tests
	dotnet test WIP/Wip.Bones.Tests --verbosity quiet

.PHONY: test-host
test-host: ## Run host integration tests
	dotnet test WIP/Wip.Bones.Host.Tests --verbosity quiet

.PHONY: test-all
test-all: test test-host ## Run all tests

.PHONY: clean
clean: ## Remove build artifacts
	dotnet clean WIP/Wip.Bones.Agents
	dotnet clean WIP/Wip.Bones.Host
	dotnet clean $(RECOVERY)

# ═══════════════════════════════════════════════════════════════════

.PHONY: help
help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help
