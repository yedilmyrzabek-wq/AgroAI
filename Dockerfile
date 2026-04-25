FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY NuGet.Config .
COPY src/AgroShield.Api/AgroShield.Api.csproj src/AgroShield.Api/
COPY src/AgroShield.Domain/AgroShield.Domain.csproj src/AgroShield.Domain/
COPY src/AgroShield.Application/AgroShield.Application.csproj src/AgroShield.Application/
COPY src/AgroShield.Infrastructure/AgroShield.Infrastructure.csproj src/AgroShield.Infrastructure/
RUN dotnet restore src/AgroShield.Api/AgroShield.Api.csproj

COPY src/ src/
RUN dotnet publish src/AgroShield.Api/AgroShield.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AgroShield.Api.dll"]
