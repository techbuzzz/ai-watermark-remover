# syntax=docker/dockerfile:1.7
#
# Multi-stage build for the WatermarkRemover HTTP API (`serve` sub-command)
# with the Astro web UI bundled in.
#
#   docker build -t watermarkremover .
#   docker run --rm -p 5080:5080 watermarkremover
#
# Three stages:
#   0. webbuild  — node:22-alpine, builds the Astro UI in /web and writes
#                  the static bundle to /web-out (consumed by the dotnet stage)
#   1. build     — dotnet SDK 10, restores + publishes the CLI; the wwwroot/
#                  content produced by webbuild is overlaid into the source
#                  tree before `dotnet publish` so the .csproj <Content>
#                  item picks it up
#   2. runtime   — aspnet 10 alpine, non-root, /app/wwwroot shipped from webbuild
#
# The final image is framework-dependent: it relies on the .NET runtime shipped
# inside `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`. Publishing targets
# `linux-musl-x64` so the produced apphost is compatible with Alpine's
# musl libc (the SDK image defaults to glibc, so an explicit RID is
# required to match the runtime stage).
#
# The final image runs as a dedicated non-root user (`wr`, uid:gid 10001),
# exposes the default API port (5080), and ships with a HEALTHCHECK that
# hits the unauthenticated `/health` endpoint exposed by `ServeCommand`.

# ----------------------------------------------------------------------------
# Stage 0 — build the Astro web UI
# ----------------------------------------------------------------------------
FROM node:22-alpine AS webbuild
WORKDIR /web

# Copy lockfile + manifest first so the npm ci layer caches across
# source-only changes.
COPY web/package.json web/package-lock.json* ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --no-audit --no-fund

# Now copy the rest of the web source and run a production build.
# `npm run build` calls `astro build` then `node scripts/sync.mjs`, which
# copies /web/dist → $WR_SYNC_TARGET (we point it at /web-out so the
# dotnet stage can pick it up via a fixed absolute path).
COPY web/ ./
ENV WR_SYNC_TARGET=/web-out \
    NODE_ENV=production
RUN --mount=type=cache,target=/root/.npm \
    npm run build

# ----------------------------------------------------------------------------
# Stage 1 — restore + publish
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the files that affect restore first so the NuGet layer can be
# reused across builds that don't touch any project metadata. The CLI
# project is the only thing we ship, so we only need the csprojs in its
# transitive project graph (Core/Text/Metadata/Image) — the test projects
# are excluded from the production image.
COPY global.json Directory.Build.props ./
COPY src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj       src/WatermarkRemover.CLI/
COPY src/WatermarkRemover.Core/WatermarkRemover.Core.csproj     src/WatermarkRemover.Core/
COPY src/WatermarkRemover.Text/WatermarkRemover.Text.csproj     src/WatermarkRemover.Text/
COPY src/WatermarkRemover.Metadata/WatermarkRemover.Metadata.csproj src/WatermarkRemover.Metadata/
COPY src/WatermarkRemover.Image/WatermarkRemover.Image.csproj   src/WatermarkRemover.Image/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj

# Overlay the Astro web UI bundle produced by the `webbuild` stage into
# the .NET source tree. The CLI csproj marks wwwroot/**/* as <Content>
# with CopyToOutputDirectory=PreserveNewest, so it'll ship in /app/wwwroot
# next to the binary.
COPY --from=webbuild /web-out ./src/WatermarkRemover.CLI/wwwroot

# Now copy the rest of the source and publish the CLI in Release.
COPY src/ src/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/WatermarkRemover.CLI/WatermarkRemover.CLI.csproj \
        -c Release \
        -r linux-musl-x64 \
        --self-contained false \
        -o /app/publish \
        --no-restore \
        /p:TreatWarningsAsErrors=true \
        /p:DebugType=embedded \
        /p:PublishSingleFile=false

# ----------------------------------------------------------------------------
# Stage 2 — runtime
# ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# curl is required for the HEALTHCHECK; everything else we need ships
# with the base image. We also provision a dedicated non-root user that
# owns the application directory and any runtime data we write.
USER root
RUN apk add --no-cache curl \
    && addgroup -S -g 10001 wr \
    && adduser  -S -G wr -u 10001 -h /app -s /sbin/nologin wr

WORKDIR /app
COPY --from=build --chown=wr:wr /app/publish ./

# Pre-create the directories users typically mount for configuration
# overrides and downloaded ONNX models, and make sure they are owned by
# the unprivileged user that runs the process.
RUN mkdir -p /app/data /app/models /app/config \
    && chown -R wr:wr /app

# Tame the .NET CLI inside the container — no logo, no first-run telemetry,
# mark this as a containerised run so the runtime can opt in to container-
# aware heuristics (default limits, garbage collection, etc.).
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

USER wr
EXPOSE 5080

# The apphost is the platform-specific executable that `dotnet publish`
# emits next to `watermarkremover.dll` for `linux-musl-x64`. Invoking it
# is equivalent to `dotnet watermarkremover.dll …` but skips the loader
# indirection on every process start.
ENTRYPOINT ["./watermarkremover"]

# Default sub-command: serve the HTTP API on all interfaces. Override
# `CMD` in `docker run` or compose to invoke a different sub-command
# (e.g. `clean-text --input … --output …`).
CMD ["serve", "--host", "0.0.0.0", "--port", "5080"]

# Match the rate-limiter's window to the probe interval so a flapping
# service doesn't get rate-limited out of its own healthcheck.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://127.0.0.1:5080/health || exit 1
