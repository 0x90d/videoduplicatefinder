### Build the VDF.Core
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env

# Copy core as a layer
WORKDIR /app/VDF.Core
COPY VDF.Core ./
    
# Copy GUI as a layer and run the restore
WORKDIR /app/VDF.GUI
COPY VDF.GUI ./
RUN dotnet restore
    
# Build the projects
RUN dotnet publish -c Release -o out

### Copy the compile app to the runtime location
# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build-env /app/VDF.GUI/out .
#ENTRYPOINT ["dotnet", "VDF.GUI.dll"]

### Set up the VNC/Web environment
FROM dorowu/ubuntu-desktop-lxde-vnc

# Install .NET dependencies
RUN apt-key adv --keyserver keyserver.ubuntu.com --recv-keys 4EB27DB2A3B88B8B
RUN apt-get update \
    && apt-get install -y libx11-dev \
    && apt-get autoclean -y \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# Install .NET
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates \
    && wget -q https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        dotnet-runtime-6.0 \
    && rm -rf /var/lib/apt/lists/*

# Copy the built files to the Desktop
WORKDIR /root/Desktop
COPY --from=build-env /app/VDF.GUI/out ./VDF

# Copy the shortcut to the desktop
COPY Docker/VDF.desktop .

ENTRYPOINT ["/startup.sh"]
