# Production image: API and client in one process, as scripts/publish.sh builds them.
# Mount persistent storage at Storage__Directory; README.md#container lists the settings.
FROM node:24-slim AS client
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY backend/FamilyLearning.Api/FamilyLearning.Api.csproj backend/FamilyLearning.Api/packages.lock.json backend/FamilyLearning.Api/
RUN dotnet restore backend/FamilyLearning.Api --locked-mode
COPY backend/ backend/
RUN dotnet publish backend/FamilyLearning.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=client /src/frontend/dist/family-learning/browser ./wwwroot
# Production never migrates on its own; the container applies pending migrations before each start.
ENTRYPOINT ["/bin/sh", "-c", "dotnet FamilyLearning.Api.dll --migrate && exec dotnet FamilyLearning.Api.dll"]
