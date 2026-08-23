FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json VietnamCarPlatform.sln ./
COPY apps/api/src/Api/VietnamCarPlatform.Api.csproj apps/api/src/Api/
COPY apps/api/src/Domain/VietnamCarPlatform.Domain.csproj apps/api/src/Domain/
COPY apps/api/src/Infrastructure/VietnamCarPlatform.Infrastructure.csproj apps/api/src/Infrastructure/
RUN dotnet restore apps/api/src/Api/VietnamCarPlatform.Api.csproj
COPY apps/api/src/Api apps/api/src/Api
COPY apps/api/src/Domain apps/api/src/Domain
COPY apps/api/src/Infrastructure apps/api/src/Infrastructure
RUN dotnet publish apps/api/src/Api/VietnamCarPlatform.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=app:app /app/publish .
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "VietnamCarPlatform.Api.dll"]
