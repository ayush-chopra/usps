using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UspsProcessor
{
    public class UspsInternationalLabelsProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;
        private static readonly object _configLock = new object();

        static UspsInternationalLabelsProcessor()
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
        public async Task<InternationalLabelCreateResponse> CreateAsync(InternationalLabelCreateRequest request)
        {
            InternationalLabelCreateResponse response = new InternationalLabelCreateResponse
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            if (request == null)
            {
                response.isSuccess = false;
                response.errorDesc = "Request payload is required";
                return response;
            }

            if (string.IsNullOrWhiteSpace(request.PaymentAuthorizationToken))
            {
                response.isSuccess = false;
                response.errorDesc = "Payment authorization token is required";
                return response;
            }

            if (!ValidateCreateRequest(request, response))
            {
                return response;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_url))
                {
                    InitializeConfigurationFromEnvironment();
                }

                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    string acceptHeader = ResolveAcceptHeader(request.ImageInfo?.ImageType);
                    string payload = JsonConvert.SerializeObject(
                        request,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    string url = string.Format("{0}international-labels/v3/international-label", _url);

                    using HttpClientHandler handler = new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                    };

                    using HttpClient client = new HttpClient(handler);
                    using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                    httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    httpRequest.Headers.Add("X-Payment-Authorization-Token", request.PaymentAuthorizationToken);

                    httpRequest.Headers.Accept.Clear();
                    if (!string.IsNullOrWhiteSpace(acceptHeader))
                    {
                        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));
                    }
                    httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/mixed"));
                    httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage httpResponse = await client.SendAsync(httpRequest);
                    if (httpResponse.IsSuccessStatusCode)
                    {
                        await PopulateResponseAsync(httpResponse, response).ConfigureAwait(false);
                    }
                    else
                    {
                        response.isSuccess = false;
                        string rawError = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        response.errorDesc = ExtractErrorMessage(rawError, httpResponse.ReasonPhrase ?? "Error creating USPS international label");
                    }
                }
                else
                {
                    response.isSuccess = false;
                    response.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (HttpRequestException hx)
            {
                response.isSuccess = false;
                response.errorDesc = hx.Message;
                _logger.Error("USPS International Labels HTTP Exception = {0}", hx.Message);
            }
            catch (Exception ex)
            {
                response.isSuccess = false;
                response.errorDesc = "unavailable";
                _logger.Error("USPS International Labels Exception = {0}", ex.Message);
            }

            return response;
        }
        #endregion

        #region CancelAsync
        public async Task<InternationalLabelCancelResponse> CancelAsync(string trackingNumber, string paymentAuthorizationToken)
        {
            InternationalLabelCancelResponse response = new InternationalLabelCancelResponse
            {
                isSuccess = true,
                errorDesc = string.Empty,
                trackingNumber = trackingNumber ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                response.isSuccess = false;
                response.errorDesc = "trackingNumber is required";
                return response;
            }

            if (string.IsNullOrWhiteSpace(paymentAuthorizationToken))
            {
                response.isSuccess = false;
                response.errorDesc = "Payment authorization token is required";
                return response;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_url))
                {
                    InitializeConfigurationFromEnvironment();
                }

                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    string url = string.Format(
                        "{0}international-labels/v3/international-label/{1}",
                        _url,
                        Uri.EscapeDataString(trackingNumber));

                    using HttpClientHandler handler = new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                    };

                    using HttpClient client = new HttpClient(handler);
                    using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Delete, url);
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    httpRequest.Headers.Add("X-Payment-Authorization-Token", paymentAuthorizationToken);

                    HttpResponseMessage httpResponse = await client.SendAsync(httpRequest);
                    if (httpResponse.IsSuccessStatusCode)
                    {
                        response.statusDescription = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        TryPopulateCancelDetails(response);
                    }
                    else
                    {
                        response.isSuccess = false;
                        string rawError = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        response.errorDesc = ExtractErrorMessage(rawError, httpResponse.ReasonPhrase ?? "Error cancelling USPS international label");
                    }
                }
                else
                {
                    response.isSuccess = false;
                    response.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (HttpRequestException hx)
            {
                response.isSuccess = false;
                response.errorDesc = hx.Message;
                _logger.Error("USPS International Labels Cancel HTTP Exception = {0}", hx.Message);
            }
            catch (Exception ex)
            {
                response.isSuccess = false;
                response.errorDesc = "unavailable";
                _logger.Error("USPS International Labels Cancel Exception = {0}", ex.Message);
            }

            return response;
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
                if (string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                {
                    InitializeConfigurationFromEnvironment();
                }

                if (string.IsNullOrWhiteSpace(_url))
                {
                    throw new InvalidOperationException("USPS base URL is not configured. Set USPS_BASE_URL or call Configure.");
                }

                if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                {
                    throw new InvalidOperationException("USPS credentials are not configured. Set USPS_CLIENT_ID and USPS_CLIENT_SECRET or call Configure.");
                }

                if (method != "auth")
                {
                    throw new InvalidOperationException(string.Format("Unsupported USPS method '{0}'", method));
                }

                string url = string.Format("{0}oauth2/v3/token", _url);
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                webRequest.ContentType = "application/x-www-form-urlencoded";
                webRequest.Accept = "application/json";
                webRequest.Method = "POST";

                payload = string.Format(
                    "grant_type=client_credentials&client_id={0}&client_secret={1}",
                    Uri.EscapeDataString(_clientId),
                    Uri.EscapeDataString(_clientSecret));

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
                HttpWebResponse? webResp = wx.Response as HttpWebResponse;
                if (webResp != null)
                {
                    Stream? stream = webResp.GetResponseStream();
                    if (stream != null)
                    {
                        using StreamReader reader = new StreamReader(stream);
                        response.message = reader.ReadToEnd();
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

        #region Helpers
        private static string ResolveAcceptHeader(string? imageType)
        {
            if (string.IsNullOrWhiteSpace(imageType))
            {
                return "application/pdf";
            }

            return imageType.Trim().ToLowerInvariant() switch
            {
                "pdf" => "application/pdf",
                "tif" => "image/tiff",
                "tiff" => "image/tiff",
                _ => "application/pdf"
            };
        }

        private static async Task PopulateResponseAsync(HttpResponseMessage httpResponse, InternationalLabelCreateResponse response)
        {
            if (httpResponse.Headers.TryGetValues("X-Tracking-Number", out IEnumerable<string> trackingValues))
            {
                response.trackingNumber = trackingValues.FirstOrDefault() ?? response.trackingNumber;
            }

            byte[] data = await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false) ?? Array.Empty<byte>();
            string? mediaType = httpResponse.Content.Headers.ContentType?.MediaType;
            response.contentType = mediaType ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                string? boundary = GetBoundary(httpResponse.Content.Headers.ContentType);
                if (!string.IsNullOrWhiteSpace(boundary))
                {
                    ParseMultipart(data, boundary!, response);
                }
            }
            else if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string json = Encoding.UTF8.GetString(data);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    response.metadata = JObject.Parse(json);
                    response.trackingNumber = response.metadata?.Value<string>("trackingNumber") ?? response.trackingNumber;
                    TryLoadBase64LabelFromMetadata(response, json);
                }

                response.content = Array.Empty<byte>();
            }
            else
            {
                response.content = data;
            }

            response.content ??= Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(response.contentType))
            {
                response.contentType = mediaType ?? string.Empty;
            }

            PopulateMetadataProperties(response);
        }

        private static string? GetBoundary(MediaTypeHeaderValue? contentType)
        {
            if (contentType == null)
            {
                return null;
            }

            NameValueHeaderValue? parameter = contentType.Parameters?.FirstOrDefault(p => string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase));
            string? boundary = parameter?.Value;
            return string.IsNullOrWhiteSpace(boundary) ? null : boundary.Trim('"');
        }

        private static void ParseMultipart(byte[] data, string boundary, InternationalLabelCreateResponse response)
        {
            byte[] boundaryBytes = Encoding.ASCII.GetBytes("--" + boundary);
            byte[] headerDelimiter = Encoding.ASCII.GetBytes("\r\n\r\n");

            int position = 0;
            while (position < data.Length)
            {
                int boundaryIndex = IndexOf(data, boundaryBytes, position);
                if (boundaryIndex < 0)
                {
                    break;
                }

                int segmentStart = boundaryIndex + boundaryBytes.Length;
                if (segmentStart + 1 < data.Length && data[segmentStart] == '-' && data[segmentStart + 1] == '-')
                {
                    break; // closing
                }

                if (segmentStart + 1 < data.Length && data[segmentStart] == '\r' && data[segmentStart + 1] == '\n')
                {
                    segmentStart += 2;
                }

                int headerEnd = IndexOf(data, headerDelimiter, segmentStart);
                if (headerEnd < 0)
                {
                    break;
                }

                string headersText = Encoding.ASCII.GetString(data, segmentStart, headerEnd - segmentStart);
                int contentStart = headerEnd + headerDelimiter.Length;

                int nextBoundary = IndexOf(data, boundaryBytes, contentStart);
                if (nextBoundary < 0)
                {
                    nextBoundary = data.Length;
                }

                int contentEnd = nextBoundary;
                if (contentEnd > 0 && data[contentEnd - 1] == '\n')
                {
                    contentEnd--;
                    if (contentEnd > 0 && data[contentEnd - 1] == '\r')
                    {
                        contentEnd--;
                    }
                }

                int contentLength = Math.Max(0, contentEnd - contentStart);
                byte[] partContent = new byte[contentLength];
                Array.Copy(data, contentStart, partContent, 0, contentLength);

                string? partMediaType = null;
                foreach (string line in headersText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colonIndex = line.IndexOf(':');
                    if (colonIndex < 0)
                    {
                        continue;
                    }

                    string name = line[..colonIndex].Trim();
                    string value = line[(colonIndex + 1)..].Trim();

                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        partMediaType = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(partMediaType) && partMediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string json = Encoding.UTF8.GetString(partContent);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        response.metadata = JObject.Parse(json);
                        response.trackingNumber = response.metadata?.Value<string>("trackingNumber") ?? response.trackingNumber;
                    }
                }
                else if (partContent.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(partMediaType))
                    {
                        partMediaType = "application/pdf";
                    }

                    response.contentType = partMediaType ?? response.contentType;
                    response.content = partContent;
                }

                position = nextBoundary + boundaryBytes.Length;
            }

            response.content ??= Array.Empty<byte>();
        }

        private static int IndexOf(byte[] buffer, byte[] pattern, int start)
        {
            for (int i = start; i <= buffer.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void PopulateMetadataProperties(InternationalLabelCreateResponse response)
        {
            JObject? meta = response.metadata;
            if (meta == null)
            {
                return;
            }

            response.sku = meta.Value<string>("SKU") ?? response.sku;
            response.postage = meta.Value<decimal?>("postage")
                ?? meta.SelectToken("postage.amount")?.Value<decimal?>()
                ?? response.postage;
            response.zone = meta.Value<string>("zone") ?? response.zone;

            JToken? feesToken = meta["fees"];
            response.fees.Clear();
            if (feesToken != null && feesToken.Type == JTokenType.Array)
            {
                foreach (JToken feeToken in feesToken.Children<JToken>())
                {
                    if (feeToken != null && feeToken.Type == JTokenType.Object)
                    {
                        response.fees.Add(new InternationalLabelFee
                        {
                            Description = feeToken.Value<string>("description") ?? feeToken.Value<string>("name"),
                            Amount = feeToken.Value<decimal?>("amount") ?? feeToken.Value<decimal?>("price"),
                            Currency = feeToken.Value<string>("currency")
                        });
                    }
                }
            }

            JToken? customsToken = meta["customs"] ?? meta["customsSummary"];
            if (customsToken != null && customsToken.Type == JTokenType.Object)
            {
                response.customsInfo = new CustomsSummary
                {
                    TotalValue = customsToken.Value<decimal?>("totalValue"),
                    CurrencyCode = customsToken.Value<string>("currencyCode"),
                    ContentsType = customsToken.Value<string>("contentsType")
                };
            }

            JToken? commitmentToken = meta["commitment"] ?? meta["serviceCommitment"];
            if (commitmentToken != null && commitmentToken.Type == JTokenType.Object)
            {
                response.commitment = new InternationalCommitment
                {
                    Name = commitmentToken.Value<string>("name"),
                    MinDays = commitmentToken.Value<int?>("minDays"),
                    MaxDays = commitmentToken.Value<int?>("maxDays"),
                    EstimatedDeliveryDate = commitmentToken.Value<string>("estimatedDeliveryDate")
                };
            }
        }

        private static string ExtractErrorMessage(string? rawBody, string? fallback)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return fallback ?? string.Empty;
            }

            try
            {
                JObject obj = JObject.Parse(rawBody);
                string? error = obj.Value<string>("error")
                    ?? obj.Value<string>("message")
                    ?? obj.SelectToken("$.error.message")?.Value<string>();

                if (string.IsNullOrWhiteSpace(error) && obj.TryGetValue("errors", out JToken? errorsToken)
                    && errorsToken is JArray arr && arr.Count > 0)
                {
                    IEnumerable<string> messages = arr
                        .Select(t => t.Value<string>("message") ?? t.Value<string>("detail") ?? t.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    error = string.Join("; ", messages);
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    string? code = obj.Value<string>("code")
                        ?? obj.Value<string>("errorCode")
                        ?? obj.SelectToken("$.error.code")?.Value<string>();

                    return string.IsNullOrWhiteSpace(code)
                        ? error!
                        : string.Format("{0}: {1}", code, error);
                }
            }
            catch (JsonException)
            {
                // fall back to raw body
            }

            return string.IsNullOrWhiteSpace(rawBody) ? (fallback ?? string.Empty) : rawBody;
        }

        private static void TryLoadBase64LabelFromMetadata(InternationalLabelCreateResponse response, string json)
        {
            if (response.metadata == null)
            {
                return;
            }

            string[] labelFields =
            {
                "labelImage",
                "label",
                "image",
                "labelImageBase64",
                "labelImage.imageBase64",
                "label.imageBase64",
                "image.base64",
                "labelImage.data",
                "image.data"
            };

            foreach (string path in labelFields)
            {
                IEnumerable<JToken> tokens;
                if (path.Contains('.'))
                {
                    tokens = response.metadata.SelectTokens(path, errorWhenNoMatch: false);
                }
                else
                {
                    tokens = response.metadata.TryGetValue(path, out JToken? token) && token != null
                        ? new[] { token }
                        : Enumerable.Empty<JToken>();
                }

                foreach (JToken token in tokens)
                {
                    string? value = token?.Value<string>();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    try
                    {
                        byte[] decoded = Convert.FromBase64String(value);
                        if (decoded.Length > 0)
                        {
                            response.content = decoded;
                            if (string.IsNullOrWhiteSpace(response.contentType))
                            {
                                response.contentType = response.metadata.Value<string>("contentType")
                                    ?? response.metadata.SelectToken("labelImageContentType")?.Value<string>()
                                    ?? InferContentTypeFromImageType(response.metadata.SelectToken("imageInfo.imageType")?.Value<string>());
                            }
                            return;
                        }
                    }
                    catch (FormatException)
                    {
                        // not valid base64; try next
                    }
                }
            }
        }

        private static string InferContentTypeFromImageType(string? imageType)
        {
            if (string.IsNullOrWhiteSpace(imageType))
            {
                return "application/pdf";
            }

            return imageType.Trim().ToUpperInvariant() switch
            {
                "TIF" => "image/tiff",
                "TIFF" => "image/tiff",
                _ => "application/pdf"
            };
        }

        private static void TryPopulateCancelDetails(InternationalLabelCancelResponse response)
        {
            if (string.IsNullOrWhiteSpace(response.statusDescription))
            {
                return;
            }

            try
            {
                JObject obj = JObject.Parse(response.statusDescription);
                string? status = obj.Value<string>("status") ?? obj.Value<string>("message");
                if (!string.IsNullOrWhiteSpace(status))
                {
                    response.statusDescription = status;
                }
            }
            catch (JsonException)
            {
                // leave as raw text
            }
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

            if (!string.IsNullOrWhiteSpace(baseUrl) || !string.IsNullOrWhiteSpace(clientId) || !string.IsNullOrWhiteSpace(clientSecret))
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

        #region Validation
        private static bool ValidateCreateRequest(InternationalLabelCreateRequest request, InternationalLabelCreateResponse response)
        {
            List<string> errors = new List<string>();

            string imageType = request.ImageInfo?.ImageType ?? string.Empty;
            if (string.IsNullOrWhiteSpace(imageType))
            {
                errors.Add("imageInfo.imageType is required (PDF or TIF)");
            }
            else
            {
                string normalized = imageType.Trim().ToUpperInvariant();
                if (normalized != "PDF" && normalized != "TIF" && normalized != "TIFF")
                {
                    errors.Add("imageInfo.imageType must be PDF or TIF(F)");
                }
            }

            if (!ValidateAddress(request.FromAddress, true))
            {
                errors.Add("fromAddress is missing required fields (streetAddress, city, state, ZIPCode, countryCode)");
            }

            if (!ValidateAddress(request.ToAddress, false))
            {
                errors.Add("toAddress is missing required fields (streetAddress, city, postalCode, countryCode)");
            }

            if (request.PackageDescription == null || string.IsNullOrWhiteSpace(request.PackageDescription.MailClass))
            {
                errors.Add("packageDescription.mailClass is required");
            }

            if (request.PackageDescription == null || string.IsNullOrWhiteSpace(request.PackageDescription.PriceType))
            {
                errors.Add("packageDescription.priceType is required (COMMERCIAL or RETAIL)");
            }

            if (request.PackageDescription == null || request.PackageDescription.Weight <= 0m)
            {
                errors.Add("packageDescription.weight must be greater than zero");
            }

            if (request.Customs == null)
            {
                errors.Add("customs section is required for international labels");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Customs.ContentsType))
                {
                    errors.Add("customs.contentsType is required");
                }

                if (request.Customs.Items == null || request.Customs.Items.Count == 0)
                {
                    errors.Add("customs.items must include at least one item");
                }
                else
                {
                    foreach (InternationalLabelCustomsItem item in request.Customs.Items)
                    {
                        if (item == null)
                        {
                            errors.Add("customs.items contains a null entry");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(item.Description))
                        {
                            errors.Add("customs.items.description is required for each item");
                        }

                        if (item.Quantity <= 0)
                        {
                            errors.Add("customs.items.quantity must be greater than zero");
                        }

                        if (item.UnitValue <= 0m)
                        {
                            errors.Add("customs.items.unitValue must be greater than zero");
                        }

                        if (item.UnitWeight <= 0m)
                        {
                            errors.Add("customs.items.unitWeight must be greater than zero");
                        }
                    }
                }
            }

                if (!string.IsNullOrWhiteSpace(request.Customs.CurrencyCode))
                {
                    if (!Regex.IsMatch(request.Customs.CurrencyCode.Trim(), "^[A-Z]{3}$"))
                    {
                        errors.Add("customs.currencyCode must be a 3-letter ISO 4217 code");
                    }
                }

                if (request.Customs.Items != null && request.Customs.Items.Count > 0)
                {
                    decimal totalLines = 0m;
                    foreach (InternationalLabelCustomsItem item in request.Customs.Items)
                    {
                        if (item != null && item.Quantity > 0 && item.UnitValue > 0m)
                        {
                            totalLines += item.Quantity * item.UnitValue;
                        }
                    }

                    if (request.Customs.TotalValue > 0m)
                    {
                        decimal difference = Math.Abs(request.Customs.TotalValue - totalLines);
                        if (difference > 0.01m)
                        {
                            errors.Add("customs.totalValue does not match sum of item values");
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                response.isSuccess = false;
                response.errorDesc = string.Join("; ", errors);
                return false;
            }

            return true;
        }

        private static bool ValidateAddress(InternationalLabelAddress? address, bool isDomesticOrigin)
        {
            if (address == null)
            {
                return false;
            }

            bool hasCore = !string.IsNullOrWhiteSpace(address.StreetAddress)
                && !string.IsNullOrWhiteSpace(address.City)
                && !string.IsNullOrWhiteSpace(address.CountryCode);

            if (!hasCore)
            {
                return false;
            }

            if (!Regex.IsMatch(address.CountryCode.Trim(), "^[A-Za-z]{2}$"))
            {
                return false;
            }

            if (isDomesticOrigin)
            {
                return !string.IsNullOrWhiteSpace(address.State)
                    && !string.IsNullOrWhiteSpace(address.ZIPCode)
                    && string.Equals(address.CountryCode, "US", StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(address.PostalCode);
        }
        #endregion

    }

    #region Request Models
    public sealed class InternationalLabelCreateRequest
    {
        [JsonProperty("imageInfo")]
        public InternationalLabelImageInfo ImageInfo { get; set; } = new InternationalLabelImageInfo();

        [JsonProperty("fromAddress")]
        public InternationalLabelAddress FromAddress { get; set; } = new InternationalLabelAddress();

        [JsonProperty("toAddress")]
        public InternationalLabelAddress ToAddress { get; set; } = new InternationalLabelAddress();

        [JsonProperty("packageDescription")]
        public InternationalLabelPackageDescription PackageDescription { get; set; } = new InternationalLabelPackageDescription();

        [JsonProperty("customs")]
        public InternationalLabelCustoms Customs { get; set; } = new InternationalLabelCustoms();

        [JsonProperty("extraServices")]
        public List<int>? ExtraServices { get; set; }

        [JsonProperty("reference")]
        public string? Reference { get; set; }

        [JsonIgnore]
        public string PaymentAuthorizationToken { get; set; } = string.Empty;
    }

    public sealed class InternationalLabelImageInfo
    {
        [JsonProperty("imageType")]
        public string ImageType { get; set; } = "PDF";

        [JsonProperty("labelType")]
        public string LabelType { get; set; } = "4X6LABEL";

        [JsonProperty("receiptOption")]
        public string? ReceiptOption { get; set; } = "NONE";
    }

    public sealed class InternationalLabelAddress
    {
        [JsonProperty("firstName")]
        public string? FirstName { get; set; }

        [JsonProperty("lastName")]
        public string? LastName { get; set; }

        [JsonProperty("companyName")]
        public string? CompanyName { get; set; }

        [JsonProperty("streetAddress")]
        public string StreetAddress { get; set; } = string.Empty;

        [JsonProperty("secondaryAddress")]
        public string? SecondaryAddress { get; set; }

        [JsonProperty("city")]
        public string City { get; set; } = string.Empty;

        [JsonProperty("state")]
        public string? State { get; set; }

        [JsonProperty("ZIPCode")]
        public string? ZIPCode { get; set; }

        [JsonProperty("postalCode")]
        public string? PostalCode { get; set; }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; } = string.Empty;

        [JsonProperty("phone")]
        public string? Phone { get; set; }

        [JsonProperty("email")]
        public string? Email { get; set; }
    }

    public sealed class InternationalLabelPackageDescription
    {
        [JsonProperty("mailClass")]
        public string MailClass { get; set; } = string.Empty;

        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("rateIndicator")]
        public string? RateIndicator { get; set; }

        [JsonProperty("processingCategory")]
        public string? ProcessingCategory { get; set; }

        [JsonProperty("weight")]
        public decimal Weight { get; set; }

        [JsonProperty("length")]
        public decimal? Length { get; set; }

        [JsonProperty("width")]
        public decimal? Width { get; set; }

        [JsonProperty("height")]
        public decimal? Height { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }
    }

    public sealed class InternationalLabelCustoms
    {
        [JsonProperty("contentsType")]
        public string ContentsType { get; set; } = "MERCHANDISE";

        [JsonProperty("invoiceNumber")]
        public string? InvoiceNumber { get; set; }

        [JsonProperty("totalValue")]
        public decimal TotalValue { get; set; }

        [JsonProperty("currencyCode")]
        public string CurrencyCode { get; set; } = "USD";

        [JsonProperty("nonDeliveryOption")]
        public string? NonDeliveryOption { get; set; }

        [JsonProperty("senderSignatureName")]
        public string? SenderSignatureName { get; set; }

        [JsonProperty("senderSignatureDate")]
        public string? SenderSignatureDate { get; set; }

        [JsonProperty("items")]
        public List<InternationalLabelCustomsItem> Items { get; set; } = new List<InternationalLabelCustomsItem>();
    }

    public sealed class InternationalLabelCustomsItem
    {
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("unitValue")]
        public decimal UnitValue { get; set; }

        [JsonProperty("unitWeight")]
        public decimal UnitWeight { get; set; }

        [JsonProperty("hsTariffNumber")]
        public string? HsTariffNumber { get; set; }

        [JsonProperty("countryOfOrigin")]
        public string? CountryOfOrigin { get; set; }
    }
    #endregion

    #region Response Models
    public sealed class InternationalLabelCreateResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public string trackingNumber { get; set; } = string.Empty;
        public string contentType { get; set; } = string.Empty;
        public byte[] content { get; set; } = Array.Empty<byte>();
        public JObject? metadata { get; set; }
        public string sku { get; set; } = string.Empty;
        public decimal? postage { get; set; }
        public string? zone { get; set; }
        public CustomsSummary? customsInfo { get; set; }
        public InternationalCommitment? commitment { get; set; }
        public List<InternationalLabelFee> fees { get; set; } = new List<InternationalLabelFee>();
    }

    public sealed class InternationalLabelCancelResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public string trackingNumber { get; set; } = string.Empty;
        public string? statusDescription { get; set; }
    }

    public sealed class InternationalLabelFee
    {
        public string? Description { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
    }

    public sealed class CustomsSummary
    {
        public decimal? TotalValue { get; set; }
        public string? CurrencyCode { get; set; }
        public string? ContentsType { get; set; }
    }

    public sealed class InternationalCommitment
    {
        public string? Name { get; set; }
        public int? MinDays { get; set; }
        public int? MaxDays { get; set; }
        public string? EstimatedDeliveryDate { get; set; }
    }
    #endregion
}
