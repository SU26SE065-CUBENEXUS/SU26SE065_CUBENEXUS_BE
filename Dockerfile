FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CubeNexus.Domain/CubeNexus.Domain.csproj CubeNexus.Domain/
COPY CubeNexus.Application/CubeNexus.Application.csproj CubeNexus.Application/
COPY CubeNexus.Infrastructure/CubeNexus.Infrastructure.csproj CubeNexus.Infrastructure/
COPY CubeNexus.API/CubeNexus.API.csproj CubeNexus.API/
RUN dotnet restore CubeNexus.API/CubeNexus.API.csproj

COPY . .
RUN dotnet publish CubeNexus.API/CubeNexus.API.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CubeNexus.API.dll"]
