FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ["ES.Kubernetes.Reflector.Host/ES.Kubernetes.Reflector.Host.csproj", "ES.Kubernetes.Reflector.Host/"]
RUN dotnet restore "ES.Kubernetes.Reflector.Host/ES.Kubernetes.Reflector.Host.csproj"
COPY . .
WORKDIR "/src/ES.Kubernetes.Reflector.Host"
RUN dotnet build "ES.Kubernetes.Reflector.Host.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ES.Kubernetes.Reflector.Host.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ES.Kubernetes.Reflector.Host.dll"]