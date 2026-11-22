# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# TRaSH Guides definitions (DESIGN.md section 7).
RUN git clone --depth 1 --filter=blob:none --sparse https://github.com/TRaSH-Guides/Guides.git /trash \
 && git -C /trash sparse-checkout set docs/json/sonarr

WORKDIR /src
COPY Overviewarr.slnx ./
COPY src/Overviewarr/Overviewarr.csproj src/Overviewarr/
COPY tests/Overviewarr.Tests/Overviewarr.Tests.csproj tests/Overviewarr.Tests/
RUN dotnet restore Overviewarr.slnx

COPY . .
RUN TRASH_DIR=/trash/docs/json/sonarr dotnet test Overviewarr.slnx --no-restore
RUN dotnet publish src/Overviewarr/Overviewarr.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra

WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=build /trash/docs/json/sonarr/cf ./trash/cf
COPY --from=build /trash/docs/json/sonarr/cf-groups ./trash/cf-groups
COPY --from=build /trash/LICENSE ./trash/LICENSE

ENV ASPNETCORE_HTTP_PORTS=7979
EXPOSE 7979

ENTRYPOINT ["dotnet", "Overviewarr.dll"]
