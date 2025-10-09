using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UspsProcessor
{
    public class UspsScanFormsProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;
        private static readonly object _configLock = new object();

        static UspsScanFormsProcessor()
        {
            _logger = LogManager.GetCurrentClassLogger();

            if (!MemoryCache.Default.Contains("uspsToken"))
            {
                MemoryCache.Default["uspsToken"] = string.Empty;
            }

            if (!MemoryCache.Default.Contains("uspsTknExpiry"))
            {
                MemoryCache.Default["uspsTknExpiry"] = DateTime.UtcNow.AddHours(-1);
            }

            InitializeConfigurationFromEnvironment();
        }

        #region CreateAsync
        public async Task<ScanFormsCreateResponse> CreateAsync(ScanFormsCreateRequest request)
        {
            ScanFormsCreateResponse createResponse = new ScanFormsCreateResponse
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            if (request == null)
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = "Request payload is required";
                return createResponse;
            }

            // Validate that exactly one creation mode is provided
            int modes = 0;
            if (request.LabelShipment != null) modes++;
            if (request.MidShipment != null) modes++;
            if (request.ManifestMidShipment != null) modes++;
            if (modes != 1)
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = modes == 0
                    ? "Specify exactly one creation mode: labelShipment, midShipment, or manifestMidShipment"
                    : "Only one creation mode is allowed per request";
                return createResponse;
            }

            try
            {
                string token = await ValidateRestTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                {
                    createResponse.isSuccess = false;
                    createResponse.errorDesc = "Invalid token for USPS API";
                    return createResponse;
                }

                string payload = JsonConvert.SerializeObject(
                    request,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                UspsGenericResponse genericResponse = await MakeRestRequestAsync("scan-form", payload);
                if (!ProcessScanFormResponse(genericResponse, createResponse))
                {
                    return createResponse;
                }
            }
            catch (Exception ex)
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = "unavailable";
                _logger.Error("USPS Scan Forms Exception = {0}", ex.Message);
            }

            return createResponse;
        }
        #endregion

        #region Response Processing
        private bool ProcessScanFormResponse(UspsGenericResponse genericResponse, ScanFormsCreateResponse createResponse)
        {
            if (!genericResponse.isSuccess)
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error)
                    ? genericResponse.error!
                    : "Error processing scan form request";
                _logger.Error("USPS Scan Forms Error = {0}", createResponse.errorDesc);
                return false;
            }

            if (string.IsNullOrWhiteSpace(genericResponse.message))
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = "Blank response received from scan form call";
                return false;
            }

            try
            {
                JToken root = JToken.Parse(genericResponse.message);
                JToken? scanFormToken = root["scanForm"] ?? root["form"] ?? root;

                ScanFormSummary summary = MapScanFormSummary(scanFormToken);
                createResponse.scanForm = summary;

                if (summary.warnings.Count > 0)
                {
                    createResponse.warnings.AddRange(summary.warnings);
                }
            }
            catch (JsonException jx)
            {
                createResponse.isSuccess = false;
                createResponse.errorDesc = "Unable to parse scan form response";
                _logger.Error("USPS Scan Forms Parse Error = {0}", jx.Message);
                return false;
            }

            return true;
        }

        private static ScanFormSummary MapScanFormSummary(JToken? token)
        {
            ScanFormSummary summary = new ScanFormSummary();
            if (token == null)
            {
                return summary;
            }

            summary.efn = token.Value<string>("efn")
                ?? token.Value<string>("electronicFileNumber")
                ?? token.Value<string>("efi")
                ?? string.Empty;

            summary.formNumber = token.Value<string>("formNumber") ?? string.Empty;
            summary.formType = token.Value<string>("formType")
                ?? token.Value<string>("psFormType")
                ?? string.Empty;
            summary.createdTimestamp = token.Value<string>("createdDateTime")
                ?? token.Value<string>("createdOn");
            summary.expiresTimestamp = token.Value<string>("expiresDateTime")
                ?? token.Value<string>("expiresOn");
            summary.packageCount = token.Value<int?>("packageCount") ?? 0;

            JToken? artifact = token["artifact"] ?? token["form"] ?? token["scanFormArtifact"];
            if (artifact != null)
            {
                summary.formUrl = artifact.Value<string>("url") ?? artifact.Value<string>("href");
                summary.formContentType = artifact.Value<string>("contentType")
                    ?? artifact.Value<string>("mimeType");

                string? base64 = artifact.Value<string>("content")
                    ?? artifact.Value<string>("data");
                summary.formContent = base64;
            }

            // Fallback: some responses may provide links instead of an artifact object
            if (string.IsNullOrWhiteSpace(summary.formUrl))
            {
                JToken? links = token["links"];
                if (links != null && links.Type == JTokenType.Array)
                {
                    foreach (JToken link in links.Children<JToken>())
                    {
                        if (link == null) { continue; }
                        string? href = link.Value<string>("href") ?? link.Value<string>("url");
                        string? rel = link.Value<string>("rel");
                        string? type = link.Value<string>("type") ?? link.Value<string>("contentType") ?? link.Value<string>("mimeType");
                        if (!string.IsNullOrWhiteSpace(href) &&
                            (!string.IsNullOrWhiteSpace(type) && type.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                                || (!string.IsNullOrWhiteSpace(rel) && (rel.Contains("form", StringComparison.OrdinalIgnoreCase) || rel.Contains("scan", StringComparison.OrdinalIgnoreCase)))))
                        {
                            summary.formUrl = href;
                            summary.formContentType = string.IsNullOrWhiteSpace(summary.formContentType) ? type : summary.formContentType;
                            break;
                        }
                    }
                }
            }

            JToken? counts = token["counts"] ?? token["summary"];
            if (counts != null)
            {
                foreach (JToken? count in counts.Children<JToken>())
                {
                    if (count == null)
                    {
                        continue;
                    }

                    summary.counts.Add(new ScanFormCount
                    {
                        mailClass = count.Value<string>("mailClass")
                            ?? count.Value<string>("service")
                            ?? string.Empty,
                        packageCount = count.Value<int?>("packageCount")
                            ?? count.Value<int?>("pieces")
                            ?? 0,
                        totalPostage = count.Value<decimal?>("totalPostage")
                    });
                }
            }

            JToken? labelsToken = token["labels"] ?? token["items"];
            if (labelsToken != null && labelsToken.Type == JTokenType.Array)
            {
                foreach (JToken? label in labelsToken.Children<JToken>())
                {
                    if (label == null)
                    {
                        continue;
                    }

                    summary.labels.Add(new ScanFormLabelSummary
                    {
                        labelId = label.Value<string>("labelId")
                            ?? label.Value<string>("labelIdentifier"),
                        trackingNumber = label.Value<string>("trackingNumber")
                            ?? label.Value<string>("IMpb")
                            ?? label.Value<string>("impb")
                            ?? label.Value<string>("imbp")
                            ?? label.Value<string>("barcode"),
                        mailClass = label.Value<string>("mailClass")
                            ?? label.Value<string>("service"),
                        packageCount = label.Value<int?>("packageCount") ?? 1
                    });
                }
            }

            JToken? warningsToken = token["warnings"];
            if (warningsToken != null && warningsToken.Type == JTokenType.Array)
            {
                foreach (JToken? warning in warningsToken.Children<JToken>())
                {
                    if (warning == null)
                    {
                        continue;
                    }

                    summary.warnings.Add(new ScanFormWarning
                    {
                        code = warning.Value<string>("warningCode")
                            ?? warning.Value<string>("code"),
                        description = warning.Value<string>("warningDescription")
                            ?? warning.Value<string>("message")
                    });
                }
            }

            JToken? errorsToken = token["errors"];
            if (errorsToken != null && errorsToken.Type == JTokenType.Array)
            {
                foreach (JToken? error in errorsToken.Children<JToken>())
                {
                    if (error == null)
                    {
                        continue;
                    }

                    summary.errors.Add(new ScanFormError
                    {
                        code = error.Value<string>("errorCode")
                            ?? error.Value<string>("code"),
                        description = error.Value<string>("errorDescription")
                            ?? error.Value<string>("message")
                    });
                }
            }

            return summary;
        }
        #endregion

        #region ValidateRestTokenAsync
        private async Task<string> ValidateRestTokenAsync()
        {
            bool isSuccess = false;
            string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
            DateTime expiryDt = MemoryCache.Default["uspsTknExpiry"] is DateTime dt ? dt : DateTime.UtcNow.AddHours(-1);

            if (string.IsNullOrWhiteSpace(token) || DateTime.UtcNow >= expiryDt)
            {
                UspsGenericResponse genericResponse = await MakeRestRequestAsync("auth", string.Empty);
                if (genericResponse.isSuccess && !string.IsNullOrWhiteSpace(genericResponse.message))
                {
                    UspsAuthResponse authResponse = JsonConvert.DeserializeObject<UspsAuthResponse>(genericResponse.message);
                    if (authResponse != null && !string.IsNullOrWhiteSpace(authResponse.access_token))
                    {
                        MemoryCache.Default["uspsTknExpiry"] = DateTime.UtcNow.AddSeconds(authResponse.expires_in - 20);
                        MemoryCache.Default["uspsToken"] = authResponse.access_token;
                        isSuccess = true;
                    }
                }
                else
                {
                    UspsAuthResponse authResponse = JsonConvert.DeserializeObject<UspsAuthResponse>(genericResponse.message);
                    if (authResponse != null && !string.IsNullOrWhiteSpace(authResponse.error))
                    {
                        isSuccess = false;
                        _logger.Error("ValidateRestTokenAsync error = {0}", authResponse.error);
                    }
                }
            }
            else
            {
                isSuccess = true;
            }

            token = isSuccess ? MemoryCache.Default["uspsToken"] as string ?? string.Empty : string.Empty;
            return token;
        }
        #endregion

        #region MakeRestRequestAsync
        private async Task<UspsGenericResponse> MakeRestRequestAsync(string method, string payload)
        {
            UspsGenericResponse response = new UspsGenericResponse
            {
                isSuccess = true
            };

            try
            {
                HttpWebRequest webRequest;
                string url;

                if (string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                {
                    InitializeConfigurationFromEnvironment();
                }

                if (string.IsNullOrWhiteSpace(_url))
                {
                    throw new InvalidOperationException("USPS base URL is not configured. Set USPS_BASE_URL or call Configure.");
                }

                if (method == "auth")
                {
                    if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                    {
                        throw new InvalidOperationException("USPS credentials are not configured. Set USPS_CLIENT_ID and USPS_CLIENT_SECRET or call Configure.");
                    }

                    url = string.Format("{0}oauth2/v3/token", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.ContentType = "application/x-www-form-urlencoded";
                    webRequest.Method = "POST";
                    webRequest.Accept = "application/json";

                    payload = string.Format(
                        "grant_type=client_credentials&client_id={0}&client_secret={1}",
                        Uri.EscapeDataString(_clientId),
                        Uri.EscapeDataString(_clientSecret));
                }
                else if (method == "scan-form")
                {
                    url = string.Format("{0}scan-forms/v3/scan-form", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    webRequest.Accept = "application/json";
                }
                else
                {
                    throw new InvalidOperationException(string.Format("Unsupported USPS method '{0}'", method));
                }

                byte[] byteData = Encoding.UTF8.GetBytes(payload ?? string.Empty);
                webRequest.ContentLength = byteData.Length;
                using (Stream postStream = webRequest.GetRequestStream())
                {
                    postStream.Write(byteData, 0, byteData.Length);
                }

                using (HttpWebResponse webResponse = (HttpWebResponse)await webRequest.GetResponseAsync())
                {
                    using (Stream dataStream = webResponse.GetResponseStream())
                    {
                        StreamReader sr = new StreamReader(dataStream);
                        response.message = sr.ReadToEnd();
                        sr.Close();
                        dataStream.Close();
                    }
                }
            }
            catch (WebException wx)
            {
                response.isSuccess = false;
                _logger.Error("WebException - Message->{0}\nInnerException->{1}", wx.Message, wx.InnerException);
                HttpWebResponse webResp = wx.Response as HttpWebResponse;
                if (webResp != null)
                {
                    Stream stream = webResp.GetResponseStream();
                    if (stream != null)
                    {
                        StreamReader reader = new StreamReader(stream);
                        response.message = reader.ReadToEnd();
                        reader.Close();
                        stream.Close();
                        _logger.Error("WebException = {0}", response.message);
                    }
                }
            }
            catch (Exception ex)
            {
                response.isSuccess = false;
                response.error = ex.Message;
                _logger.Error("Exception = {0}", ex.Message);
            }

            return response;
        }
        #endregion

        #region Configuration
        public static void Configure(string? baseUrl, string? clientId, string? clientSecret)
        {
            lock (_configLock)
            {
                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    _url = NormalizeBaseUrl(baseUrl);
                }

                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    _clientId = clientId;
                }

                if (!string.IsNullOrWhiteSpace(clientSecret))
                {
                    _clientSecret = clientSecret;
                }
            }
        }

        private static void InitializeConfigurationFromEnvironment()
        {
            string? baseUrl = Environment.GetEnvironmentVariable("USPS_BASE_URL")
                ?? Environment.GetEnvironmentVariable("USPS_API_BASEURL");

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                string? envName = Environment.GetEnvironmentVariable("USPS_ENV");
                baseUrl = ResolveBaseUrlFromEnvironment(envName);
            }

            string? clientId = Environment.GetEnvironmentVariable("USPS_CLIENT_ID");
            string? clientSecret = Environment.GetEnvironmentVariable("USPS_CLIENT_SECRET");

            if (!string.IsNullOrWhiteSpace(baseUrl)
                || !string.IsNullOrWhiteSpace(clientId)
                || !string.IsNullOrWhiteSpace(clientSecret))
            {
                Configure(baseUrl, clientId, clientSecret);
            }
        }

        private static string? ResolveBaseUrlFromEnvironment(string? envName)
        {
            if (string.IsNullOrWhiteSpace(envName))
            {
                return null;
            }

            return string.Equals(envName, "tem", StringComparison.OrdinalIgnoreCase)
                ? "https://apis-tem.usps.com/"
                : string.Equals(envName, "prod", StringComparison.OrdinalIgnoreCase)
                    ? "https://apis.usps.com/"
                    : null;
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            string normalized = baseUrl.Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            return normalized;
        }
        #endregion

    }

    #region Request models
    public sealed class ScanFormsCreateRequest
    {
        [JsonProperty("labelShipment")]
        public ScanFormLabelShipment? LabelShipment { get; set; }

        [JsonProperty("midShipment")]
        public ScanFormMidShipment? MidShipment { get; set; }

        [JsonProperty("manifestMidShipment")]
        public ScanFormManifestMidShipment? ManifestMidShipment { get; set; }

        [JsonProperty("contact")]
        public ScanFormContact? Contact { get; set; }

        [JsonProperty("acceptanceLocation")]
        public ScanFormAcceptanceLocation? AcceptanceLocation { get; set; }

        [JsonProperty("outputOptions")]
        public ScanFormOutputOptions? OutputOptions { get; set; }
    }

    public sealed class ScanFormLabelShipment
    {
        [JsonProperty("labels")]
        public List<ScanFormLabelReference>? Labels { get; set; }

        [JsonProperty("mailDate")]
        public string? MailDate { get; set; }

        [JsonProperty("customerReference")]
        public string? CustomerReference { get; set; }

        [JsonProperty("shipmentId")]
        public string? ShipmentId { get; set; }
    }

    public sealed class ScanFormLabelReference
    {
        [JsonProperty("labelId")]
        public string? LabelId { get; set; }

        [JsonProperty("trackingNumber")]
        public string? TrackingNumber { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("packageCount")]
        public int? PackageCount { get; set; }
    }

    public sealed class ScanFormMidShipment
    {
        [JsonProperty("mid")]
        public string? Mid { get; set; }

        [JsonProperty("crid")]
        public string? Crid { get; set; }

        [JsonProperty("startDate")]
        public string? StartDate { get; set; }

        [JsonProperty("endDate")]
        public string? EndDate { get; set; }

        [JsonProperty("timeZone")]
        public string? Timezone { get; set; }

        [JsonProperty("includeLabelsWithoutScanForms")]
        public bool? IncludeLabelsWithoutScanForms { get; set; }
    }

    public sealed class ScanFormManifestMidShipment
    {
        [JsonProperty("manifestMid")]
        public string? ManifestMid { get; set; }

        [JsonProperty("mid")]
        public string? Mid { get; set; }

        [JsonProperty("crid")]
        public string? Crid { get; set; }

        [JsonProperty("startDate")]
        public string? StartDate { get; set; }

        [JsonProperty("endDate")]
        public string? EndDate { get; set; }

        [JsonProperty("timeZone")]
        public string? Timezone { get; set; }
    }

    public sealed class ScanFormContact
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("email")]
        public string? Email { get; set; }

        [JsonProperty("phone")]
        public string? Phone { get; set; }
    }

    public sealed class ScanFormAcceptanceLocation
    {
        [JsonProperty("facilityName")]
        public string? FacilityName { get; set; }

        [JsonProperty("address")]
        public ScanFormAddress? Address { get; set; }

        [JsonProperty("acceptanceType")]
        public string? AcceptanceType { get; set; }
    }

    public sealed class ScanFormAddress
    {
        [JsonProperty("address1")]
        public string? Address1 { get; set; }

        [JsonProperty("address2")]
        public string? Address2 { get; set; }

        [JsonProperty("city")]
        public string? City { get; set; }

        [JsonProperty("state")]
        public string? State { get; set; }

        [JsonProperty("postalCode")]
        public string? PostalCode { get; set; }

        [JsonProperty("countryCode")]
        public string? CountryCode { get; set; }
    }

    public sealed class ScanFormOutputOptions
    {
        [JsonProperty("format")]
        public string? Format { get; set; }

        [JsonProperty("includePdf")]
        public bool? IncludePdf { get; set; }

        [JsonProperty("includeLink")]
        public bool? IncludeLink { get; set; }

        [JsonProperty("copies")]
        public int? Copies { get; set; }
    }
    #endregion

    #region Response models
    public sealed class ScanFormsCreateResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public ScanFormSummary? scanForm { get; set; }
        public List<ScanFormWarning> warnings { get; set; } = new List<ScanFormWarning>();
    }

    public sealed class ScanFormSummary
    {
        public string efn { get; set; } = string.Empty;
        public string formNumber { get; set; } = string.Empty;
        public string formType { get; set; } = string.Empty;
        public string? createdTimestamp { get; set; }
        public string? expiresTimestamp { get; set; }
        public int packageCount { get; set; }
        public string? formUrl { get; set; }
        public string? formContent { get; set; }
        public string? formContentType { get; set; }
        public List<ScanFormCount> counts { get; set; } = new List<ScanFormCount>();
        public List<ScanFormLabelSummary> labels { get; set; } = new List<ScanFormLabelSummary>();
        public List<ScanFormWarning> warnings { get; set; } = new List<ScanFormWarning>();
        public List<ScanFormError> errors { get; set; } = new List<ScanFormError>();
    }

    public sealed class ScanFormCount
    {
        public string mailClass { get; set; } = string.Empty;
        public int packageCount { get; set; }
        public decimal? totalPostage { get; set; }
    }

    public sealed class ScanFormLabelSummary
    {
        public string? labelId { get; set; }
        public string? trackingNumber { get; set; }
        public string? mailClass { get; set; }
        public int packageCount { get; set; }
    }

    public sealed class ScanFormWarning
    {
        public string? code { get; set; }
        public string? description { get; set; }
    }

    public sealed class ScanFormError
    {
        public string? code { get; set; }
        public string? description { get; set; }
    }
    #endregion
}
