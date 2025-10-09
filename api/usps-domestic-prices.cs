using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UspsProcessor
{
    public class UspsDomesticPricesProcessor
    {
        private static readonly Logger _logger;
        private static readonly object _configLock = new object();
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsDomesticPricesProcessor()
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

        #region GetDomesticPricesAsync
        public async Task<DomesticPriceLookupResponse> GetDomesticPricesAsync(DomesticPriceLookupRequest request)
        {
            DomesticPriceLookupResponse lookupResponse = new DomesticPriceLookupResponse
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            try
            {
                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    string payload = JsonConvert.SerializeObject(
                        request,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("domestic-prices", payload);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            try
                            {
                                JToken root = JToken.Parse(genericResponse.message);
                                JToken? rates = root["rates"];
                                if (rates != null && rates.Type == JTokenType.Array && rates.HasValues)
                                {
                                    foreach (JToken rate in rates)
                                    {
                                        decimal price = rate.Value<decimal?>("price")
                                            ?? rate.Value<decimal?>("baseRate")
                                            ?? rate.Value<decimal?>("amount")
                                            ?? 0m;

                                        lookupResponse.quotes.Add(new DomesticPriceQuote
                                        {
                                            service = rate.Value<string>("mailClass")
                                                ?? rate.Value<string>("description")
                                                ?? rate.Value<string>("service")
                                                ?? string.Empty,
                                            price = price,
                                            currency = rate.Value<string>("currencyCode")
                                                ?? rate.Value<string>("currency")
                                                ?? "USD",
                                            deliveryStandard = rate.Value<string>("deliveryStandard")
                                                ?? rate.Value<string>("serviceStandard"),
                                            rateIndicator = rate.Value<string>("rateIndicator"),
                                            priceType = rate.Value<string>("priceType"),
                                            destinationEntryFacilityType = rate.Value<string>("destinationEntryFacilityType"),
                                        });
                                    }

                                    if (lookupResponse.quotes.Count == 0)
                                    {
                                        lookupResponse.isSuccess = false;
                                        lookupResponse.errorDesc = "No domestic price quotes returned";
                                    }
                                }
                                else
                                {
                                    lookupResponse.isSuccess = false;
                                    lookupResponse.errorDesc = "No domestic price quotes returned";
                                }
                            }
                            catch (JsonException jx)
                            {
                                lookupResponse.isSuccess = false;
                                lookupResponse.errorDesc = "Unable to parse domestic prices response";
                                _logger.Error("USPS Domestic Prices Parse Error = {0}", jx.Message);
                            }
                        }
                        else
                        {
                            lookupResponse.isSuccess = false;
                            lookupResponse.errorDesc = "Blank response received from domestic prices call";
                        }
                    }
                    else
                    {
                        lookupResponse.isSuccess = false;
                        lookupResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error) ? genericResponse.error : "Error processing domestic prices request";
                        _logger.Error("USPS Domestic Prices Error = {0}", lookupResponse.errorDesc);
                    }
                }
                else
                {
                    lookupResponse.isSuccess = false;
                    lookupResponse.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (Exception ex)
            {
                lookupResponse.isSuccess = false;
                lookupResponse.errorDesc = "unavailable";
                _logger.Error("USPS Domestic Prices Exception = {0}", ex.Message);
            }

            return lookupResponse;
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

                HttpWebRequest webRequest;
                string url;

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
                else if (method == "domestic-prices")
                {
                    url = string.Format("{0}prices/v3/base-rates/search", _url);
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
    }

    public sealed class DomesticPriceLookupRequest
    {
        [JsonProperty("originZIPCode")]
        public string OriginZIPCode { get; set; } = string.Empty;

        [JsonProperty("destinationZIPCode")]
        public string DestinationZIPCode { get; set; } = string.Empty;

        [JsonProperty("weight")]
        public decimal Weight { get; set; }

        [JsonProperty("length")]
        public decimal Length { get; set; }

        [JsonProperty("width")]
        public decimal Width { get; set; }

        [JsonProperty("height")]
        public decimal Height { get; set; }

        [JsonProperty("mailClass")]
        public string MailClass { get; set; } = string.Empty;

        [JsonProperty("processingCategory")]
        public string ProcessingCategory { get; set; } = string.Empty;

        [JsonProperty("rateIndicator")]
        public string RateIndicator { get; set; } = string.Empty;

        [JsonProperty("destinationEntryFacilityType")]
        public string DestinationEntryFacilityType { get; set; } = string.Empty;

        [JsonProperty("priceType")]
        public string PriceType { get; set; } = string.Empty;

        [JsonProperty("mailingDate")]
        public string MailingDate { get; set; } = string.Empty;

        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonProperty("contractNumber")]
        public string? ContractNumber { get; set; }

        [JsonProperty("originEntryFacilityType")]
        public string? OriginEntryFacilityType { get; set; }

        [JsonProperty("zone")]
        public string? Zone { get; set; }

        [JsonIgnore]
        public decimal WeightOz
        {
            get => Weight;
            set => Weight = value;
        }

        [JsonIgnore]
        public PackageDimensions? Dimensions
        {
            get => new PackageDimensions
            {
                LengthIn = Length,
                WidthIn = Width,
                HeightIn = Height,
            };
            set
            {
                if (value != null)
                {
                    Length = value.LengthIn;
                    Width = value.WidthIn;
                    Height = value.HeightIn;
                }
            }
        }

        [JsonIgnore]
        public string OriginZip
        {
            get => OriginZIPCode;
            set => OriginZIPCode = value;
        }

        [JsonIgnore]
        public string DestinationZip
        {
            get => DestinationZIPCode;
            set => DestinationZIPCode = value;
        }
    }

    public sealed class DomesticPriceLookupResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public List<DomesticPriceQuote> quotes { get; set; } = new List<DomesticPriceQuote>();
    }

    public sealed class DomesticPriceQuote
    {
        public string service { get; set; } = string.Empty;
        public decimal price { get; set; }
        public string currency { get; set; } = "USD";
        public string? deliveryStandard { get; set; }
        public string? rateIndicator { get; set; }
        public string? priceType { get; set; }
        public string? destinationEntryFacilityType { get; set; }
    }
}
