# syntax=docker/dockerfile:1.7
# Nosum.Host image (docs/adr/ADR-031 D1). Build from the repository root:
#   docker build -f deploy/host.Dockerfile --build-arg NOSUM_RELEASE=private -t nosum/host .
# The final image is the .NET runtime only (no SDK), runs as the non-root `app` user and carries nothing but the published host and the maps it loads.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# public | private. A public build is refused while assets/PLACEHOLDER_CONTENT exists (docs/adr/ADR-031 D9), here and again by MSBuild at publish.
ARG NOSUM_RELEASE=private
WORKDIR /src

COPY global.json .editorconfig Directory.Build.props Directory.Packages.props Version.props ./
COPY engine/Nosum.Core engine/Nosum.Core
COPY contracts contracts
COPY gameplay gameplay
COPY server server
COPY assets assets
COPY deploy/check-placeholder-art.sh deploy/PlaceholderGate.targets deploy/

RUN sh deploy/check-placeholder-art.sh "$NOSUM_RELEASE"
RUN dotnet restore server/Nosum.Host/Nosum.Host.csproj
RUN dotnet publish server/Nosum.Host/Nosum.Host.csproj -c Release --no-restore -p:NosumRelease="$NOSUM_RELEASE" -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /out .
COPY deploy/host-entrypoint.sh /usr/local/bin/nosum-entrypoint
RUN chmod 0555 /usr/local/bin/nosum-entrypoint \
 && mkdir -p /data \
 && chown "$APP_UID" /data

# Nalix keeps its server key (certificate.private/.public), server.ini and block lists under NALIX_BASE_PATH: mount a volume on /data, or the key changes with every container.
ENV DOTNET_ENVIRONMENT=Production \
    NALIX_BASE_PATH=/data \
    NALIX_CONFIG_PATH=/data/config \
    NOSUM_OPS_HOST=localhost \
    DOTNET_EnableDiagnostics=0
VOLUME /data
USER $APP_UID
EXPOSE 57300 57301

HEALTHCHECK --interval=15s --timeout=6s --start-period=90s --retries=3 CMD ["dotnet", "Nosum.Host.dll", "healthcheck"]
ENTRYPOINT ["nosum-entrypoint"]
CMD ["dotnet", "Nosum.Host.dll"]
