FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MedicalManager.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish MedicalManager.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN mkdir -p /data/keys
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV MEDICALMANAGER_DATA_DIR=/data
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "MedicalManager.dll"]
