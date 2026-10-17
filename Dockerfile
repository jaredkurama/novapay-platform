FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props NuGet.Config ./
COPY src/NovaPay.Api/NovaPay.Api.csproj src/NovaPay.Api/packages.lock.json src/NovaPay.Api/
RUN dotnet restore src/NovaPay.Api/NovaPay.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/NovaPay.Api/NovaPay.Api.csproj --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "NovaPay.Api.dll"]
