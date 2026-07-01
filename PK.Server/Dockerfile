FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PK.AI.sln ./
COPY PK.Server/PK.Server.csproj PK.Server/
RUN dotnet restore

COPY PK.Server/ PK.Server/
RUN dotnet publish PK.Server/PK.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "PK.Server.dll"]

