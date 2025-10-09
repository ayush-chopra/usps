#!/usr/bin/env python3
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, Optional

OUTPUT_PATH = Path(__file__).resolve().parent / "output" / "shipping-options-result.json"
REQUIRED_ENV_KEYS = ["USPS_CLIENT_ID", "USPS_CLIENT_SECRET"]
KNOWN_ENDPOINTS = {
    "TEM": "https://apis-tem.usps.com/",
    "Tem": "https://apis-tem.usps.com/",
    "tem": "https://apis-tem.usps.com/",
    "PROD": "https://apis.usps.com/",
    "Prod": "https://apis.usps.com/",
    "prod": "https://apis.usps.com/",
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


def http_post(url: str, payload: Dict[str, Any], headers: Optional[Dict[str, str]] = None, timeout: int = 15):
    data = json.dumps(payload).encode("utf-8")
    hdrs = {"Content-Type": "application/json"}
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


def parse_float(value: Optional[str]) -> Optional[float]:
    if value is None or value == "":
        return None
    try:
        return float(value)
    except ValueError:
        return None


def parse_int_list(value: Optional[str]) -> Optional[list[int]]:
    if not value:
        return None
    items: list[int] = []
    for segment in value.split(","):
        segment = segment.strip()
        if not segment:
            continue
        try:
            items.append(int(segment))
        except ValueError:
            continue
    return items or None


def prune_none(obj: Any) -> Any:
    if isinstance(obj, dict):
        return {k: prune_none(v) for k, v in obj.items() if v is not None}
    if isinstance(obj, list):
        return [prune_none(v) for v in obj if v is not None]
    return obj


def main() -> int:
    env_file = os.environ.get("ENV_FILE", ".env.local")
    results: Dict[str, Any] = {
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "envFile": env_file,
        "baseUrl": None,
        "authUrl": None,
        "shippingOptionsUrl": None,
        "auth": None,
        "shippingOptions": None,
        "errors": [],
    }
    exit_code = 0

    try:
        env = load_env(env_file)
    except FileNotFoundError:
        results["errors"].append(f"Env file '{env_file}' was not found")
        exit_code = 1
        env = {}
    except OSError as exc:
        results["errors"].append(f"Failed to read env file: {exc}")
        exit_code = 1
        env = {}

    missing = [key for key in REQUIRED_ENV_KEYS if not env.get(key)]
    if missing:
        results["errors"].append(f"Missing required env values: {', '.join(missing)}")
        exit_code = 1
    else:
        raw_base = env.get("USPS_BASE_URL") or env.get("USPS_API_BASEURL") or env.get("MOCK_SERVER_BASEURL")
        usps_env = env.get("USPS_ENV")
        if usps_env:
            env_override = KNOWN_ENDPOINTS.get(usps_env, KNOWN_ENDPOINTS.get(usps_env.upper()))
            if env_override and (not raw_base or "localhost" in raw_base or raw_base.rstrip("/").endswith("9091")):
                raw_base = env_override
        if not raw_base:
            results["errors"].append("Could not determine USPS API base URL from env")
            exit_code = 1
        else:
            base_url = raw_base.rstrip("/") + "/"
            results["baseUrl"] = base_url
            auth_url = urllib.parse.urljoin(base_url, "oauth2/v3/token")
            shipping_url = urllib.parse.urljoin(base_url, "shipments/v3/options/search")
            results["authUrl"] = auth_url
            results["shippingOptionsUrl"] = shipping_url

            try:
                auth_payload = {
                    "grant_type": "client_credentials",
                    "client_id": env["USPS_CLIENT_ID"],
                    "client_secret": env["USPS_CLIENT_SECRET"],
                }
                results["auth"] = http_post(auth_url, auth_payload)
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
            if results["auth"] and isinstance(results["auth"], dict):
                body = results["auth"].get("body")
                if isinstance(body, dict):
                    token = body.get("access_token")

            if token:
                today = datetime.utcnow().date().isoformat()
                extra_services = parse_int_list(env.get("USPS_EXTRA_SERVICES"))
                default_mail_class = env.get("USPS_MAIL_CLASS") or "ALL"
                package_description: Dict[str, Any] = {
                    "weight": parse_float(env.get("USPS_PACKAGE_WEIGHT"))
                        or parse_float(env.get("USPS_WEIGHT_LBS"))
                        or parse_float(env.get("USPS_WEIGHT_OZ"))
                        or 2.0,
                    "length": parse_float(env.get("USPS_DIM_LENGTH")) or 8.0,
                    "height": parse_float(env.get("USPS_DIM_HEIGHT")) or 4.0,
                    "width": parse_float(env.get("USPS_DIM_WIDTH")) or 6.0,
                    "girth": parse_float(env.get("USPS_DIM_GIRTH")),
                    "mailClass": default_mail_class,
                    "extraServices": extra_services,
                    "mailingDate": env.get("USPS_MAILING_DATE") or today,
                    "packageValue": parse_float(env.get("USPS_PACKAGE_VALUE")),
                }
                package_description = prune_none(package_description)

                pricing_option: Dict[str, Any] = {}
                price_type = env.get("USPS_PRICE_TYPE") or "COMMERCIAL"
                if price_type:
                    pricing_option["priceType"] = price_type
                account_number = env.get("USPS_ACCOUNT_NUMBER")
                account_type = env.get("USPS_ACCOUNT_TYPE") or ("EPS" if account_number else None)
                if account_number:
                    pricing_option["paymentAccount"] = {
                        "accountType": account_type,
                        "accountNumber": account_number,
                    }

                payload: Dict[str, Any] = {
                    "originZIPCode": env.get("USPS_ORIGIN_ZIP", "10018"),
                    "destinationZIPCode": env.get("USPS_DESTINATION_ZIP", "95823"),
                    "packageDescription": package_description,
                }
                origin_country = env.get("USPS_ORIGIN_COUNTRY_CODE")
                destination_country = env.get("USPS_DESTINATION_COUNTRY_CODE")
                if origin_country:
                    payload["originCountryCode"] = origin_country
                if destination_country:
                    payload["destinationCountryCode"] = destination_country
                pricing_options = prune_none(pricing_option)
                if pricing_options:
                    payload["pricingOptions"] = [pricing_options]

                payload = prune_none(payload)

                try:
                    results["shippingOptions"] = http_post(
                        shipping_url,
                        payload,
                        headers={
                            "Authorization": f"Bearer {token}",
                            "Accept": "application/json",
                        },
                    )
                    if results["shippingOptions"].get("status") != 200:
                        exit_code = 1
                except urllib.error.HTTPError as err:
                    body = err.read().decode("utf-8", errors="replace") if err.fp else ""
                    results["shippingOptions"] = {
                        "status": err.code,
                        "headers": dict(err.headers.items()) if err.headers else {},
                        "body": {"raw": body},
                    }
                    results["errors"].append(f"Shipping options request failed with HTTP {err.code}")
                    exit_code = 1
                except urllib.error.URLError as err:
                    results["shippingOptions"] = {
                        "status": "connection_error",
                        "body": {"message": str(err.reason)},
                    }
                    results["errors"].append(f"Shipping options request connection error: {err.reason}")
                    exit_code = 1
                except Exception as exc:
                    results["shippingOptions"] = {
                        "status": "error",
                        "body": {"message": str(exc)},
                    }
                    results["errors"].append(f"Shipping options request failed: {exc}")
                    exit_code = 1
            else:
                if not results["errors"]:
                    results["errors"].append("Access token not returned from auth response")
                exit_code = 1 if exit_code == 0 else exit_code

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(results, indent=2), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
