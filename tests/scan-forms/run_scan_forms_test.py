#!/usr/bin/env python3
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

OUTPUT_PATH = Path(__file__).resolve().parent / "output" / "scan-forms-result.json"
REQUIRED_ENV_KEYS = ["USPS_CLIENT_ID", "USPS_CLIENT_SECRET"]
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
SUPPORTED_MODES = {"label", "mid", "manifest_mid"}


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


def http_post_json(url: str, payload: Dict[str, Any], headers: Optional[Dict[str, str]] = None, timeout: int = 15):
    data = json.dumps(payload).encode("utf-8")
    hdrs = {"Content-Type": "application/json", "Accept": "application/json"}
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


def prune_none(obj: Any) -> Any:
    if isinstance(obj, dict):
        return {k: prune_none(v) for k, v in obj.items() if v is not None}
    if isinstance(obj, list):
        return [prune_none(v) for v in obj if v is not None]
    return obj


def parse_csv(value: Optional[str]) -> List[str]:
    if not value:
        return []
    return [item.strip() for item in value.split(",") if item.strip()]


def parse_bool(value: Optional[str]) -> Optional[bool]:
    if value is None:
        return None
    value = value.strip().lower()
    if value in {"1", "true", "yes", "y"}:
        return True
    if value in {"0", "false", "no", "n"}:
        return False
    return None


def parse_int(value: Optional[str]) -> Optional[int]:
    if value is None or value.strip() == "":
        return None
    try:
        return int(value)
    except ValueError:
        return None


def build_request_body(env: Dict[str, str], errors: List[str]) -> Optional[Dict[str, Any]]:
    today = datetime.utcnow().date().isoformat()
    mode = env.get("USPS_SCAN_FORM_MODE", "label").strip().lower()
    if mode not in SUPPORTED_MODES:
        errors.append(f"Unsupported USPS_SCAN_FORM_MODE '{mode}'. Choose from label, mid, manifest_mid")
        return None

    body: Dict[str, Any] = {}

    if mode == "label":
        label_ids = parse_csv(env.get("USPS_SCAN_LABEL_IDS"))
        tracking_numbers = parse_csv(env.get("USPS_SCAN_LABEL_TRACKINGS"))
        mail_classes = parse_csv(env.get("USPS_SCAN_LABEL_CLASSES"))
        package_counts = parse_csv(env.get("USPS_SCAN_LABEL_COUNTS"))

        max_len = max((len(label_ids), len(tracking_numbers), len(mail_classes), len(package_counts), 0))
        labels: List[Dict[str, Any]] = []
        for idx in range(max_len):
            entry: Dict[str, Any] = {}
            if idx < len(label_ids):
                entry["labelId"] = label_ids[idx]
            if idx < len(tracking_numbers):
                entry["trackingNumber"] = tracking_numbers[idx]
            if idx < len(mail_classes):
                entry["mailClass"] = mail_classes[idx]
            if idx < len(package_counts):
                count_val = parse_int(package_counts[idx])
                if count_val is not None:
                    entry["packageCount"] = count_val
            entry = prune_none(entry)
            if entry:
                labels.append(entry)
        if not labels:
            errors.append("Label mode requires at least one labelId or trackingNumber via USPS_SCAN_LABEL_IDS or USPS_SCAN_LABEL_TRACKINGS")
            return None

        label_payload: Dict[str, Any] = {
            "labels": labels,
            "mailDate": env.get("USPS_SCAN_MAIL_DATE") or today,
            "customerReference": env.get("USPS_SCAN_CUSTOMER_REFERENCE"),
            "shipmentId": env.get("USPS_SCAN_SHIPMENT_ID"),
        }
        body["labelShipment"] = prune_none(label_payload)

    elif mode == "mid":
        mid = env.get("USPS_SCAN_MID") or env.get("USPS_MID")
        if not mid:
            errors.append("MID mode requires USPS_SCAN_MID or USPS_MID")
            return None
        mid_payload: Dict[str, Any] = {
            "mid": mid,
            "crid": env.get("USPS_SCAN_CRID") or env.get("USPS_CRID"),
            "startDate": env.get("USPS_SCAN_START_DATE") or today,
            "endDate": env.get("USPS_SCAN_END_DATE"),
            "timeZone": env.get("USPS_SCAN_TIMEZONE"),
            "includeLabelsWithoutScanForms": parse_bool(env.get("USPS_SCAN_INCLUDE_UNFORMED")),
        }
        body["midShipment"] = prune_none(mid_payload)

    elif mode == "manifest_mid":
        manifest_mid = env.get("USPS_SCAN_MANIFEST_MID")
        mid = env.get("USPS_SCAN_MID") or env.get("USPS_MID")
        if not manifest_mid:
            errors.append("Manifest MID mode requires USPS_SCAN_MANIFEST_MID")
            return None
        manifest_payload: Dict[str, Any] = {
            "manifestMid": manifest_mid,
            "mid": mid,
            "crid": env.get("USPS_SCAN_CRID") or env.get("USPS_CRID"),
            "startDate": env.get("USPS_SCAN_START_DATE") or today,
            "endDate": env.get("USPS_SCAN_END_DATE"),
            "timeZone": env.get("USPS_SCAN_TIMEZONE"),
        }
        body["manifestMidShipment"] = prune_none(manifest_payload)

    contact = prune_none({
        "name": env.get("USPS_SCAN_CONTACT_NAME") or env.get("USPS_CONTACT_NAME"),
        "email": env.get("USPS_SCAN_CONTACT_EMAIL") or env.get("USPS_CONTACT_EMAIL"),
        "phone": env.get("USPS_SCAN_CONTACT_PHONE") or env.get("USPS_CONTACT_PHONE"),
    })
    if contact:
        body["contact"] = contact

    acceptance_address = prune_none({
        "address1": env.get("USPS_SCAN_ACCEPT_ADDRESS1") or env.get("USPS_ACCEPT_ADDRESS1"),
        "address2": env.get("USPS_SCAN_ACCEPT_ADDRESS2") or env.get("USPS_ACCEPT_ADDRESS2"),
        "city": env.get("USPS_SCAN_ACCEPT_CITY") or env.get("USPS_ACCEPT_CITY"),
        "state": env.get("USPS_SCAN_ACCEPT_STATE") or env.get("USPS_ACCEPT_STATE"),
        "postalCode": env.get("USPS_SCAN_ACCEPT_POSTAL") or env.get("USPS_ACCEPT_POSTAL"),
        "countryCode": env.get("USPS_SCAN_ACCEPT_COUNTRY") or env.get("USPS_ACCEPT_COUNTRY"),
    })
    acceptance = prune_none({
        "facilityName": env.get("USPS_SCAN_ACCEPT_FACILITY") or env.get("USPS_ACCEPT_FACILITY"),
        "address": acceptance_address,
        "acceptanceType": env.get("USPS_SCAN_ACCEPT_TYPE"),
    })
    if acceptance:
        body["acceptanceLocation"] = acceptance

    output_options = prune_none({
        "format": env.get("USPS_SCAN_OUTPUT_FORMAT"),
        "includePdf": parse_bool(env.get("USPS_SCAN_INCLUDE_PDF")),
        "includeLink": parse_bool(env.get("USPS_SCAN_INCLUDE_LINK")),
        "copies": parse_int(env.get("USPS_SCAN_OUTPUT_COPIES")),
    })
    if output_options:
        body["outputOptions"] = output_options

    return prune_none(body)


