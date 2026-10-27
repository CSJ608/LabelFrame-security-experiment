# LabelFrame Server（Ubuntu 容器，framework-dependent）
# 构建：docker build -f packaging/ubuntu/Dockerfile -t labelframe-server artifacts/server-linux/linux-x64
# 运行：docker run -d --name labelframe-server -p 53961:53961 -v labelframe-data:/var/lib/labelframe/server labelframe-server
FROM ubuntu:24.04 AS android-tools
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip ca-certificates \
    && mkdir -p /opt/labelframe/android \
    && curl --fail --location --retry 3 https://dl.google.com/dl/android/maven2/com/android/tools/build/apksig/8.9.2/apksig-8.9.2.jar -o /opt/labelframe/android/apksig.jar \
    && echo 'c070ed1394629d74641aa0906f60b2ffa1ee77e6366a1f93437f59717b1aeb89  /opt/labelframe/android/apksig.jar' | sha256sum -c - \
    && curl --fail --location --retry 3 https://dl.google.com/dl/android/maven2/com/android/tools/build/aapt2/8.9.2-12782657/aapt2-8.9.2-12782657-linux.jar -o /tmp/aapt2.jar \
    && echo 'ffb7b7d419c7341dd013f2f87bfb0148f8bff0a2a47c222f54935b3b8f233605  /tmp/aapt2.jar' | sha256sum -c - \
    && unzip /tmp/aapt2.jar -d /opt/labelframe/android/aapt2-package \
    && chmod 755 /opt/labelframe/android/aapt2-package/aapt2

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# SkiaSharp Linux 依赖、中文预览字体与容器健康检查工具。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl fontconfig fonts-wqy-microhei libfontconfig1 libfreetype6 libharfbuzz0b msitools osslsigncode ca-certificates openjdk-17-jdk-headless \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY . /app
COPY --from=android-tools /opt/labelframe/android /opt/labelframe/android
ARG LABELFRAME_VERSION=dev
LABEL org.opencontainers.image.title="LabelFrame Server" \
      org.opencontainers.image.version="$LABELFRAME_VERSION" \
      org.opencontainers.image.source="https://github.com/marci-labs/LabelFrame"
ENV LABELFRAME_SERVER_LISTEN=http://0.0.0.0:53961
ENV LABELFRAME_SERVER_DB=/var/lib/labelframe/server/server.db
ENV LABELFRAME_SERVER_TEMPLATES_DB=/var/lib/labelframe/server/templates.db
ENV LABELFRAME_SERVER_LOGS_DB=/var/lib/labelframe/server/logs.db
ENV LABELFRAME_ANDROID_JAVA=/usr/bin/java
ENV LABELFRAME_ANDROID_APKSIGNER_JAR=/opt/labelframe/android/apksig.jar
ENV LABELFRAME_ANDROID_AAPT2=/opt/labelframe/android/aapt2-package/aapt2
ENV LABELFRAME_ANDROID_PUBLISHER_SHA256=369914044fb6283f722c58860ebed20cc9b4dcec551af761b701dc3f9f18ce43
EXPOSE 53961
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=12 \
    CMD curl --fail --silent http://127.0.0.1:53961/healthz > /dev/null || exit 1
ENTRYPOINT ["/app/LabelFrame.Server"]
