Security Guidelines

- Do not commit secrets. Use User Secrets locally, CI secrets in pipelines.
- Never log Client ID/Secret or tokens. Logging includes correlation IDs, status codes, and timings only.
- Rotate credentials immediately if exposure is suspected.
- Validate all request inputs with FluentValidation.
- Follow least-privilege for CI variables.
- Review USPS release notes for breaking changes and update safely.

