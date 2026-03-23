FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TikTokTracker.Web/TikTokTracker.Web.csproj", "TikTokTracker.Web/"]
RUN dotnet restore "TikTokTracker.Web/TikTokTracker.Web.csproj"
COPY . .
WORKDIR "/src/TikTokTracker.Web"
RUN dotnet build "TikTokTracker.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TikTokTracker.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TikTokTracker.Web.dll"]
