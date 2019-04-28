FROM mcr.microsoft.com/dotnet/core/aspnet:2.2 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build
WORKDIR /src
COPY ["ES.Kubernetes.Reflector/ES.Kubernetes.Reflector.csproj", "ES.Kubernetes.Reflector/"]

RUN dotnet restore "ES.Kubernetes.Reflector/ES.Kubernetes.Reflector.csproj"
COPY . .
WORKDIR "/src/ES.Kubernetes.Reflector"
RUN dotnet build "ES.Kubernetes.Reflector.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "ES.Kubernetes.Reflector.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "ES.Kubernetes.Reflector.dll"]