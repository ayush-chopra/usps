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

OUTPUT_PATH = Path(__file__).resolve().parent / "output" / "international-prices-result.json"
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
        "baseRatesUrl": None,
        "baseRatesListUrl": None,
        "extraServiceUrl": None,
        "totalRatesUrl": None,
        "auth": None,
        "baseRates": None,
        "baseRatesList": None,
        "extraServiceRates": None,
        "totalRates": None,
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
            env_override = KNOWN_ENDPOINTS.get(usps_env) or KNOWN_ENDPOINTS.get(usps_env.upper())
            if env_override and (not raw_base or "localhost" in raw_base or raw_base.rstrip("/").endswith("9091")):
                raw_base = env_override
        if not raw_base:
            results["errors"].append("Could not determine USPS API base URL from env")
            exit_code = 1
        else:
            base_url = raw_base.rstrip("/") + "/"
            results["baseUrl"] = base_url
            auth_url = urllib.parse.urljoin(base_url, "oauth2/v3/token")
            base_rates_url = urllib.parse.urljoin(base_url, "international-prices/v3/base-rates/search")
            base_rates_list_url = urllib.parse.urljoin(base_url, "international-prices/v3/base-rates-list/search")
            extra_service_url = urllib.parse.urljoin(base_url, "international-prices/v3/extra-service-rates/search")
            total_rates_url = urllib.parse.urljoin(base_url, "international-prices/v3/total-rates/search")
            results["authUrl"] = auth_url
            results["baseRatesUrl"] = base_rates_url
            results["baseRatesListUrl"] = base_rates_list_url
            results["extraServiceUrl"] = extra_service_url
            results["totalRatesUrl"] = total_rates_url

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
            auth_body = results["auth"].get("body") if isinstance(results["auth"], dict) else None
            if isinstance(auth_body, dict):
                token = auth_body.get("access_token")

            if token:
                today = datetime.utcnow().date().isoformat()
                default_weight = parse_float(env.get("USPS_PACKAGE_WEIGHT"))
                if default_weight is None:
                    default_weight = parse_float(env.get("USPS_WEIGHT_LBS"))
                if default_weight is None:
                    default_weight = parse_float(env.get("USPS_WEIGHT_OZ"))
                if default_weight is None:
                    default_weight = 2.0

                base_rates_payload: Dict[str, Any] = {
                    "originZIPCode": env.get("USPS_ORIGIN_ZIP", "22407"),
                    "foreignPostalCode": env.get("USPS_FOREIGN_POSTAL", "10109"),
                    "destinationCountryCode": env.get("USPS_DESTINATION_COUNTRY_CODE", "CA"),
                    "destinationEntryFacilityType": env.get("USPS_DEST_ENTRY_FACILITY", "NONE"),
                    "weight": default_weight,
                    "length": parse_float(env.get("USPS_DIM_LENGTH")) or 9.0,
                    "width": parse_float(env.get("USPS_DIM_WIDTH")) or 6.0,
                    "height": parse_float(env.get("USPS_DIM_HEIGHT")) or 4.0,
                    "mailClass": env.get("USPS_MAIL_CLASS", "PRIORITY_MAIL_INTERNATIONAL"),
                    "processingCategory": env.get("USPS_PROCESSING_CATEGORY", "MACHINABLE"),
                    "rateIndicator": env.get("USPS_RATE_INDICATOR", "SP"),
                    "priceType": env.get("USPS_PRICE_TYPE", "COMMERCIAL"),
                    "accountType": env.get("USPS_ACCOUNT_TYPE", "EPS"),
                    "accountNumber": env.get("USPS_ACCOUNT_NUMBER", "1234567890"),
                    "mailingDate": env.get("USPS_MAILING_DATE", today),
                }
                base_rates_payload = prune_none(base_rates_payload)

                base_rates_list_payload: Dict[str, Any] = {
                    "originZIPCode": base_rates_payload.get("originZIPCode"),
                    "foreignPostalCode": base_rates_payload.get("foreignPostalCode"),
                    "destinationCountryCode": base_rates_payload.get("destinationCountryCode"),
                    "weight": default_weight,
                    "length": base_rates_payload.get("length"),
                    "width": base_rates_payload.get("width"),
                    "height": base_rates_payload.get("height"),
                    "mailClass": env.get("USPS_MAIL_CLASS", "PRIORITY_MAIL_INTERNATIONAL"),
                    "priceType": env.get("USPS_PRICE_TYPE", "COMMERCIAL"),
                    "accountType": base_rates_payload.get("accountType"),
                    "accountNumber": base_rates_payload.get("accountNumber"),
                    "mailingDate": base_rates_payload.get("mailingDate"),
                }
                base_rates_list_payload = prune_none(base_rates_list_payload)

                total_rates_payload: Dict[str, Any] = {
                    "originZIPCode": base_rates_payload.get("originZIPCode"),
                    "foreignPostalCode": base_rates_payload.get("foreignPostalCode"),
                    "destinationCountryCode": base_rates_payload.get("destinationCountryCode"),
                    "weight": default_weight,
                    "length": base_rates_payload.get("length"),
                    "width": base_rates_payload.get("width"),
                    "height": base_rates_payload.get("height"),
                    "mailClass": base_rates_payload.get("mailClass"),
                    "priceType": base_rates_payload.get("priceType"),
                    "mailingDate": base_rates_payload.get("mailingDate"),
                    "accountType": base_rates_payload.get("accountType"),
                    "accountNumber": base_rates_payload.get("accountNumber"),
                    "itemValue": parse_float(env.get("USPS_ITEM_VALUE")) or 300.0,
                    "extraServices": parse_int_list(env.get("USPS_EXTRA_SERVICES")) or None,
                    "processingCategory": base_rates_payload.get("processingCategory"),
                    "rateIndicator": base_rates_payload.get("rateIndicator"),
                }
                total_rates_payload = prune_none(total_rates_payload)

                headers = {
                    "Authorization": f"Bearer {token}",
                }

                try:
                    results["baseRates"] = http_post_json(base_rates_url, base_rates_payload, headers=headers)
                    if results["baseRates"].get("status") != 200:
                        exit_code = 1
                except urllib.error.HTTPError as err:
                    body = err.read().decode("utf-8", errors="replace") if err.fp else ""
                    results["baseRates"] = {
                        "status": err.code,
                        "headers": dict(err.headers.items()) if err.headers else {},
                        "body": {"raw": body},
                    }
                    results["errors"].append(f"Base rates request failed with HTTP {err.code}")
                    exit_code = 1
                except urllib.error.URLError as err:
                    results["baseRates"] = {
                        "status": "connection_error",
                        "body": {"message": str(err.reason)},
                    }
                    results["errors"].append(f"Base rates request connection error: {err.reason}")
                    exit_code = 1
                except Exception as exc:
                    results["baseRates"] = {
                        "status": "error",
                        "body": {"message": str(exc)},
                    }
                    results["errors"].append(f"Base rates request failed: {exc}")
                    exit_code = 1

                try:
                    results["baseRatesList"] = http_post_json(base_rates_list_url, base_rates_list_payload, headers=headers)
                    if results["baseRatesList"].get("status") != 200:
                        exit_code = 1
                except urllib.error.HTTPError as err:
                    body = err.read().decode("utf-8", errors="replace") if err.fp else ""
                    results["baseRatesList"] = {
                        "status": err.code,
                        "headers": dict(err.headers.items()) if err.headers else {},
                        "body": {"raw": body},
                    }
                    results["errors"].append(f"Base rates list request failed with HTTP {err.code}")
                    exit_code = 1
                except urllib.error.URLError as err:
                    results["baseRatesList"] = {
                        "status": "connection_error",
                        "body": {"message": str(err.reason)},
                    }
                    results["errors"].append(f"Base rates list connection error: {err.reason}")
                    exit_code = 1
                except Exception as exc:
                    results["baseRatesList"] = {
                        "status": "error",
                        "body": {"message": str(exc)},
                    }
                    results["errors"].append(f"Base rates list request failed: {exc}")
                    exit_code = 1

                try:
                    results["totalRates"] = http_post_json(total_rates_url, total_rates_payload, headers=headers)
                    if results["totalRates"].get("status") != 200:
                        exit_code = 1
                except urllib.error.HTTPError as err:
                    body = err.read().decode("utf-8", errors="replace") if err.fp else ""
                    results["totalRates"] = {
                        "status": err.code,
                        "headers": dict(err.headers.items()) if err.headers else {},
                        "body": {"raw": body},
                    }
                    results["errors"].append(f"Total rates request failed with HTTP {err.code}")
                    exit_code = 1
                except urllib.error.URLError as err:
                    results["totalRates"] = {
                        "status": "connection_error",
                        "body": {"message": str(err.reason)},
                    }
                    results["errors"].append(f"Total rates connection error: {err.reason}")
                    exit_code = 1
                except Exception as exc:
                    results["totalRates"] = {
                        "status": "error",
                        "body": {"message": str(exc)},
                    }
                    results["errors"].append(f"Total rates request failed: {exc}")
                    exit_code = 1

                # Discover eligible extra services from total rates if available
                discovered_extras: list[int] = []
                total_body = results["totalRates"].get("body") if isinstance(results["totalRates"], dict) else None
                if isinstance(total_body, dict):
                    rate_options = total_body.get("rateOptions")
                    if isinstance(rate_options, list):
                        for option in rate_options:
                            extras = option.get("extraServices") if isinstance(option, dict) else None
                            if isinstance(extras, list):
                                for extra in extras:
                                    if isinstance(extra, dict):
                                        code = extra.get("extraService")
                                        try:
                                            if code is not None:
                                                discovered_extras.append(int(code))
                                        except (TypeError, ValueError):
                                            continue

                configured_extra_codes = parse_int_list(env.get("USPS_EXTRA_SERVICES")) or []
                candidate_codes = discovered_extras or configured_extra_codes or [930]

                # Determine sensible item value for insurance extras
                configured_item_value = parse_float(env.get("USPS_ITEM_VALUE"))
                item_value = configured_item_value if configured_item_value is not None else 300.0
                item_value_str = ("%.2f" % item_value).rstrip("0").rstrip(".") if item_value is not None else None

                # Prepare base payload used for each extra attempt
                base_extra_payload: Dict[str, Any] = {
                    "mailClass": env.get("USPS_MAIL_CLASS", "PRIORITY_MAIL_INTERNATIONAL"),
                    "priceType": env.get("USPS_PRICE_TYPE", "COMMERCIAL"),
                    "itemValue": item_value_str,
                    "weight": default_weight,
                    "mailingDate": env.get("USPS_MAILING_DATE", today),
                    "rateIndicator": env.get("USPS_RATE_INDICATOR", "SP"),
                    "destinationCountryCode": base_rates_payload.get("destinationCountryCode"),
                    "accountType": base_rates_payload.get("accountType"),
                    "accountNumber": base_rates_payload.get("accountNumber"),
                }
                base_extra_payload = prune_none(base_extra_payload)

                extra_errors: list[str] = []
                allowed_codes = {813, 820, 826, 857, 930, 931, 955}
                for code in candidate_codes:
                    if code not in allowed_codes:
                        extra_errors.append(f"Skipping extra service {code} not allowed by schema")
                        continue
                    extra_payload = dict(base_extra_payload)
                    extra_payload["extraService"] = code

                    try:
                        call_result = http_post_json(extra_service_url, extra_payload, headers=headers)
                        results["extraServiceRates"] = call_result
                        if call_result.get("status") == 200:
                            break
                        extra_errors.append(f"Extra service {code} failed with status {call_result.get('status')}")
                    except urllib.error.HTTPError as err:
                        body = err.read().decode("utf-8", errors="replace") if err.fp else ""
                        results["extraServiceRates"] = {
                            "status": err.code,
                            "headers": dict(err.headers.items()) if err.headers else {},
                            "body": {"raw": body},
                        }
                        extra_errors.append(f"Extra service {code} request failed with HTTP {err.code}")
                    except urllib.error.URLError as err:
                        results["extraServiceRates"] = {
                            "status": "connection_error",
                            "body": {"message": str(err.reason)},
                        }
                        extra_errors.append(f"Extra service {code} connection error: {err.reason}")
                    except Exception as exc:
                        results["extraServiceRates"] = {
                            "status": "error",
                            "body": {"message": str(exc)},
                        }
                        extra_errors.append(f"Extra service {code} request failed: {exc}")

                if results.get("extraServiceRates") and results["extraServiceRates"].get("status") != 200:
                    if extra_errors:
                        results["errors"].extend(extra_errors)
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
