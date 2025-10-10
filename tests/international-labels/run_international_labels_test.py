#!/usr/bin/env python3
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

OUTPUT_PATH = Path(__file__).resolve().parent / "output" / "international-labels-result.json"
REQUIRED_ENV_KEYS = ["USPS_CLIENT_ID", "USPS_CLIENT_SECRET", "USPS_PAYMENT_TOKEN"]
KNOWN_ENDPOINTS = {
    "TEM": "https://apis-tem.usps.com/",
    "tem": "https://apis-tem.usps.com/",
    "Tem": "https://apis-tem.usps.com/",
    "CAT": "https://apis-tem.usps.com/",
    "cat": "https://apis-tem.usps.com/",
    "Cat": "https://apis-tem.usps.com/",
    "PROD": "https://apis.usps.com/",
    "prod": "https://apis.usps.com/",
    "Prod": "https://apis.usps.com/",
}


def load_env(path: str) -> Dict[str, str]:
    env: Dict[str, str] = {}
    with open(path, "r", encoding="utf-8") as handle:
        for raw_line in handle:
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            if "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip()
            if value.startswith('"') and value.endswith('"') and len(value) >= 2:
                value = value[1:-1]
            env[key] = value
    return env


def http_post_form(url: str, payload: Dict[str, Any], headers: Optional[Dict[str, str]] = None, timeout: int = 15):
    data = urllib.parse.urlencode(payload).encode("utf-8")
    hdrs = {"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json"}
    if headers:
        hdrs.update(headers)
    request = urllib.request.Request(url, data=data, headers=hdrs, method="POST")
    with urllib.request.urlopen(request, timeout=timeout) as response:
        body_bytes = response.read()
        body_text = body_bytes.decode("utf-8") if body_bytes else ""
        try:
            body_json = json.loads(body_text) if body_text else {}
        except json.JSONDecodeError:
            body_json = {"raw": body_text}
        return {
            "status": response.status,
            "headers": dict(response.headers.items()),
            "body": body_json,
        }


def http_request(url: str, payload: Optional[Dict[str, Any]], headers: Dict[str, str], method: str = "POST", timeout: int = 40):
    data: Optional[bytes] = None
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        hdrs = {"Content-Type": "application/json"}
        hdrs.update(headers)
    else:
        hdrs = dict(headers)
    request = urllib.request.Request(url, data=data, headers=hdrs, method=method)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        body_bytes = response.read()
        body_text = body_bytes.decode("utf-8", errors="replace") if body_bytes else ""
        body_b64 = base64.b64encode(body_bytes).decode("ascii") if body_bytes else ""
        return {
            "status": response.status,
            "headers": dict(response.headers.items()),
            "body": body_text,
            "bodyBase64": body_b64,
        }


def parse_bool(value: Optional[str]) -> Optional[bool]:
    if value is None:
        return None
    lowered = value.strip().lower()
    if lowered in {"true", "1", "yes", "y"}:
        return True
    if lowered in {"false", "0", "no", "n"}:
        return False
    return None


def parse_float(value: Optional[str], default: Optional[float] = None) -> Optional[float]:
    if value is None or value.strip() == "":
        return default
    try:
        return float(value)
    except ValueError:
        return default


