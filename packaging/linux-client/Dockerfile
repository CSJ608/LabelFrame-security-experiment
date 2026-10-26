FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LabelFrame.Core/LabelFrame.Core.csproj src/LabelFrame.Core/
COPY src/LabelFrame.Rendering/LabelFrame.Rendering.csproj src/LabelFrame.Rendering/
COPY src/LabelFrame.Api/LabelFrame.Api.csproj src/LabelFrame.Api/
COPY src/LabelFrame.Updates/LabelFrame.Updates.csproj src/LabelFrame.Updates/
COPY src/LabelFrame.WinHost/LabelFrame.WinHost.csproj src/LabelFrame.WinHost/
RUN dotnet restore src/LabelFrame.WinHost/LabelFrame.WinHost.csproj \
    -p:TargetFramework=net10.0 \
    -r linux-x64
COPY . .
RUN dotnet publish src/LabelFrame.WinHost/LabelFrame.WinHost.csproj \
    -c Release \
    -f net10.0 \
    -r linux-x64 \
    --self-contained false \
    --no-restore \
    -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG LABELFRAME_VERSION=dev
LABEL org.opencontainers.image.title="LabelFrame Linux Client" \
      org.opencontainers.image.version="$LABELFRAME_VERSION" \
      org.opencontainers.image.source="https://github.com/marci-labs/LabelFrame"
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fontconfig fonts-wqy-microhei libfontconfig1 libfreetype6 libharfbuzz0b \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out/ /app/

RUN mkdir -p /var/lib/labelframe/client/print \
    && chown -R app:app /var/lib/labelframe/client

ENV LABELFRAME_LISTEN=http://0.0.0.0:53960
ENV LABELFRAME_DB=/var/lib/labelframe/client/jobs.db
ENV LABELFRAME_TEMPLATES_DB=/var/lib/labelframe/client/templates.db
ENV LABELFRAME_LOGS_DB=/var/lib/labelframe/client/logs.db
ENV LABELFRAME_CONFIG=/var/lib/labelframe/client/settings.json
ENV LABELFRAME_CONNECTION=/var/lib/labelframe/client/connection.json
ENV LABELFRAME_PRINT_SETTINGS=/var/lib/labelframe/client/print-settings.json
ENV LABELFRAME_HOST_LOG=/var/lib/labelframe/client/host.log
ENV LABELFRAME_PRINT_OUTPUT=/var/lib/labelframe/client/print
ENV LABELFRAME_TRANSPORT=Log
ENV LABELFRAME_OPEN_BROWSER=0
ENV LABELFRAME_TRAY=0

USER app
EXPOSE 53960
VOLUME ["/var/lib/labelframe/client"]
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=12 \
    CMD curl --fail --silent http://127.0.0.1:53960/healthz > /dev/null || exit 1

ENTRYPOINT ["/app/LabelFrame.ClientHost"]
