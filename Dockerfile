FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/InventoryHold.Contracts/InventoryHold.Contracts.csproj src/InventoryHold.Contracts/
COPY src/InventoryHold.Domain/InventoryHold.Domain.csproj src/InventoryHold.Domain/
COPY src/InventoryHold.Infrastructure/InventoryHold.Infrastructure.csproj src/InventoryHold.Infrastructure/
COPY src/InventoryHold.WebApi/InventoryHold.WebApi.csproj src/InventoryHold.WebApi/
RUN dotnet restore src/InventoryHold.WebApi/InventoryHold.WebApi.csproj
COPY src/ src/
RUN dotnet publish src/InventoryHold.WebApi/InventoryHold.WebApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "InventoryHold.WebApi.dll"]
