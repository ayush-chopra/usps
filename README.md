USPS v3 SDK (.NET 8)

Quick start

1) Start mock server
   dotnet run --project mock/server

2) Run sample against mock (no creds needed)
   export MOCK_SERVER_BASEURL=http://localhost:9091/
   dotnet run --project samples/Usps.Samples

3) Run tests
   dotnet test

Projects
- src/Usps.V3: SDK library with typed clients
- samples/Usps.Samples: Console smoke demo
- tests/Usps.V3.Tests: Unit + contract tests
- mock/server: WireMock.Net standalone mock server

See docs/README.md for detailed usage and setup.

