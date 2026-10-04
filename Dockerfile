# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies (enables layer caching)
COPY *.csproj ./
RUN dotnet restore

# Copy remaining files and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# .NET 8 ASP.NET images default to port 8080 non-root execution
EXPOSE 8080

# Replace 'MyDotNetApi' with the name of your generated project DLL
ENTRYPOINT ["dotnet", "MusicPlatform.API.dll"]