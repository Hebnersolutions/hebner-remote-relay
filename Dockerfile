# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ./src/Hebner.Agent.BrokerStub/Hebner.Agent.BrokerStub.csproj
RUN dotnet publish ./src/Hebner.Agent.BrokerStub/Hebner.Agent.BrokerStub.csproj -c Release -o /out

# ---------- run ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}
EXPOSE 10000
ENTRYPOINT ["dotnet", "Hebner.Agent.BrokerStub.dll"]
