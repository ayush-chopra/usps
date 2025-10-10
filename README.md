USPS v3 SDK (.NET Framework 4.8)
==================================

Overview
--------
This repository contains a .NET Framework 4.8 SDK for interacting with the USPS v3 APIs. It includes processors for domestic and international pricing, labels, shipping options, scan forms, service standards, and related utilities. The code targets **.NET Framework 4.8** and is designed for deployment on Windows environments where the full framework is available.

Prerequisites
-------------
- Windows with the **.NET Framework 4.8 Developer Pack** installed
- Visual Studio 2019 (16.11) or Visual Studio 2022 with .NET desktop workload
- Optional: `MSBuild.exe` (ships with Visual Studio) and `vstest.console.exe` for command-line builds/tests

Solution Layout
---------------
- `Usps.sln` &mdash; Visual Studio solution targeting .NET Framework 4.8
- `api/` &mdash; USPS API processors (address validation, pricing, labels, etc.)
- `label/` &mdash; Markdown documentation for specific label flows
- `tests/` &mdash; Integration scripts and harnesses for exercising the processors

Building
--------
### Visual Studio
1. Open `Usps.sln` in Visual Studio.
2. Select **Build ➜ Build Solution** (or press `Ctrl+Shift+B`).

### Command line (MSBuild)
```cmd
"%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" Usps.sln /p:Configuration=Release /p:Platform="Any CPU"
```
Adjust the MSBuild path to match your installed Visual Studio edition.

Testing
-------
The repository currently provides Python integration harnesses under `tests/` that exercise the USPS APIs against live endpoints. These scripts require real USPS OAuth and Payments credentials. To run them:

1. Populate `.env.local` with the required USPS environment variables (client ID, secret, payment token, etc.).
2. Execute the desired script, for example:
   ```bash
   python tests/domestic-labels/run_domestic_labels_test.py
   python tests/international-labels/run_international_labels_test.py
   ```
3. Results are written to the corresponding `tests/**/output/*.json` files along with any saved label artifacts.

For unit/integration tests written in .NET (if added later), run them via Visual Studio Test Explorer or `vstest.console.exe`.

API Highlights
--------------
- Address validation and normalization
- Domestic and international pricing processors
- Shipping options and service standards lookups
- SCAN form creation and management
- Domestic and international label generation with Payments 3.0 integration
