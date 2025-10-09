# USPS **Domestic Labels 3.0** — Comprehensive Guide
_Last updated: 2025-10-09_

> Purpose: Create **domestic USPS shipping labels** (PDF or TIFF) with IMpb barcodes for products like **USPS Ground Advantage**, **Priority Mail / Express**, **Parcel Select (incl. Destination Entry & Lightweight)**, **Media/Library Mail**, **Bound Printed Matter**, etc. The API supports **payment via EPS/Permit**, optional **label edits (JSON Patch)**, **returns labels**, and **cancellation/refunds**. It pairs with **Payments 3.0** (to authorize funds), **Domestic Prices 3.0** (to estimate rates and SKUs), **SCAN Forms 3.0** (acceptance scans), and **Tracking 3.0**. 

---

## At a Glance
- **Create a label:** `POST /labels/v3/label` (requires OAuth Bearer token **and** a **Payments 3.0** payment-authorization token). citeturn5view0  
- **Edit a label:** JSON Patch `PATCH /labels/v3/label/{{labelId}}` with constraints (some scenarios must refund + recreate). citeturn6view0  
- **Create a returns label:** `POST /labels/v3/return-label`. citeturn7search10  
- **Void / refund a label:** `DELETE /labels/v3/label/{{trackingNumber}}` (cancel). citeturn7search10  
- **Generate First‑Class indicia (letters/flats):** `PATCH /labels/v3/indicia` (multipart response with metadata + PDF). citeturn6view0  
- **Formats returned:** Label image as **PDF or TIFF**; some endpoints return **multipart/mixed** with a JSON metadata part + binary label image (see “Responses & Files”). citeturn0search14

**Where this is documented**
- USPS Dev Portal Domestic Labels page (cloud). citeturn7search1turn7search2
- USPS official **api‑examples** repo (cURL + example bodies). citeturn5view0
- **Payments 3.0** for payment authorization tokens (valid ~8 hours). citeturn0search2turn0search8
- IMpb barcode and file obligations (DMM 204 / Publication 199). citeturn0search5turn0search9

---

## Environments & Base URLs
- **Production:** `https://apis.usps.com`  
- **Test (TEM/CAT):** `https://apis-tem.usps.com`  
_Use the same paths; only the hostname changes._ citeturn1view0

---

## Authentication & Payment
### 1) OAuth 2.0 (Bearer) — required for all calls
Obtain an OAuth **access token** using **client_credentials** and include it in `Authorization: Bearer <token>`. citeturn1view0

### 2) Payments 3.0 — required for label purchase
Before calling **Domestic Labels**, obtain a **payment authorization token** by posting your **roles** (at minimum `PAYER`, often `LABEL_OWNER`) with EPS/Permit details to:  
`POST /payments/v3/payment-authorization` → returns `paymentAuthorizationToken` (put this value in header `X-Payment-Authorization-Token`). citeturn5view0

**Minimum payment fields (from USPS examples)**
- `accountType`: `"EPS"` or `"PERMIT"`  
- `accountNumber`: your EPS account or permit number  
- `permit ZIP Code`: required only for **PERMIT** flows citeturn5view0turn7search10

> **Tip:** Tokens expire; rotate them (Payments tokens are typically valid ~8 hours). citeturn0search2

---

## Core Endpoints

### Create a Label
```
POST /labels/v3/label
Headers:
  Authorization: Bearer <OAuthToken>
  X-Payment-Authorization-Token: <PaymentToken>
  Content-Type: application/json
```
**Representative request skeleton** (merge with “Request Model” below): citeturn5view0
```json
{
  "imageInfo": {
    "imageType": "PDF",
    "labelType": "4X6LABEL",
    "receiptOption": "NONE",
    "suppressPostage": false,
    "suppressMailDate": false,
    "returnLabel": false
  },
  "toAddress": { "firstName": "Jane", "lastName": "Doe", "streetAddress": "1100 Wyoming", "city": "St. Louis", "state": "MO", "ZIPCode": "63118" },
  "fromAddress": { "firstName": "John", "lastName": "Smith", "streetAddress": "4120 Bingham Ave", "city": "St. Louis", "state": "MO", "ZIPCode": "63118" },
  "packageDescription": {
    "mailClass": "USPS_GROUND_ADVANTAGE",
    "rateIndicator": "SP",
    "processingCategory": "MACHINABLE",
    "weight": 1.0,
    "length": 10.0, "width": 6.0, "height": 4.0,
    "girth": 0.5
  },
  "extraServices": [ ],
  "priceType": "COMMERCIAL",
  "mailingDate": "2025-10-09"
}
```

**Successful response (high level):** JSON metadata (standardized address, routing info, **trackingNumber**, **SKU**, **postage**, extras, zone, commitment) + **label image** (PDF/TIFF). citeturn6view0

---

### Edit a Label (JSON Patch)
```
PATCH /labels/v3/label/{labelId}
Headers:
  Authorization: Bearer <OAuthToken>
  X-Payment-Authorization-Token: <PaymentToken>
  Content-Type: application/json
Body: JSON Patch operations
```
- Supports **selective edits** (weight/dims, processing category, containers, rateIndicator).  
- **Not allowed** in certain cases — e.g., original label had `suppressPostage=false`, **cubic softpack** dimension edits, or converting **cubic ↔ non‑cubic**. In those cases, **refund + recreate**. citeturn6view0

