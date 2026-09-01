FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY RentalManager.Core/RentalManager.Core.csproj RentalManager.Core/
COPY RentalManager.Infrastructure/RentalManager.Infrastructure.csproj RentalManager.Infrastructure/
COPY RentalManager.Api/RentalManager.Api.csproj RentalManager.Api/
RUN dotnet restore RentalManager.Api/RentalManager.Api.csproj -m:1 /p:NuGetAudit=false
COPY . .
RUN dotnet publish RentalManager.Api/RentalManager.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-noto-core \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /var/rental/slips && chown -R app:app /var/rental
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RentalManager.Api.dll"]
