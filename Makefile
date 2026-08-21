# Makefile — WatermarkRemover one-command build / dev helpers.
#
# Usage:
#   make            # build everything (web UI + .NET) and run the dev server
#   make build      # build both the web UI and the .NET solution
#   make web        # build only the Astro web UI
#   make dotnet     # build only the .NET solution
#   make test       # run all test suites (web + .NET)
#   make serve      # build everything and run the API + UI on :5080
#   make clean      # remove build artefacts
#
# All targets are deliberately phrased so that a single `make` invocation
# leaves a working binary AND a working web UI. This is the "out of the box"
# promise: `git clone` → `make` → open `http://localhost:5080/`.

# ---- Tooling ----
NPM            ?= npm.cmd
NPM_FLAGS      ?= --no-audit --no-fund
DOTNET         ?= dotnet
SOLUTION       := src/WatermarkRemover.sln
PROJECT        := src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj
WEB_DIR        := web
CONFIG         ?= Release
PORT           ?= 5080

# ---- Default target ----
.PHONY: all
all: build

.PHONY: help
help:
	@echo "WatermarkRemover — make targets:"
	@echo "  make / make all    Build web UI + .NET (everything needed to run)"
	@echo "  make build         Same as above"
	@echo "  make web           Build only the Astro web UI"
	@echo "  make dotnet        Build only the .NET solution"
	@echo "  make test          Run all tests (web vitest + dotnet test)"
	@echo "  make serve         Build everything, then run the API + UI on :$(PORT)"
	@echo "  make clean         Remove build artefacts (dist/, bin/, obj/, wwwroot/)"

# ---- Build the Astro web UI and sync it into the .NET wwwroot/ ----
.PHONY: web
web:
	cd $(WEB_DIR) && $(NPM) install $(NPM_FLAGS) && $(NPM) run build

# ---- Build the .NET solution ----
.PHONY: dotnet
dotnet:
	$(DOTNET) build $(SOLUTION) -c $(CONFIG) /p:TreatWarningsAsErrors=true

# ---- Both: web then dotnet ----
.PHONY: build
build: web dotnet

# ---- Tests ----
.PHONY: test-web
test-web:
	cd $(WEB_DIR) && $(NPM) test

.PHONY: test-dotnet
test-dotnet:
	$(DOTNET) test $(SOLUTION) --no-build -c $(CONFIG)

.PHONY: test
test: test-web test-dotnet

# ---- Serve (dev) ----
.PHONY: serve
serve: build
	$(DOTNET) run --project $(PROJECT) -- serve --port $(PORT)

# ---- Clean ----
.PHONY: clean
clean:
	cd $(WEB_DIR) && $(NPM) run clean 2>/dev/null || true
	rm -rf $(WEB_DIR)/dist $(WEB_DIR)/.astro
	rm -rf src/WatermarkRemover.CLI/wwwroot
	$(DOTNET) clean $(SOLUTION)

# ---- Convenience: smoke test the co-located serve ----
.PHONY: smoke
smoke: build
	@echo "Starting serve in background, hitting /, /health, /clean/text, then stopping..."
	@$(DOTNET) run --project $(PROJECT) -- serve --port $(PORT) &
	@SERVER_PID=$$!; \
	  sleep 5; \
	  echo "GET / → $$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$(PORT)/)"; \
	  echo "GET /health → $$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$(PORT)/health)"; \
	  echo "POST /clean/text → $$(curl -s -o /dev/null -w '%{http_code}' -X POST -H 'Content-Type: application/json' -d '{\"text\":\"Hello\\u200b\"}' http://127.0.0.1:$(PORT)/clean/text)"; \
	  kill $$SERVER_PID 2>/dev/null || true
