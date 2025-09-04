# Sử dụng hình ảnh cơ bản cho .NET 8
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "Webhook_Message.csproj"
RUN dotnet publish "Webhook_Message.csproj" -c Release -o /app/published

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=build /app/published .
ENTRYPOINT ["dotnet", "Webhook_Message.dll"]