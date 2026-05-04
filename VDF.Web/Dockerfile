# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VDF.Core/VDF.Core.csproj", "VDF.Core/"]
COPY ["VDF.Web/VDF.Web.csproj", "VDF.Web/"]
RUN dotnet restore "VDF.Web/VDF.Web.csproj"
COPY VDF.Core/ VDF.Core/
COPY VDF.Web/ VDF.Web/
RUN dotnet publish "VDF.Web/VDF.Web.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "VDF.Web.dll"]
