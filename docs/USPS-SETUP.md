USPS Developer Setup

1) Create USPS Business Customer Gateway account
2) In USPS Developer Portal, create an app and obtain Client ID/Secret
3) Enable Public Access I for:
   - Addresses 3.0
   - Domestic Prices 3.0
   - International Prices 3.0
   - Service Standards 3.0
   - Domestic Labels 3.0
   - Shipping Options 3.0
4) Record your CRID and MID
5) For Labels, set up an Enterprise Payment Account (EPA) as needed

Local secrets
- Use .NET User Secrets for local dev
  dotnet user-secrets init --project samples/Usps.Samples
  dotnet user-secrets set USPS_CLIENT_ID "<id>" --project samples/Usps.Samples
  dotnet user-secrets set USPS_CLIENT_SECRET "<secret>" --project samples/Usps.Samples

CI secrets
- Store USPS_CLIENT_ID and USPS_CLIENT_SECRET as repository secrets

Environments
- TEM base: https://apis-tem.usps.com/v3/
- PROD base: https://apis.usps.com/v3/
- Token TEM: https://apis-tem.usps.com/oauth2/v3/token
- Token PROD: https://apis.usps.com/oauth2/v3/token

