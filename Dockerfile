FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MedicalManager.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish MedicalManager.csproj -c Release -o /app/publish \
    -r linux-x64 --self-contained false -p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data/keys
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV MEDICALMANAGER_DATA_DIR=/data
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "MedicalManager.dll"]
