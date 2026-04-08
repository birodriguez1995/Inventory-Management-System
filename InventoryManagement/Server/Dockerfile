# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first (layer cache)
COPY InventoryManagement.sln ./
COPY Shared/InventoryManagement.Shared.csproj Shared/
COPY Client/InventoryManagement.Client.csproj Client/
COPY Server/InventoryManagement.Server.csproj Server/

RUN dotnet restore Server/InventoryManagement.Server.csproj

# Copy source
COPY Shared/ Shared/
COPY Client/ Client/
COPY Server/ Server/

# Publish
RUN dotnet publish Server/InventoryManagement.Server.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN mkdir /data && chmod 777 /data

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "InventoryManagement.Server.dll"]
