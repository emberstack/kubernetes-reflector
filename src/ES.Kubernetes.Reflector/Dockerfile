FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim-amd64 AS build
ARG BUILD_CONFIGURATION=Release
COPY . .
RUN dotnet build "src/ES.Kubernetes.Reflector/ES.Kubernetes.Reflector.csproj" -c $BUILD_CONFIGURATION

FROM build AS publish
RUN dotnet publish "src/ES.Kubernetes.Reflector/ES.Kubernetes.Reflector.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-build /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ES.Kubernetes.Reflector.dll"]