**Example ops**: see “cURL Recipes → Edit Weight/Dimensions”. citeturn6view0

---

### Create a **Returns** Label
```
POST /labels/v3/return-label
```
Generates a **domestic returns** label (paid or merchant‑pay, depending on your setup). Use the same OAuth and Payment headers. citeturn7search10

---

### **Void / Cancel** a Label (Refund)
```
DELETE /labels/v3/label/{trackingNumber}
```
Cancels an unused label (refund rules depend on product/account). Provide the **tracking number** of the label to void. citeturn7search10

---

### **Indicia** for Letters/Flats (Optional)
```
PATCH /labels/v3/indicia
```
For First‑Class **letters/flats** postage imprimatur. Response is **multipart**: JSON **indiciaMetadata** + **receiptImage.pdf**. citeturn6view0

---

## Request Model — Common Fields

### `imageInfo`
Field | Type | Notes
---|---|---
`imageType` | enum | `"PDF"` or `"TIF"` (a/k/a TIFF). citeturn7search2
`labelType` | enum | Common: `"4X6LABEL"`, `"4X4LABEL"`. citeturn5view0
`receiptOption` | enum | `"NONE"`, `"SEPARATE_PAGE"` (separate receipt PDF in some flows). citeturn6view0
`suppressPostage` | boolean | Hide printed amount (for negotiated accounts/returns). citeturn5view0
`suppressMailDate` | boolean | Hide the mail date on the face.
`returnLabel` | boolean | Marks the created label as a **return**. citeturn5view0

### `toAddress` / `fromAddress`
- Name components, street lines, city, state, ZIP5/ZIP+4. USPS normalizes casing and may return standardized values (e.g., `STE` for `Suite`). citeturn6view0

### `packageDescription`
- `mailClass` (product), `rateIndicator` (program), `processingCategory`, weight/dimensions/girth, non‑machinable flags, optional container metadata. citeturn5view0

### `extraServices`
- Array of **extra service codes** (Signature, Insurance, etc.). Validate with Prices 3.0 if needed.

### Pricing/Payment
- `priceType`: `"COMMERCIAL"` or `"RETAIL"` (requires appropriate authorization).  
- `mailingDate` influences price table selection and commitments. citeturn5view0

---

## Responses & Files

### Typical Label Response
- JSON metadata containing: `labelAddress` (standardized), `routingInformation`, **`trackingNumber`**, **`postage`**, **`SKU`**, `extraServices[]`, `zone`, `commitment` ETA window, `weight/weightUOM`, `fees[]`, `constructCode`, `labelBrokerID` (if applicable). citeturn6view0
- Label image as **PDF/TIFF**. Some responses are delivered as **multipart/mixed** where one part is JSON and the other is the binary label image; parse by boundary and save the file. citeturn0search14

**IMpb & electronic file obligations**  
Commercial parcels must meet **IMpb** barcode standards and submit piece‑level data per DMM 204 / Pub 199; labels not meeting IMpb specs may be rejected. citeturn0search5turn0search11

---

## Common Flows (End‑to‑End)

1) **Quote, Choose, Then Buy**  
   - Use **Domestic Prices 3.0** to compute base/extras or combine via **total-rates**, choose the SKU, then **create the label** with matching `mailClass`/indicators. citeturn1view0

2) **Create → SCAN → Track**  
   - Create labels, then create a **SCAN Form 3.0** to generate acceptance events for all packages at drop‑off, then monitor via **Tracking 3.0**. citeturn1view0turn0search6turn0search16

3) **Edit or Void**  
   - If minor corrections are needed, attempt **PATCH**. If disallowed (e.g., cubic → non‑cubic) or label is printed with visible postage, **void** and recreate. citeturn6view0

---

## Error Handling & Troubleshooting
HTTP | Likely cause | What to check
---|---|---
400 | Invalid body/combination | Required fields, `mailClass`/`rateIndicator` pairing, image type/size, address validity.
401 | Missing/expired OAuth | Refresh token.
403 | Payments/entitlement | Missing or expired **paymentAuthorizationToken**; EPS/Permit settings mismatch. citeturn5view0
409 | Edit constraints | Scenario requires refund + new label (see edit limitations). citeturn6view0
415 | Multipart/format issues | If parsing multipart responses, ensure boundary handling; label image is **binary**. citeturn0search14
5xx | USPS outage | Retry with exponential backoff.

**Debug tips**
- Always log the **multipart boundary** and save both parts when responses are multipart. citeturn0search14  
- Confirm **Payments token** scope covers the caller roles (`PAYER`, `LABEL_OWNER`) for the CRID/MID you are using. citeturn5view0  
- Validate IMpb compliance if labels scan poorly in the network. citeturn0search5

---

## cURL Recipes

