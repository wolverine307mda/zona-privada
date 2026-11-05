# Imagen única: compila el front de Angular, lo mete en wwwroot y publica la API de .NET.

# 1) Front
FROM node:25-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY web/ ./
RUN npx ng build --configuration production

# 2) API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY api/Mda.Api/Mda.Api.csproj api/Mda.Api/
# con el runtime de Linux para poder precompilar (ReadyToRun): el arranque y las primeras peticiones van más rápido
RUN dotnet restore api/Mda.Api/Mda.Api.csproj -r linux-x64 -p:PublishReadyToRun=true
COPY api/Mda.Api/ api/Mda.Api/
COPY --from=web /web/dist/web/browser/ api/Mda.Api/wwwroot/
RUN dotnet publish api/Mda.Api/Mda.Api.csproj -c Release -o /app --no-restore -r linux-x64 --self-contained false -p:PublishReadyToRun=true

# 3) Ejecución: imagen mínima, usuario sin privilegios
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
# QuestPDF (el informe mensual en PDF) necesita fontconfig y una tipografía en el sistema
# Tesseract (OCR en español e inglés) y poppler (pdftoppm, para pasar a imagen los PDF escaneados): la búsqueda por contenido de Documentos lee imágenes
# y PDF escaneados en este mismo contenedor, sin enviar nada fuera. Con «Indexado__Ocr=off» no se usan (y la imagen funciona igual).
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core tesseract-ocr tesseract-ocr-spa tesseract-ocr-eng poppler-utils && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# carpeta para las claves de las cookies (el volumen las conserva entre reinicios) y para las miniaturas de la galería
RUN mkdir -p /claves /derivados && chown $APP_UID /claves /derivados
USER $APP_UID
ENTRYPOINT ["dotnet", "Mda.Api.dll"]
