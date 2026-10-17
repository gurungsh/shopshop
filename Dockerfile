# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG DLL
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
RUN mkdir logs && chown "$APP_UID" logs
COPY --from=build /app/publish .
USER $APP_UID
ENV APP_DLL=$DLL
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]
