# ---------- 1. BUILD STAGE ----------
FROM mcr.microsoft.com/dotnet/sdk:latest AS build
WORKDIR /src

# Copy your local /app folder into the container
COPY ./app ./app
WORKDIR /src/app

# Restore dependencies (like NuGet packages)
RUN dotnet restore

# Build and publish the app for release
RUN dotnet publish -c Release -o /app/publish

# ---------- 2. RUNTIME STAGE ----------
FROM mcr.microsoft.com/dotnet/aspnet:latest
WORKDIR /app

# Copy the published app from the build stage into this final runtime image
COPY --from=build /app/publish .

# Expose port 5000 for HTTP access
EXPOSE 5000

ENV HTTP_PORTS=""
ENV HTTPS_PORTS=""

# Start the app when the container runs
ENTRYPOINT ["dotnet", "app.dll"]