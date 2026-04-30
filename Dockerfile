FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY hive.log/HiveLog.Api/HiveLog.Api.csproj hive.log/HiveLog.Api/

RUN dotnet restore hive.log/HiveLog.Api/HiveLog.Api.csproj

COPY hive.log/ hive.log/

RUN dotnet publish hive.log/HiveLog.Api/HiveLog.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "HiveLog.Api.dll"]
