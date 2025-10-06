USPS Mock Server (WireMock.Net)

Start:

  dotnet run --project mock/server

Defaults to http://localhost:9091

Routes:
- POST /addresses/standardize
- POST /prices/domestic
- POST /prices/international
- POST /servicestandards/lookup
- GET  /servicestandards/files
- POST /labels/domestic (Accept: application/pdf|image/svg+xml|text/plain)
- POST /shippingoptions/quote

Examples:

  curl -X POST http://localhost:9091/addresses/standardize -H 'Content-Type: application/json' -d '{"addresses":[{"addressLine1":"475 L\'Enfant Plaza SW","city":"Washington","state":"DC"}]}'

  curl -X POST http://localhost:9091/labels/domestic -H 'Accept: application/pdf' -H 'Content-Type: application/json' -d '{"service":"Ground Advantage","format":"pdf","shipFrom":{"name":"A","addressLine1":"1","city":"NY","state":"NY","zipCode":"10001"},"shipTo":{"name":"B","addressLine1":"2","city":"SF","state":"CA","zipCode":"94105"},"weightOz":8}' --output label.pdf