def main() -> int:
    env_file = os.environ.get("ENV_FILE", ".env.local")
    results: Dict[str, Any] = {
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "envFile": env_file,
        "baseUrl": None,
        "authUrl": None,
        "scanFormUrl": None,
        "auth": None,
        "scanForm": None,
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
        scan_url = urllib.parse.urljoin(base_url, "scan-forms/v3/scan-form")
        results["authUrl"] = auth_url
        results["scanFormUrl"] = scan_url

        if not results["errors"]:
            try:
                auth_payload = {
                    "grant_type": "client_credentials",
                    "client_id": env["USPS_CLIENT_ID"],
                    "client_secret": env["USPS_CLIENT_SECRET"],
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

    request_body = None
    if token:
        request_body = build_request_body(env, results["errors"])
        if request_body is None:
            exit_code = 1 if exit_code == 0 else exit_code
    else:
        if not results["errors"]:
            results["errors"].append("Access token not returned from auth response")
        exit_code = 1 if exit_code == 0 else exit_code

    if token and request_body:
        headers = {"Authorization": f"Bearer {token}"}
        try:
            results["scanForm"] = http_post_json(results["scanFormUrl"], request_body, headers=headers)
            if results["scanForm"].get("status") != 200:
                exit_code = 1
        except urllib.error.HTTPError as err:
            body = err.read().decode("utf-8", errors="replace") if err.fp else ""
            results["scanForm"] = {
                "status": err.code,
                "headers": dict(err.headers.items()) if err.headers else {},
                "body": {"raw": body},
            }
            results["errors"].append(f"Scan form request failed with HTTP {err.code}")
            exit_code = 1
        except urllib.error.URLError as err:
            results["scanForm"] = {
                "status": "connection_error",
                "body": {"message": str(err.reason)},
            }
            results["errors"].append(f"Scan form request connection error: {err.reason}")
            exit_code = 1
        except Exception as exc:
            results["scanForm"] = {
                "status": "error",
                "body": {"message": str(exc)},
            }
            results["errors"].append(f"Scan form request failed: {exc}")
            exit_code = 1

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