def build_default_request(env: Dict[str, str]) -> Dict[str, Any]:
    today = datetime.utcnow().date().isoformat()

    customs_item = {
        "description": env.get("USPS_INT_ITEM_DESC", "T-shirt, cotton"),
        "quantity": int(env.get("USPS_INT_ITEM_QTY", "1")),
        "unitValue": parse_float(env.get("USPS_INT_ITEM_UNIT_VALUE", "20"), 20.0),
        "unitWeight": parse_float(env.get("USPS_INT_ITEM_UNIT_WEIGHT", "0.5"), 0.5),
        "hsTariffNumber": env.get("USPS_INT_ITEM_HS", "610910"),
        "countryOfOrigin": env.get("USPS_INT_ITEM_ORIGIN", "US"),
    }

    return {
        "imageInfo": {
            "imageType": env.get("USPS_INT_IMAGE_TYPE", "PDF"),
            "labelType": env.get("USPS_INT_LABEL_TYPE", "4X6LABEL"),
            "receiptOption": env.get("USPS_INT_RECEIPT_OPTION", "NONE"),
        },
        "fromAddress": {
            "firstName": env.get("USPS_INT_FROM_FIRST", "Acme"),
            "lastName": env.get("USPS_INT_FROM_LAST", "Fulfillment"),
            "streetAddress": env.get("USPS_INT_FROM_STREET", "1100 Wyoming"),
            "secondaryAddress": env.get("USPS_INT_FROM_SECONDARY"),
            "city": env.get("USPS_INT_FROM_CITY", "St. Louis"),
            "state": env.get("USPS_INT_FROM_STATE", "MO"),
            "ZIPCode": env.get("USPS_INT_FROM_ZIP", "63118"),
            "countryCode": env.get("USPS_INT_FROM_COUNTRY", "US"),
            "phone": env.get("USPS_INT_FROM_PHONE"),
            "email": env.get("USPS_INT_FROM_EMAIL"),
        },
        "toAddress": {
            "firstName": env.get("USPS_INT_TO_FIRST", "Alice"),
            "lastName": env.get("USPS_INT_TO_LAST", "Smith"),
            "streetAddress": env.get("USPS_INT_TO_STREET", "10 Downing St"),
            "secondaryAddress": env.get("USPS_INT_TO_SECONDARY"),
            "city": env.get("USPS_INT_TO_CITY", "London"),
            "postalCode": env.get("USPS_INT_TO_POSTAL", "SW1A 2AA"),
            "countryCode": env.get("USPS_INT_TO_COUNTRY", "GB"),
            "phone": env.get("USPS_INT_TO_PHONE"),
            "email": env.get("USPS_INT_TO_EMAIL"),
        },
        "packageDescription": {
            "mailClass": env.get("USPS_INT_MAIL_CLASS", "PRIORITY_MAIL_INTERNATIONAL"),
            "priceType": env.get("USPS_INT_PRICE_TYPE", "COMMERCIAL"),
            "rateIndicator": env.get("USPS_INT_RATE_INDICATOR", "SP"),
            "processingCategory": env.get("USPS_INT_PROCESSING_CATEGORY", "MACHINABLE"),
            "weight": parse_float(env.get("USPS_INT_WEIGHT_LBS", "2.5"), 2.5),
            "length": parse_float(env.get("USPS_INT_DIM_LENGTH", "12")),
            "width": parse_float(env.get("USPS_INT_DIM_WIDTH", "8")),
            "height": parse_float(env.get("USPS_INT_DIM_HEIGHT", "4")),
            "mailingDate": env.get("USPS_INT_MAILING_DATE", today),
        },
        "customs": {
            "contentsType": env.get("USPS_INT_CONTENTS", "MERCHANDISE"),
            "invoiceNumber": env.get("USPS_INT_INVOICE"),
            "totalValue": parse_float(env.get("USPS_INT_TOTAL_VALUE", "20"), 20.0),
            "currencyCode": env.get("USPS_INT_CURRENCY", "USD"),
            "nonDeliveryOption": env.get("USPS_INT_NON_DELIVERY", "RETURN"),
            "senderSignatureName": env.get("USPS_INT_DECLARANT", "Jane Smith"),
            "senderSignatureDate": env.get("USPS_INT_DECLARANT_DATE", today),
            "items": [customs_item],
        },
        "extraServices": [int(code) for code in env.get("USPS_INT_EXTRA_SERVICES", "").split(",") if code.strip().isdigit()],
        "reference": env.get("USPS_INT_REFERENCE"),
    }


def save_label_artifact(headers: Dict[str, Any], body_b64: str) -> Optional[str]:
    content_type = headers.get("Content-Type")
    if not content_type:
        return None

    if content_type.startswith("application/json") or not body_b64:
        return None

    extension = "pdf" if "pdf" in content_type.lower() else "tif"
    output_dir = OUTPUT_PATH.parent
    output_dir.mkdir(parents=True, exist_ok=True)
    file_path = output_dir / f"international-label.{extension}"
    try:
        file_path.write_bytes(base64.b64decode(body_b64))
    except Exception:
        return None
    return str(file_path)


