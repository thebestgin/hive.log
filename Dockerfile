FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files for restore
COPY hive.log/HiveLog.Api/HiveLog.Api.csproj hive.log/HiveLog.Api/
COPY hivecache/src/HiveCache/HiveCache.csproj hivecache/src/HiveCache/
COPY hivecache/src/HiveCache.Postgres/HiveCache.Postgres.csproj hivecache/src/HiveCache.Postgres/

RUN dotnet restore hive.log/HiveLog.Api/HiveLog.Api.csproj

# Copy all source
COPY hive.log/ hive.log/
COPY hivecache/src/HiveCache/ hivecache/src/HiveCache/
COPY hivecache/src/HiveCache.Postgres/ hivecache/src/HiveCache.Postgres/

RUN dotnet publish hive.log/HiveLog.Api/HiveLog.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/hive.log/HiveLog.Api/_Projects /app/_Projects

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "HiveLog.Api.dll"]
