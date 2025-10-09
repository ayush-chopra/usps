# USPS **SCAN Forms 3.0** — Comprehensive Guide
_Last updated: 2025-10-09_

> Purpose: Programmatically create a **Shipment Confirmation Acceptance Notice (SCAN) form** that links multiple USPS labels under one **Electronic File Number (EFN)**. USPS scans PS Form **5630** (or **3152**) at acceptance, which generates an **acceptance tracking event** for every linked label. citeturn1view0turn2view0

---

## At a Glance
- **What you get:** A single SCAN manifest (EFN) covering many labels; when USPS scans the paper form (PS 5630/3152) at drop‑off, it **creates acceptance scans** for all packages on that form. citeturn1view0turn4view0  
- **Why it matters:** Faster retail acceptance (one scan for many packages), consistent **Acceptance** events in Tracking, fewer counter transactions. citeturn1view0  
- **Who can use it:** Any USPS APIs integrator with OAuth access; often paired with **Domestic/International Labels 3.0** (which generate the SSF/labels you’ll aggregate). citeturn0search7turn0search11

---

## Environments & Base URLs
- **Production:** `https://apis.usps.com`  
- **Test (TEM/CAT):** `https://apis-tem.usps.com`  
_Use the same paths; only the hostname changes. USPS migrated V3 APIs to cloud on **Aug 24, 2025**._ citeturn4view0turn0search18

---

## Authentication (OAuth 2.0)
All calls require an **OAuth 2.0** Bearer token in the `Authorization` header. Tokens expire and must be refreshed. citeturn0search16

**Token**
```http
POST https://apis.usps.com/oauth2/v3/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=<KEY>&client_secret=<SECRET>
```
_Then call SCAN endpoints with `Authorization: Bearer <token>`. citeturn0search16_

---

## Endpoints
> The SCAN API supports **three creation modes** that feed the same endpoint. citeturn6view0

### Create SCAN Form (Label Shipment)
```
POST /scan-forms/v3/scan-form
```
**Use when:** You have a **set of specific labels** to include (e.g., you just printed today’s labels).  
**cURL (example):** citeturn6view0
```bash
curl -X POST "https://apis.usps.com/scan-forms/v3/scan-form"   -H "Authorization: Bearer $TOKEN"   -H "Content-Type: application/json"   -d @SCANForm-LabelShipment-request.json
```
**Output:** JSON with SCAN form details (including the generated **EFN**); you then render/print **PS 5630/3152** from the response content or link and present it at acceptance. citeturn4view0

---

### Create SCAN Form (MID Shipment)
```
POST /scan-forms/v3/scan-form
```
**Use when:** You want USPS to **gather all labels for a Mailer ID (MID)** across a time window (commonly “today”), instead of listing label IDs yourself.  
**cURL (example):** citeturn6view0
```bash
curl -X POST "https://apis.usps.com/scan-forms/v3/scan-form"   -H "Authorization: Bearer $TOKEN"   -H "Content-Type: application/json"   -d @SCANForm-MIDShipment-request.json
```

---

### Create SCAN Form (Manifest MID Shipment)
```
POST /scan-forms/v3/scan-form
```
**Use when:** Your operation uses a **Manifest MID** distinct from the label owner MID (common for consolidators/3PL workflows).  
**cURL (example):** citeturn6view0
```bash
curl -X POST "https://apis.usps.com/scan-forms/v3/scan-form"   -H "Authorization: Bearer $TOKEN"   -H "Content-Type: application/json"   -d @SCANForm-ManifestMIDShipment-request.json
```

> All three modes post to the **same** endpoint; the **request body** determines whether you’re supplying explicit labels or instructing USPS to collect labels by **MID**/**manifestMID**. See the official examples referenced above. citeturn6view0

---

## Request Model — Common Concepts
While the exact JSON differs by mode, requests typically include:
- **Identity**: your **CRID/MID**/**manifestMID** and/or an explicit **list of labels** to include. (Label‑based requests usually pass label/IMpb identifiers; MID-based requests pass MIDs and a filter window.) citeturn6view0  
- **Mailing window**: date/time or day‑level selection for which labels to aggregate. citeturn6view0  
- **Contact & acceptance info**: shipper/contact name, phone, and **acceptance location** details to print on PS 5630. (Varies by account set‑up.) citeturn1view0  
- **Output preferences**: PDF/TIFF and page options where supported (implementation detail may vary; check your app entitlement & examples). citeturn4view0

