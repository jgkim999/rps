# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY Rps/Rps.csproj Rps/
RUN dotnet restore "Rps/Rps.csproj"

# Copy the rest of the application code
COPY Rps/ Rps/

# Build the application
WORKDIR /src/Rps
RUN dotnet build "Rps.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Rps.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Expose port 5184
EXPOSE 5184

# Copy published application from publish stage
COPY --from=publish /app/publish .

# Set environment variables (can be overridden at runtime)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5184

# Optional: Set default Redis and RabbitMQ connection strings
# These should be overridden at runtime with actual values
ENV Redis__SignalRBackplane=""
ENV Redis__FusionCacheRedisCache=""
ENV Redis__FusionCacheBackplane=""
ENV RabbitMQ__Host=""
ENV RabbitMQ__Username=""
ENV RabbitMQ__Password=""

# Run the application
ENTRYPOINT ["dotnet", "Rps.dll"]
