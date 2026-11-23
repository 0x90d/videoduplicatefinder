# Build stage
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY ["VDF.Core/VDF.Core.csproj", "VDF.Core/"]
COPY ["VDF.Web/VDF.Web.csproj", "VDF.Web/"]
# Build for the image's own platform: a build for any platform would carry every
# platform's ONNX Runtime library (~200 MB). TARGETARCH is set by BuildKit; the legacy
# builder leaves it empty, so fall back to the build container's own architecture
# (dpkg uses the same names), or an ARM host would get the x64 library.
ARG TARGETARCH
RUN ARCH="${TARGETARCH:-$(dpkg --print-architecture)}"; \
	if [ "$ARCH" = "arm64" ]; then echo linux-arm64; else echo linux-x64; fi > /tmp/rid
RUN dotnet restore "VDF.Web/VDF.Web.csproj" -r "$(cat /tmp/rid)"
COPY VDF.Core/ VDF.Core/
COPY VDF.Web/ VDF.Web/
RUN dotnet publish "VDF.Web/VDF.Web.csproj" -c Release -r "$(cat /tmp/rid)" --self-contained false --no-restore -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:11.0
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "VDF.Web.dll"]
