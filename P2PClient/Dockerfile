# ✅在该文件的上层目录运行 docker build . -f P2PClient/Dockerfile -t p2p.client:1.0
# ❌如果在当前目录下运行 docker build . -t p2p.client:1.0 会导致无法找到要COPY的文件,因为上下文在当前目录,无法访问父级和兄弟文件夹中的文件内容


FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["P2PClient/P2PClient.csproj", "P2PClient/"]
COPY ["P2PViaUDP/P2PViaUDP.csproj", "P2PViaUDP/"]
RUN dotnet restore "P2PClient/P2PClient.csproj"
COPY . .
WORKDIR "/src/P2PClient"
RUN dotnet build "P2PClient.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "P2PClient.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "P2PClient.dll"]
