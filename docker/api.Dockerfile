# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
ARG SERVICE_PROJECT
WORKDIR /src

COPY Directory.Build.props ./
COPY DigitalDealsCRM.slnx ./
COPY src ./src
COPY tests ./tests
COPY tools ./tools

RUN dotnet restore "${SERVICE_PROJECT}"

FROM restore AS publish
ARG SERVICE_PROJECT
RUN dotnet publish "${SERVICE_PROJECT}" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=publish /app/publish ./

ENTRYPOINT ["dotnet"]
