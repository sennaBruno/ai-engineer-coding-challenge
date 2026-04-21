# syntax=docker/dockerfile:1.7

# ---------- Stage 1: build + publish the .NET 10 Web API ----------
#
# Uses the full SDK image. Restore is its own layer so package pulls
# only re-run when the csproj changes, not on every source edit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/src/Api/Api.csproj backend/src/Api/
RUN dotnet restore backend/src/Api/Api.csproj

COPY backend/src/Api/ backend/src/Api/
RUN dotnet publish backend/src/Api/Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# ---------- Stage 2: slim ASP.NET Core runtime ----------
#
# Only the ASP.NET shared framework, no SDK. ~3x smaller than the build
# image. curl is added for the HEALTHCHECK; ~2 MB.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# .NET 8+ base images ship a non-root `app` user (UID 64198) by default.
# Reuse it instead of creating a second one.

COPY --from=build /app/publish ./

# Bake the SOP document into the image so /api/ingest works out of the
# box. Explicit file path, not a whole-dir copy — prevents scratch files
# (notes.txt, .DS_Store, PDF drafts) that someone drops into
# knowledge-base/ from shipping inside the image.
# In a real deployment the document would come from object storage or a
# document service, not a container layer.
COPY knowledge-base/Grocery_Store_SOP.md /app/knowledge-base/

# Writable data dir for the JSON vector store. Mounted as a named volume
# in compose so ingested embeddings survive container restarts.
RUN mkdir -p /app/Data && chown -R app:app /app

USER app

EXPOSE 5181

ENV ASPNETCORE_URLS=http://+:5181 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    Challenge__SourceDocumentPath=/app/knowledge-base/Grocery_Store_SOP.md \
    Challenge__VectorStoreJsonPath=/app/Data/vector-store.json

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent http://localhost:5181/api/health || exit 1

ENTRYPOINT ["dotnet", "GroceryStore.Chatbot.Api.dll"]
