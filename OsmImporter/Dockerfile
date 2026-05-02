FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src

COPY ["OsmImporter.csproj", "./"]
RUN dotnet restore "./OsmImporter.csproj"

COPY . .
RUN dotnet build "./OsmImporter.csproj" -c Release

ENTRYPOINT ["dotnet", "run", "--project", "OsmImporter.csproj", "-c", "Release"]
