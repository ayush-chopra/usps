# USPS v3 REST Integration – Full Roadmap (C#/.NET 8)

> **Purpose**: A complete, copy‑ready plan for generating a **new, empty** repository that integrates the **six modern USPS v3 REST APIs**:
> - Addresses 3.0
> - Domestic Prices 3.0
> - International Prices 3.0
> - Service Standards 3.0
> - Domestic Labels 3.0
> - Shipping Options 3.0
>
> This file is written for a code‑gen model (e.g., GPT‑5 Codex) to create **production‑grade** code, and for a senior developer to review and integrate safely.

---

## 0) Before You Start — Accounts, Tools, & Access

### USPS Accounts & Access
- [ ] USPS **Business Customer Gateway** account created.
- [ ] USPS **Developer Portal** app created → obtain **Client ID** and **Client Secret** (a.k.a. Consumer Key/Secret).
- [ ] Enable **Public Access I** (default tier) for the six APIs above.
- [ ] Record your **CRID** and **MID** for your organization.
- [ ] (Labels) If generating shipping labels: set up an **Enterprise Payment Account (EPA)**; confirm ability to obtain a **Payments token** if required for Labels 3.0.
- [ ] Confirm both **TEM** (test) and **PROD** access are visible in the portal.

### Workstation Requirements
- [ ] **.NET SDK 8.x** installed (`dotnet --version`).
- [ ] **Git** installed and configured.
- [ ] An editor/IDE (VS Code / Visual Studio 2022+).
- [ ] Optional: **Docker** if you wish to containerize builds/tests later.

### Repository Policies (Security & DX)
- [ ] No secrets in source control; use **User Secrets** (local) and CI **Secrets** (remote).
- [ ] Enforce **code style**, **analyzers**, and **linting** (EditorConfig + Roslyn analyzers).
- [ ] Centralized **logging** (Microsoft.Extensions.Logging) with correlation IDs.
- [ ] **Retries + timeouts** via Polly; **circuit‑breaker** optional.
- [ ] **Unit + integration tests**; integration tests run only when USPS test credentials are present.
- [ ] **CI pipeline** with cached restores, unit tests, (optional) integration tests, and coverage.

---

## 1) Repository Scaffold (Clean Slate)

Create solution and three projects:

```
Usps.sln
├─ src/
│  └─ Usps.V3/                 # Class Library (SDK)
├─ samples/
│  └─ Usps.Samples/            # Console demo
└─ tests/
   └─ Usps.V3.Tests/           # xUnit tests
```

### Commands to Generate
```bash
dotnet new sln -n Usps

# SDK
dotnet new classlib -n Usps.V3 -o src/Usps.V3
dotnet sln add src/Usps.V3/Usps.V3.csproj

# Sample console app
dotnet new console -n Usps.Samples -o samples/Usps.Samples
dotnet sln add samples/Usps.Samples/Usps.Samples.csproj
dotnet add samples/Usps.Samples reference src/Usps.V3/Usps.V3.csproj

# Tests
dotnet new xunit -n Usps.V3.Tests -o tests/Usps.V3.Tests
dotnet sln add tests/Usps.V3.Tests/Usps.V3.Tests.csproj
dotnet add tests/Usps.V3.Tests reference src/Usps.V3/Usps.V3.csproj
```

### NuGet Packages
```bash
# Core
dotnet add src/Usps.V3 package Microsoft.Extensions.Http
dotnet add src/Usps.V3 package Microsoft.Extensions.Options.ConfigurationExtensions
dotnet add src/Usps.V3 package System.Text.Json
dotnet add src/Usps.V3 package Polly
dotnet add src/Usps.V3 package Polly.Extensions.Http
dotnet add src/Usps.V3 package FluentValidation

# Logging
dotnet add src/Usps.V3 package Microsoft.Extensions.Logging.Abstractions

# Tests
dotnet add tests/Usps.V3.Tests package FluentAssertions
dotnet add tests/Usps.V3.Tests package coverlet.collector
```

Optional (source‑gen perf):
```bash
dotnet add src/Usps.V3 package System.Text.Json.SourceGeneration
```

