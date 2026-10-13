# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy directory configuration files
COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

# Copy project files for caching restore
COPY ["src/MindLift.Shared/MindLift.Shared.csproj", "src/MindLift.Shared/"]
COPY ["src/MindLift.Domain/MindLift.Domain.csproj", "src/MindLift.Domain/"]
COPY ["src/MindLift.Application/MindLift.Application.csproj", "src/MindLift.Application/"]
COPY ["src/MindLift.Infrastructure/MindLift.Infrastructure.csproj", "src/MindLift.Infrastructure/"]
COPY ["src/MindLift.UI/MindLift.UI.csproj", "src/MindLift.UI/"]
COPY ["src/MindLift.Web/MindLift.Web.csproj", "src/MindLift.Web/"]
COPY ["src/MindLift.Portal/MindLift.Portal.csproj", "src/MindLift.Portal/"]
COPY ["src/MindLift.Api/MindLift.Api.csproj", "src/MindLift.Api/"]

# Restore packages
RUN dotnet restore "src/MindLift.Api/MindLift.Api.csproj"

# Copy all source files
COPY src/ src/

# Publish Api (bundles MindLift.Web and MindLift.Portal static assets)
RUN dotnet publish "src/MindLift.Api/MindLift.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install Tesseract OCR with English and Arabic language data
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        tesseract-ocr \
        tesseract-ocr-eng \
        tesseract-ocr-ara && \
    rm -rf /var/lib/apt/lists/*

# Environment defaults for production container
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Ocr__TesseractPath=/usr/bin/tesseract
ENV Ocr__Languages=eng+ara

# Copy published application
COPY --from=build /app/publish .

# Run as non-root user
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "MindLift.Api.dll"]
