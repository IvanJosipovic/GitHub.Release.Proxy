ARG BUILDPLATFORM=linux/amd64

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:620e765fe18186c08399f7aa978f79f04b6bbf0ee1b3b8a91e2d5c9619e59da1 AS build
RUN apk add build-base zlib-dev
WORKDIR /src
COPY GitHub.Release.Proxy.csproj .
RUN dotnet restore "GitHub.Release.Proxy.csproj"
COPY . .
RUN dotnet build "GitHub.Release.Proxy.csproj" -c Release -o /app/build

FROM build AS publish
ARG VERSION=0.0.1
RUN dotnet publish "GitHub.Release.Proxy.csproj" -c Release --use-current-runtime -o /app/publish /p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine@sha256:379b17d7d388a2a1b5330bfc2429a01091f85e255d3bce7981d65927d786c000 AS base
WORKDIR /app
EXPOSE 8080

LABEL org.opencontainers.image.description="Simple Github Release Proxy"

# renovate: datasource=repology depName=alpine_3_23/ca-certificates-bundle versioning=loose
ENV CA_CERTIFICATES_BUNDLES_VERSION="20251003-r0"

# renovate: datasource=repology depName=alpine_3_23/busybox versioning=loose
ENV BUSYBOX_VERSION="1.37.0-r30"

# renovate: datasource=repology depName=alpine_3_23/busybox-binsh versioning=loose
ENV BUSYBOX_BINSH_VERSION="1.37.0-r30"

# renovate: datasource=repology depName=alpine_3_23/libcrypto3 versioning=loose
ENV LIBCRYPTO3_VERSION="3.5.8-r0"

# renovate: datasource=repology depName=alpine_3_23/ssl_client versioning=loose
ENV SSL_VERSION="1.37.0-r30"

# renovate: datasource=repology depName=alpine_3_23/libgcc versioning=loose
ENV LIBGCC_VERSION="15.2.0-r2"

# renovate: datasource=repology depName=alpine_3_23/libssl3 versioning=loose
ENV LIBSSL3_VERSION="3.5.8-r0"

# renovate: datasource=repology depName=alpine_3_23/libstdc++ versioning=loose
ENV LIBSTDC_VERSION="15.2.0-r2"

# renovate: datasource=repology depName=alpine_3_23/zlib versioning=loose
ENV LIBZLIB_VERSION="1.3.2-r0"

RUN apk add --upgrade --no-cache \
        ca-certificates-bundle>$CA_CERTIFICATES_BUNDLES_VERSION \
        busybox>$BUSYBOX_BINSH_VERSION \
        busybox-binsh>$BUSYBOX_BINSH_VERSION \
        libcrypto3>$LIBCRYPTO3_VERSION \
        ssl_client>$SSL_VERSION \
        \
        # .NET dependencies
        libgcc>$LIBGCC_VERSION \
        libssl3>$LIBSSL3_VERSION \
        libstdc++>$LIBSTDC_VERSION \
        zlib>$LIBZLIB_VERSION

USER app

COPY --from=publish /app/publish .
ENTRYPOINT ["./GitHub.Release.Proxy"]