### A) Authorize Payment (EPS)
```bash
curl -X POST "https://apis.usps.com/payments/v3/payment-authorization"   -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json"   -d '{
    "roles": [
      { "roleName": "PAYER", "CRID": "XXXX", "MID": "XXXXXXX", "manifestMID": "XXXXXXX",
        "accountType": "EPS", "accountNumber": "XXXXXXXXXX" },
      { "roleName": "LABEL_OWNER", "CRID": "XXXX", "MID": "XXXXXXX", "manifestMID": "XXXXXXX",
        "accountType": "EPS", "accountNumber": "XXXXXXXXXX" }
    ]
  }'
# → save .paymentAuthorizationToken
```
citeturn5view0

### B) Create Label (4x6 PDF)
```bash
curl -X POST "https://apis.usps.com/labels/v3/label"   -H "Authorization: Bearer $TOKEN"   -H "X-Payment-Authorization-Token: $PAYMENTTOKEN"   -H "Content-Type: application/json"   -d '{
    "imageInfo": { "imageType": "PDF", "labelType": "4X6LABEL", "receiptOption": "NONE",
                   "suppressPostage": false, "suppressMailDate": false, "returnLabel": false },
    "toAddress": { "firstName":"Jane", "lastName":"Doe", "streetAddress":"1100 Wyoming",
                   "city":"St. Louis","state":"MO","ZIPCode":"63118" },
    "fromAddress": { "firstName":"John", "lastName":"Smith","streetAddress":"4120 Bingham Ave",
                     "city":"St. Louis","state":"MO","ZIPCode":"63118" },
    "packageDescription": { "mailClass":"USPS_GROUND_ADVANTAGE", "rateIndicator":"SP",
                            "processingCategory":"MACHINABLE",
                            "weight":1.0, "length":10, "width":6, "height":4, "girth":0.5 },
    "priceType":"COMMERCIAL","mailingDate":"2025-10-09"
  }'
```
citeturn5view0

### C) Edit Weight/Dimensions
```bash
curl -X PATCH "https://apis.usps.com/labels/v3/label/{labelId}"   -H "Authorization: Bearer $TOKEN"   -H "X-Payment-Authorization-Token: $PAYMENTTOKEN"   -H "Content-Type: application/json"   -d '[
    {"op":"replace","path":"/packageDescription/weight","value":1.25},
    {"op":"replace","path":"/packageDescription/length","value":11}
  ]'
```
citeturn6view0

### D) Create a Returns Label
```bash
curl -X POST "https://apis.usps.com/labels/v3/return-label"   -H "Authorization: Bearer $TOKEN"   -H "X-Payment-Authorization-Token: $PAYMENTTOKEN"   -H "Content-Type: application/json"   -d '{ "imageInfo": { "imageType": "PDF", "labelType": "4X6LABEL" }, "toAddress": { /* ... */ }, "fromAddress": { /* ... */ }, "packageDescription": { /* ... */ } }'
```
citeturn7search10

### E) Void a Label (Refund)
```bash
curl -X DELETE "https://apis.usps.com/labels/v3/label/{trackingNumber}"   -H "Authorization: Bearer $TOKEN"   -H "X-Payment-Authorization-Token: $PAYMENTTOKEN"
```
citeturn7search10

### F) First‑Class **Indicia** (letters/flats)
```bash
curl -X PATCH "https://apis.usps.com/labels/v3/indicia"   -H "Authorization: Bearer $TOKEN"   -H "X-Payment-Authorization-Token: $PAYMENTTOKEN"   -H "Content-Type: application/json"   -d '{
    "indiciaDescription": {
      "processingCategory":"LETTERS", "weight":3.5, "mailingDate":"2025-10-09",
      "length":6.0, "height":4.0, "thickness":0.25
    },
    "imageInfo": { "imageType":"PDF", "receiptOption":"SEPARATE_PAGE" }
  }'
# Response is multipart: JSON 'indiciaMetadata' + 'receiptImage.pdf' part.
```
citeturn6view0

---

## Pairing With Other USPS APIs
- **Payments 3.0**: get the payment token used in `X-Payment-Authorization-Token`. citeturn0search2  
- **Domestic Prices 3.0**: price ahead of purchase; ensure inputs (class, rate indicator) match.  
- **SCAN Forms 3.0**: create PS 5630/3152 manifest to trigger acceptance scans for all labels.  
- **Tracking 3.0**: show live package status to customers. citeturn0search16

---

## Compliance Notes (IMpb)
- **IMpb** barcode and electronic file are required for commercial parcels; see **DMM 204** and **Publication 199**. Poor quality labels or missing data can lead to surcharges or rejection. citeturn0search5turn0search11

---

## Migration Notes
- **Web Tools** legacy Labels APIs are retiring in **January 2026**. Use the cloud **USPS APIs** shown above and the official migration guide. citeturn0search6

---

## Appendix — Supported Products (examples)
USPS lists Labels 3.0 coverage including **USPS Ground Advantage**, **Parcel Select (Destination Entry, Lightweight)**, **Connect Local/Regional**, **Priority Mail / Express**, **First‑Class Package Service**, **BPM**, **Library**, **Media**. citeturn5view0
