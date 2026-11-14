FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Polle.sln ./
COPY src/Polle.Domain/Polle.Domain.csproj src/Polle.Domain/
COPY src/Polle.Infrastructure/Polle.Infrastructure.csproj src/Polle.Infrastructure/
COPY src/Polle.Api/Polle.Api.csproj src/Polle.Api/
RUN dotnet restore src/Polle.Api/Polle.Api.csproj

COPY src/ src/
RUN dotnet publish src/Polle.Api/Polle.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Polle.Api.dll"]
