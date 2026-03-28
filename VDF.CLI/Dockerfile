# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VDF.Core/VDF.Core.csproj", "VDF.Core/"]
COPY ["VDF.CLI/VDF.CLI.csproj", "VDF.CLI/"]
RUN dotnet restore "VDF.CLI/VDF.CLI.csproj"
COPY VDF.Core/ VDF.Core/
COPY VDF.CLI/ VDF.CLI/
RUN dotnet publish "VDF.CLI/VDF.CLI.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["./vdf-cli"]