---

## 2) Configuration Model & Environments

Create `UspsOptions` (in `src/Usps.V3/Options/UspsOptions.cs`):

```csharp
public enum UspsEnvironment { Tem, Prod }

public sealed class UspsOptions
{
    public UspsEnvironment Environment { get; set; } = UspsEnvironment.Tem;

    // Base API root
    public string BaseUrl { get; set; } = "https://apis-tem.usps.com/v3/";

    // OAuth token endpoint
    public string OAuthTokenUrl { get; set; } = "https://apis-tem.usps.com/oauth2/v3/token";

    // App creds
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Optional (Labels payments token etc.)
    public bool PaymentsEnabled { get; set; } = false;
    public string? PaymentsTokenUrl { get; set; }
}
```

**Environment switching logic** (executed during DI registration):
- If `UspsEnvironment.Prod` → `BaseUrl = "https://apis.usps.com/v3/"`, `OAuthTokenUrl = "https://apis.usps.com/oauth2/v3/token"`.
- Allow overrides via env vars, appsettings, or direct code configuration.

### Required Environment Variables (local/dev)
```
USPS_ENV=Tem        # or Prod
USPS_CLIENT_ID=xxxxx
USPS_CLIENT_SECRET=xxxxx
```

Use **User Secrets** for local dev:
```bash
dotnet user-secrets init --project samples/Usps.Samples
dotnet user-secrets set USPS_CLIENT_ID "<id>" --project samples/Usps.Samples
dotnet user-secrets set USPS_CLIENT_SECRET "<secret>" --project samples/Usps.Samples
dotnet user-secrets set USPS_ENV "Tem" --project samples/Usps.Samples
```

---

## 3) OAuth2 – Client Credentials Token Client

Create interface `IUspsTokenClient` and implementation `UspsTokenClient` with safe caching.

**Key Requirements**
- POST JSON to `OAuthTokenUrl` with `{ grant_type: "client_credentials", client_id, client_secret }`.
- Parse `access_token` & `expires_in` (seconds).
- Cache token in `IMemoryCache`; **refresh at ~70% lifetime**; guard with `SemaphoreSlim` to prevent stampedes.
- Never log secrets; log only status/latency and correlation IDs.

**Token Client Sketch**
```csharp
public interface IUspsTokenClient
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
```

Create an `AuthDelegatingHandler` that injects `Authorization: Bearer <token>` on every request.

---

## 4) Resilience, Timeouts, & Correlation

Add a **Polly** policy:
- Retries on transient errors (**5xx**) and **429** (rate limits) with **exponential backoff + jitter**.
- Global request **timeout** ~10s (configurable by client).
- Capture/propagate **X-Correlation-ID** header; generate a GUID if absent.

**Polly Skeleton**
```csharp
public static class PollyPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> Default() =>
        HttpPolicyExtensions.HandleTransientHttpError()
        .OrResult(r => (int)r.StatusCode == 429)
        .WaitAndRetryAsync(6, retryAttempt =>
            TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt)));
}
```

---

## 5) API Client Interfaces (Six v3 APIs)

Create one **typed client** per API, with interface + implementation. Keep **paths configurable** in case USPS adjusts routes.

> **Base Address** is `UspsOptions.BaseUrl`, e.g., `https://apis-tem.usps.com/v3/`.

### 5.1 Addresses 3.0
**Interface**
```csharp
public interface IAddressesClient
{
    Task<StandardizeAddressResponse> StandardizeAsync(StandardizeAddressRequest req, CancellationToken ct = default);
}
```

**Endpoints (typical)**
- `POST addresses/standardize` (bulk supported).

**Request (example)**
```csharp
public sealed class StandardizeAddressRequest
{
    public List<AddressInput> Addresses { get; init; } = new();
}
public sealed class AddressInput
{
    public string addressLine1 { get; init; } = "";
    public string? addressLine2 { get; init; }
    public string? city { get; init; }
    public string? state { get; init; }
    public string? zipCode { get; init; }
}
```

### 5.2 Domestic Prices 3.0
- `POST prices/domestic` → rate quotes (weight, dims, origin ZIP, destination ZIP, service group).

