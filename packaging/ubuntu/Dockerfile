# LabelFrame Server（Ubuntu 容器，framework-dependent）
# 构建：docker build -f packaging/ubuntu/Dockerfile -t labelframe-server artifacts/server-linux/linux-x64
# 运行：docker run -d --name labelframe-server -p 53961:53961 -v labelframe-data:/var/lib/labelframe/server labelframe-server
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# SkiaSharp Linux 依赖、中文预览字体与容器健康检查工具。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fontconfig fonts-wqy-microhei libfontconfig1 libfreetype6 libharfbuzz0b msitools osslsigncode ca-certificates \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY . /app
ARG LABELFRAME_VERSION=dev
LABEL org.opencontainers.image.title="LabelFrame Server" \
      org.opencontainers.image.version="$LABELFRAME_VERSION" \
      org.opencontainers.image.source="https://github.com/marci-labs/LabelFrame"
ENV LABELFRAME_SERVER_LISTEN=http://0.0.0.0:53961
ENV LABELFRAME_SERVER_DB=/var/lib/labelframe/server/server.db
ENV LABELFRAME_SERVER_TEMPLATES_DB=/var/lib/labelframe/server/templates.db
ENV LABELFRAME_SERVER_LOGS_DB=/var/lib/labelframe/server/logs.db
EXPOSE 53961
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=12 \
    CMD curl --fail --silent http://127.0.0.1:53961/healthz > /dev/null || exit 1
ENTRYPOINT ["/app/LabelFrame.Server"]
