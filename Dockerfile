# Imagen única: compila el front de Angular, lo mete en wwwroot y publica la API de .NET.

# 1) Front
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY web/ ./
RUN npx ng build --configuration production

# 2) API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY api/Mda.Api/Mda.Api.csproj api/Mda.Api/
RUN dotnet restore api/Mda.Api/Mda.Api.csproj
COPY api/Mda.Api/ api/Mda.Api/
COPY --from=web /web/dist/web/browser/ api/Mda.Api/wwwroot/
RUN dotnet publish api/Mda.Api/Mda.Api.csproj -c Release -o /app --no-restore

# 3) Ejecución: imagen mínima, usuario sin privilegios
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# carpeta para las claves de las cookies (el volumen las conserva entre reinicios) y para las miniaturas de la galería
RUN mkdir -p /claves /derivados && chown $APP_UID /claves /derivados
USER $APP_UID
ENTRYPOINT ["dotnet", "Mda.Api.dll"]