### 5.3 International Prices 3.0
- `POST prices/international` → rate quotes (country, weight/dims, content type/customs).

### 5.4 Service Standards 3.0
- `POST servicestandards/lookup` → ETA/service windows.
- `GET servicestandards/files` → links for bulk files (optional).

### 5.5 Domestic Labels 3.0
- `POST labels/domestic` → returns **PDF/ZPL/SVG** label bytes (+ metadata like tracking).
- For **Payments** (if required by your account): acquire a payments token separately and include per spec.
- Content negotiation via `Accept` header (e.g., `application/pdf`, `image/svg+xml`, `text/plain` for ZPL).

### 5.6 Shipping Options 3.0
- `POST shippingoptions/quote` → returns **available options + prices + service standards** in one call.

> **Validation**: Use **FluentValidation** for each request type (weights, units, ZIPs/country codes, required fields).

---

## 6) Dependency Injection (One‑liner AddUspsV3)

Create `ServiceCollectionExtensions`:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUspsV3(this IServiceCollection services, Action<UspsOptions> configure)
    {
        services.Configure(configure);
        services.AddMemoryCache();

        // Token client
        services.AddHttpClient<IUspsTokenClient, UspsTokenClient>();
        services.AddTransient<AuthDelegatingHandler>();

        void AddClient<TClient, TImpl>()
            where TImpl : class, TClient
        {
            services.AddHttpClient<TClient, TImpl>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<UspsOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<AuthDelegatingHandler>()
            .AddPolicyHandler(PollyPolicies.Default());
        }

        AddClient<IAddressesClient, AddressesClient>();
        AddClient<IDomesticPricesClient, DomesticPricesClient>();
        AddClient<IInternationalPricesClient, InternationalPricesClient>();
        AddClient<IServiceStandardsClient, ServiceStandardsClient>();
        AddClient<IDomesticLabelsClient, DomesticLabelsClient>();
        AddClient<IShippingOptionsClient, ShippingOptionsClient>();

        return services;
    }
}
```

---

## 7) Sample Console (End‑to‑End Smoke)

Entry point in `samples/Usps.Samples/Program.cs`:
1. Load `UspsOptions` from env/User Secrets.
2. Register services via `AddUspsV3(...)` (`Environment = Tem` by default).
3. **Flow**:
   - Standardize 1–2 addresses.
   - Query **Shipping Options 3.0** (gets options + prices + standards in one call).
   - (If enabled) Generate **Domestic Label (PDF)** → write to `./out/label.pdf`.
4. Log token expiry, retry attempts, and correlation ID.

Run:
```bash
dotnet run --project samples/Usps.Samples
```

---

## 8) Testing

### Unit Tests
- DTO serialization round‑trips (camelCase).
- Validators reject bad inputs (e.g., missing ZIP/State).
- Polly policy test: given 429 + 5xx results → retry count reached.

### Integration Tests (TEM)
- **Skipped by default** unless env vars present:
  - `USPS_CLIENT_ID`, `USPS_CLIENT_SECRET`, `USPS_ENV=Tem`
- Scenarios:
  - Addresses: valid input returns standardized output.
  - ShippingOptions: returns at least one option for a realistic package.
  - Labels (optional): only if your account has permissions; otherwise test harness should **Skip** gracefully.

Run:
```bash
dotnet test --configuration Release
```

---

## 9) CI/CD (GitHub Actions Example)

Create `.github/workflows/ci.yml`:
- Triggers: PRs to `main`, pushes to `main`.
- Jobs:
  1. **build-and-test**
     - Setup .NET 8
     - `dotnet restore`
     - `dotnet build -c Release`
     - `dotnet test -c Release --collect:"XPlat Code Coverage"`
  2. **integration-tests** (optional)
     - Needs secrets: `USPS_CLIENT_ID`, `USPS_CLIENT_SECRET`
     - Runs integration test subset with `USPS_ENV=Tem`

---

## 10) Security & Compliance Notes

- Secrets must **never** be printed or committed.
- Rotate keys if exposed; require reviewers for changes touching auth, endpoints, or payments.
- Validate and sanitize all input (especially label shipments).
- Persist logs with correlation IDs for traceability; mask PII where appropriate.
- Periodically review **USPS API Release Notes** to adapt to breaking changes (fields, routes, product names).

---

## 11) Release Checklist (PROMOTE TO PROD)

- [ ] All unit tests green; integration tests green in **TEM**.
- [ ] Confirm rate‑limit behavior under load (observe 429 backoff).
- [ ] Confirm token refresh works and never deadlocks.
- [ ] For **Labels**: EPA and Payments token confirmed; sample label generated.
- [ ] Switch `Environment = Prod` **only** after approvals; set Prod secrets in CI.
- [ ] Smoke test in Prod (small volume) before enabling full traffic.
- [ ] Tag release; generate changelog notes.

---

## 12) “Hand‑Off” Snippets for Application Teams

### DI Registration (app)
```csharp
services.AddUspsV3(opts =>
{
    opts.Environment = UspsEnvironment.Tem; // switch to Prod later
    opts.ClientId = Environment.GetEnvironmentVariable("USPS_CLIENT_ID")!;
    opts.ClientSecret = Environment.GetEnvironmentVariable("USPS_CLIENT_SECRET")!;
});
```

### Example Usage
```csharp
var standardized = await addressesClient.StandardizeAsync(new StandardizeAddressRequest
{
    Addresses = new()
    {
        new AddressInput { addressLine1 = "1600 Amphitheatre Pkwy", city = "Mountain View", state = "CA", zipCode = "94043" }
    }
});
```

---

## 13) Known Gotchas

- USPS may adjust field names/validation; keep **routes and models configurable** and watch **release notes**.
- Quotas vary by access tier; implement **429** handling from day one.
- Labels often need **extra onboarding** (EPA/Payments). Support feature‑flagging and graceful skip of label tests.
- MIME handling: PDF/ZPL/SVG; ensure correct `Accept` headers and file writes.
- Timeouts: do not rely on default infinite timeouts; set per‑client limits.

---

## 14) What the Code‑Gen Model Must Produce

- [ ] **Usps.V3** library with: Options, Token client, DelegatingHandler, Typed clients (6), DTOs, Validators, Polly policies, Exceptions, Logging.
- [ ] **Usps.Samples** console showing end‑to‑end usage (address → options → (optional) label).
- [ ] **Usps.V3.Tests** with unit + opt‑in integration tests.
- [ ] **Docs**: `README.md`, `USPS-SETUP.md`, `SECURITY.md`, `CHANGELOG.md`.
- [ ] **CI** workflow with unit tests (and optional integration tests when secrets are present).

---

### Appendix – Suggested Folder Layout (Library)

```
src/Usps.V3/
├─ Options/
│  └─ UspsOptions.cs
├─ Auth/
│  ├─ IUspsTokenClient.cs
│  ├─ UspsTokenClient.cs
│  └─ AuthDelegatingHandler.cs
├─ Http/
│  ├─ PollyPolicies.cs
│  └─ UspsException.cs
├─ Clients/
│  ├─ IAddressesClient.cs / AddressesClient.cs
│  ├─ IDomesticPricesClient.cs / DomesticPricesClient.cs
│  ├─ IInternationalPricesClient.cs / InternationalPricesClient.cs
│  ├─ IServiceStandardsClient.cs / ServiceStandardsClient.cs
│  ├─ IDomesticLabelsClient.cs / DomesticLabelsClient.cs
│  └─ IShippingOptionsClient.cs / ShippingOptionsClient.cs
├─ Models/
│  ├─ Addresses/
│  ├─ PricesDomestic/
│  ├─ PricesInternational/
│  ├─ ServiceStandards/
│  ├─ Labels/
│  └─ ShippingOptions/
├─ Validation/
│  └─ (FluentValidation validators)
└─ ServiceCollectionExtensions.cs
```

This roadmap is intentionally **implementation‑ready**. Feed it to your code‑gen model to scaffold the repo, then have your senior dev wire credentials, validate endpoints against the USPS Developer Portal, and run the TEM integration tests.