---

## Response Anatomy
The response includes (representative):
- **EFN** (Electronic File Number) for the SCAN, which ties all labels on the form. citeturn1view0  
- **Form artifact** (or link/content) for **PS 5630/3152** to print and hand over at acceptance. citeturn4view0  
- **Counts/labels summarized** on the form (e.g., number of packages per class/service).  
- **Warnings/errors** for labels that could not be included (e.g., missing SSF).

USPS scanning the PS 5630/3152 at induction creates **Acceptance** tracking events for all linked labels. citeturn1view0

---

## Required Prerequisites
- **OAuth** access and a USPS **Developer Portal** application. citeturn0search16  
- Ability to create **labels** (Domestic or International Labels 3.0)—the SCAN form aggregates labels already created and associated to your CRID/MID. citeturn0search7turn0search11  
- Correct **MID/manifestMID** association to your label transactions (especially for MID‑based SCAN). citeturn6view0

---

## Error Handling & Troubleshooting
HTTP | Likely cause | What to check
---|---|---
400 | Invalid body/unsupported combination | Ensure you’re using a supported mode (labels vs MID), include required identity fields, and restrict to **today’s** labels unless your entitlement supports wider windows. citeturn6view0
401 | Missing/expired token | Refresh OAuth token and resend. citeturn0search16
403 | Not entitled | Verify your app’s SCAN entitlement; confirm CRID/MID is authorized.
404 | No labels found | For MID‑based requests, the window/MID produced no eligible labels.
409 | Duplicate | You attempted to create a second SCAN for the **same logical set**; either reuse the EFN or adjust selection.
5xx | USPS outage | Retry with exponential backoff; monitor status.

**Operational tips**
- Prefer **Label Shipment** when your workflow already has the list of labels in hand; it’s deterministic and easiest to debug. citeturn6view0  
- For **MID Shipment**, ensure labels are generated under the **same MID/manifestMID** you pass to SCAN (and within the chosen window). citeturn6view0  
- Always print and present the returned **PS 5630/3152**; a SCAN EFN alone does not create acceptance events—the **paper form must be scanned** at induction. citeturn1view0

---

## Pairing With Other APIs
- **Domestic Labels 3.0** / **International Labels 3.0** — create the labels you’ll add to SCAN; these also generate the **Shipping Services File** entries that SCAN ties together. citeturn0search7turn0search11  
- **Tracking 3.0** — verify **Acceptance** events after USPS scans your SCAN form. citeturn0search6

---

## Migration Notes
USPS **Web Tools** legacy SCAN API is being retired by **Jan 25, 2026**; migrate to SCAN Forms 3.0 on USPS APIs. citeturn0search5

---

## Example Files (from USPS GitHub)
- `SCANForm-LabelShipment-request.json` / `...-response.json`  
- `SCANForm-MIDShipment-request.json` / `...-response.json`  
- `SCANForm-ManifestMIDShipment-request.json` / `...-response.json`  
cURL examples and file names are in the official USPS examples repo. citeturn6view0

---

## cURL Recipe (copy/paste)
```bash
# Create a SCAN form by explicit label list
curl -X POST "https://apis.usps.com/scan-forms/v3/scan-form"   -H "Authorization: Bearer $TOKEN"   -H "Content-Type: application/json"   -d @SCANForm-LabelShipment-request.json

# Create a SCAN form by MID (gather labels for your MID)
curl -X POST "https://apis.usps.com/scan-forms/v3/scan-form"   -H "Authorization: Bearer $TOKEN"   -H "Content-Type: application/json"   -d @SCANForm-MIDShipment-request.json
```

---

## Glossary
- **EFN** — Electronic File Number; unique identifier for a SCAN form. citeturn1view0  
- **PS 5630 / PS 3152** — Paper SCAN manifests scanned at acceptance (5630: standard; 3152: consolidated). citeturn2view0  
- **MID / manifestMID** — Mailer ID used for label ownership/induction; **manifestMID** may differ for consolidators. citeturn6view0
