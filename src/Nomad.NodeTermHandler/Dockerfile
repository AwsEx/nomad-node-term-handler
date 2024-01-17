FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG TARGETPLATFORM
ENV TARGETPLATFORM=${TARGETPLATFORM:-linux/amd64}
ARG NOMADVERSION
ENV NOMADVERSION=${NOMADVERSION:-1.7.3}
WORKDIR /source

# copy csproj and restore as distinct layers
COPY *.csproj .
COPY *.config .
RUN dotnet restore

# copy and publish app and libraries
COPY . .
RUN dotnet publish -o /app *.csproj

WORKDIR /extras
RUN PLATFORM_UNDERSCORE=$(echo $TARGETPLATFORM | sed 's;/;_;g') && \
    apt-get update && apt-get install -y unzip curl && \
    curl -fsSL https://releases.hashicorp.com/nomad/${NOMADVERSION}/nomad_${NOMADVERSION}_${PLATFORM_UNDERSCORE}.zip -o nomad.zip && \
    unzip nomad.zip && \
    rm nomad.zip

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:7.0

WORKDIR /app
COPY --from=build /extras/nomad /usr/local/bin
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["./Nomad.NodeTermHandler"]