FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .
RUN dotnet tool restore
RUN dotnet restore smr-map-collection.fsproj
RUN dotnet publish smr-map-collection.fsproj -c release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0

COPY --from=build /app /app
USER ubuntu
WORKDIR /work

ENTRYPOINT ["/app/smr-map-collection"]