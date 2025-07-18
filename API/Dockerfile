
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5198

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src


COPY ["API.csproj", "./"] 

RUN dotnet restore

COPY . .

WORKDIR /src


RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false


FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "API.dll"]