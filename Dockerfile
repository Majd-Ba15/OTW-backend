FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY OTW.Api.csproj .
RUN dotnet restore OTW.Api.csproj
COPY . .
RUN dotnet publish OTW.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "OTW.Api.dll"]
