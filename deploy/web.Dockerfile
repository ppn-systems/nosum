# syntax=docker/dockerfile:1.7
# Caddy image that serves the static WASM client and proxies the game WebSocket (docs/adr/ADR-031 D1, D2). Build from the repository root:
#   docker build -f deploy/web.Dockerfile --build-arg NOSUM_RELEASE=private -t nosum/web .
# The client is published ahead-of-time compiled (docs/adr/ADR-002 D5), which needs the wasm-tools workload and takes several minutes.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# public | private. A public build is refused while assets/PLACEHOLDER_CONTENT exists (docs/adr/ADR-031 D9), here and again by MSBuild at publish.
ARG NOSUM_RELEASE=private
WORKDIR /src

COPY global.json .editorconfig Directory.Build.props Directory.Packages.props Version.props ./
COPY engine engine
COPY contracts contracts
COPY gameplay gameplay
COPY wasm wasm
COPY assets assets
COPY deploy/check-placeholder-art.sh deploy/PlaceholderGate.targets deploy/

RUN sh deploy/check-placeholder-art.sh "$NOSUM_RELEASE"
RUN dotnet workload install wasm-tools
RUN dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumWebDeploy=true -p:NosumRelease="$NOSUM_RELEASE" -o /out

FROM caddy:2-alpine AS final
COPY --from=build /out/wwwroot /srv
COPY deploy/Caddyfile /etc/caddy/Caddyfile
EXPOSE 80 443 443/udp
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 CMD ["wget", "-q", "--spider", "http://127.0.0.1:2019/config/"]
