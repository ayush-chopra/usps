USPS v3 SDK for .NET 8

What’s included
- Typed clients for six USPS v3 APIs
- Resilience (Polly), DI, options, logging
- Local mock server (WireMock.Net) + fixtures
- Sample console app (no USPS creds needed)
- Unit + contract tests

Run the mock server
- dotnet run --project mock/server
- Default base URL: http://localhost:9091/

Run the sample (against mock)
- export MOCK_SERVER_BASEURL=http://localhost:9091/
- dotnet run --project samples/Usps.Samples
- Optional: export USPS_LABELS_ENABLED=true to save a PDF label to ./out/label.pdf

Switching to TEM/PROD later
- Set USPS_ENV=Tem or Prod
- Provide USPS_CLIENT_ID and USPS_CLIENT_SECRET via env or User Secrets
- Unset MOCK_SERVER_BASEURL to use real base URL

Available environment variables
- USPS_ENV=Tem
- USPS_CLIENT_ID=__set_in_user_secrets_or_CI__
- USPS_CLIENT_SECRET=__set_in_user_secrets_or_CI__
- USPS_LABELS_ENABLED=false
- MOCK_SERVER_BASEURL=http://localhost:9091/