def main() -> int:
    env_file = os.environ.get("ENV_FILE", ".env.local")
    results: Dict[str, Any] = {
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "envFile": env_file,
        "baseUrl": None,
        "authUrl": None,
        "labelUrl": None,
        "auth": None,
        "label": None,
        "errors": [],
    }
    exit_code = 0

    try:
        env = load_env(env_file)
    except FileNotFoundError:
        results["errors"].append(f"Env file '{env_file}' was not found")
        OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT_PATH.write_text(json.dumps(results, indent=2), encoding="utf-8")
        return 1
    except OSError as exc:
        results["errors"].append(f"Failed to read env file: {exc}")
        OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT_PATH.write_text(json.dumps(results, indent=2), encoding="utf-8")
        return 1

    missing = [key for key in REQUIRED_ENV_KEYS if not env.get(key)]
    if missing:
        results["errors"].append(f"Missing required env values: {', '.join(missing)}")
        exit_code = 1

    raw_base = env.get("USPS_BASE_URL") or env.get("USPS_API_BASEURL") or env.get("MOCK_SERVER_BASEURL")
    usps_env = env.get("USPS_ENV")
    if usps_env:
        override = KNOWN_ENDPOINTS.get(usps_env) or KNOWN_ENDPOINTS.get(usps_env.upper())
        if override and (not raw_base or "localhost" in raw_base or raw_base.rstrip("/").endswith("9091")):
            raw_base = override
    if not raw_base:
        results["errors"].append("Could not determine USPS API base URL from env")
        exit_code = 1
    else:
        base_url = raw_base.rstrip("/") + "/"
        results["baseUrl"] = base_url
        auth_url = urllib.parse.urljoin(base_url, "oauth2/v3/token")
        label_url = urllib.parse.urljoin(base_url, "international-labels/v3/international-label")
        results["authUrl"] = auth_url
        results["labelUrl"] = label_url

        try:
            auth_payload = {
                "grant_type": "client_credentials",
                "client_id": env.get("USPS_CLIENT_ID", ""),
                "client_secret": env.get("USPS_CLIENT_SECRET", ""),
            }
            results["auth"] = http_post_form(auth_url, auth_payload)
        except urllib.error.HTTPError as err:
            body = err.read().decode("utf-8", errors="replace") if err.fp else ""
            results["auth"] = {
                "status": err.code,
                "headers": dict(err.headers.items()) if err.headers else {},
                "body": {"raw": body},
            }
            results["errors"].append(f"Auth request failed with HTTP {err.code}")
            exit_code = 1
        except urllib.error.URLError as err:
            results["auth"] = {
                "status": "connection_error",
                "body": {"message": str(err.reason)},
            }
            results["errors"].append(f"Auth request connection error: {err.reason}")
            exit_code = 1
        except Exception as exc:
            results["auth"] = {
                "status": "error",
                "body": {"message": str(exc)},
            }
            results["errors"].append(f"Auth request failed: {exc}")
            exit_code = 1

    token = None
    if results.get("auth") and isinstance(results["auth"], dict):
        body = results["auth"].get("body")
        if isinstance(body, dict):
            token = body.get("access_token")

    if not token:
        if not results["errors"]:
            results["errors"].append("Access token not returned from auth response")
        exit_code = 1

    if token and results.get("labelUrl"):
        label_payload = build_default_request(env)
        headers = {
            "Authorization": f"Bearer {token}",
            "X-Payment-Authorization-Token": env["USPS_PAYMENT_TOKEN"],
            "Accept": "application/json, multipart/mixed"
        }

        try:
            results["label"] = http_request(results["labelUrl"], label_payload, headers=headers, method="POST", timeout=60)
            if results["label"].get("status") != 200:
                exit_code = 1
            else:
                artifact_path = save_label_artifact(results["label"].get("headers", {}), results["label"].get("bodyBase64", ""))
                if artifact_path:
                    results["label"]["savedLabel"] = artifact_path
        except urllib.error.HTTPError as err:
            body = err.read().decode("utf-8", errors="replace") if err.fp else ""
            results["label"] = {
                "status": err.code,
                "headers": dict(err.headers.items()) if err.headers else {},
                "body": {"raw": body},
            }
            results["errors"].append(f"International label request failed with HTTP {err.code}")
            exit_code = 1
        except urllib.error.URLError as err:
            results["label"] = {
                "status": "connection_error",
                "body": {"message": str(err.reason)},
            }
            results["errors"].append(f"International label request connection error: {err.reason}")
            exit_code = 1
        except Exception as exc:
            results["label"] = {
                "status": "error",
                "body": {"message": str(exc)},
            }
            results["errors"].append(f"International label request failed: {exc}")
            exit_code = 1

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
