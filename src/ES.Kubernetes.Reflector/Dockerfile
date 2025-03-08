FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim-amd64 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ES.Kubernetes.Reflector/ES.Kubernetes.Reflector.csproj", "ES.Kubernetes.Reflector/"]
COPY . .
WORKDIR "/src/ES.Kubernetes.Reflector"
RUN dotnet build "ES.Kubernetes.Reflector.csproj" -c $BUILD_CONFIGURATION

FROM build AS publish
RUN dotnet publish "ES.Kubernetes.Reflector.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-build /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ES.Kubernetes.Reflector.dll"]