# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first so restore is cached independently of source changes.
COPY src/RailApi/RailApi.csproj src/RailApi/
RUN dotnet restore src/RailApi/RailApi.csproj

COPY . .
RUN dotnet publish src/RailApi/RailApi.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user.
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "RailApi.dll"]
