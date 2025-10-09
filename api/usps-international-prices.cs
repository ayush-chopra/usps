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

namespace UspsProcessor
{
    public class UspsInternationalPricesProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsInternationalPricesProcessor()
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
        }

        #region QuoteAsync
        public async Task<InternationalPricesQuoteResponse> QuoteAsync(InternationalPricesQuoteRequest request)
        {
            InternationalPricesQuoteResponse quoteResponse = new InternationalPricesQuoteResponse
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            if (request == null)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Request payload is required";
                return quoteResponse;
            }

            try
            {
                string token = await ValidateRestTokenAsync();
                if (string.IsNullOrWhiteSpace(token))
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "Invalid token for USPS API";
                    return quoteResponse;
                }

                JsonSerializerSettings serializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };

                bool anyEndpointRequested = false;

                if (request.BaseRates != null)
                {
                    anyEndpointRequested = true;
                    string payload = JsonConvert.SerializeObject(request.BaseRates, serializerSettings);
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("international-base-rates", payload);
                    if (!ProcessBaseRatesResponse(genericResponse, quoteResponse, false))
                    {
                        return quoteResponse;
                    }
                }

                if (request.BaseRatesList != null)
                {
                    anyEndpointRequested = true;
                    string payload = JsonConvert.SerializeObject(request.BaseRatesList, serializerSettings);
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("international-base-rates-list", payload);
                    if (!ProcessBaseRatesResponse(genericResponse, quoteResponse, true))
                    {
                        return quoteResponse;
                    }
                }

                if (request.ExtraServiceRates != null)
                {
                    anyEndpointRequested = true;
                    string payload = JsonConvert.SerializeObject(request.ExtraServiceRates, serializerSettings);
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("international-extra-service-rates", payload);
                    if (!ProcessExtraServiceRatesResponse(genericResponse, quoteResponse))
                    {
                        return quoteResponse;
                    }
                }

                if (request.TotalRates != null)
                {
                    anyEndpointRequested = true;
                    string payload = JsonConvert.SerializeObject(request.TotalRates, serializerSettings);
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("international-total-rates", payload);
                    if (!ProcessTotalRatesResponse(genericResponse, quoteResponse))
                    {
                        return quoteResponse;
                    }
                }

                if (!anyEndpointRequested)
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "Specify at least one international prices request payload.";
                }
            }
            catch (Exception ex)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "unavailable";
                _logger.Error("USPS International Prices Exception = {0}", ex.Message);
            }

            return quoteResponse;
        }
        #endregion

        #region Response Processing
        private bool ProcessBaseRatesResponse(UspsGenericResponse genericResponse, InternationalPricesQuoteResponse quoteResponse, bool isList)
        {
            if (!genericResponse.isSuccess)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error)
                    ? genericResponse.error!
                    : "Error processing international prices request";
                _logger.Error("USPS International Prices Error = {0}", quoteResponse.errorDesc);
                return false;
            }

            if (string.IsNullOrWhiteSpace(genericResponse.message))
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Blank response received from international prices call";
                return false;
            }

            try
            {
                if (isList)
                {
                    UspsInternationalRateOptionsResponse? rateOptionsResponse = JsonConvert.DeserializeObject<UspsInternationalRateOptionsResponse>(genericResponse.message);
                    if (rateOptionsResponse?.rateOptions == null || rateOptionsResponse.rateOptions.Count == 0)
                    {
                        quoteResponse.isSuccess = false;
                        quoteResponse.errorDesc = "No international base rate options returned";
                        return false;
                    }

                    foreach (UspsInternationalRateOption option in rateOptionsResponse.rateOptions)
                    {
                        InternationalBaseRateOption mapped = MapRateOption(option);
                        if (mapped.rates.Count > 0)
                        {
                            quoteResponse.baseRateOptions.Add(mapped);
                        }
                    }

                    if (quoteResponse.baseRateOptions.Count == 0)
                    {
                        quoteResponse.isSuccess = false;
                        quoteResponse.errorDesc = "No international base rate options returned";
                        return false;
                    }
                }
                else
                {
                    UspsInternationalBaseRatesResponse? baseRatesResponse = JsonConvert.DeserializeObject<UspsInternationalBaseRatesResponse>(genericResponse.message);
                    if (baseRatesResponse?.rates == null || baseRatesResponse.rates.Count == 0)
                    {
                        quoteResponse.isSuccess = false;
                        quoteResponse.errorDesc = "No international base rates returned";
                        return false;
                    }

                    InternationalBaseRateOption mapped = MapBaseRates(baseRatesResponse);
                    if (mapped.rates.Count == 0)
                    {
                        quoteResponse.isSuccess = false;
                        quoteResponse.errorDesc = "No international base rates returned";
                        return false;
                    }

                    quoteResponse.baseRates.Add(mapped);
                }
            }
            catch (JsonException jx)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Unable to parse international prices response";
                _logger.Error("USPS International Prices Parse Error = {0}", jx.Message);
                return false;
            }

            return true;
        }

        private bool ProcessExtraServiceRatesResponse(UspsGenericResponse genericResponse, InternationalPricesQuoteResponse quoteResponse)
        {
            if (!genericResponse.isSuccess)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error)
                    ? genericResponse.error!
                    : "Error processing international extra service rates request";
                _logger.Error("USPS International Prices Error = {0}", quoteResponse.errorDesc);
                return false;
            }

            if (string.IsNullOrWhiteSpace(genericResponse.message))
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Blank response received from international extra service rates call";
                return false;
            }

            try
            {
                UspsInternationalExtraService? extraService = JsonConvert.DeserializeObject<UspsInternationalExtraService>(genericResponse.message);
                if (extraService == null)
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "No international extra service rates returned";
                    return false;
                }

                InternationalExtraServiceQuote mapped = MapExtraService(extraService);
                quoteResponse.extraServiceRates.Add(mapped);
            }
            catch (JsonException jx)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Unable to parse international extra service rates response";
                _logger.Error("USPS International Prices Parse Error = {0}", jx.Message);
                return false;
            }

            return true;
        }

        private bool ProcessTotalRatesResponse(UspsGenericResponse genericResponse, InternationalPricesQuoteResponse quoteResponse)
        {
            if (!genericResponse.isSuccess)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error)
                    ? genericResponse.error!
                    : "Error processing international total rates request";
                _logger.Error("USPS International Prices Error = {0}", quoteResponse.errorDesc);
                return false;
            }

            if (string.IsNullOrWhiteSpace(genericResponse.message))
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Blank response received from international total rates call";
                return false;
            }

            try
            {
                UspsInternationalTotalRatesResponse? totalRatesResponse = JsonConvert.DeserializeObject<UspsInternationalTotalRatesResponse>(genericResponse.message);
                if (totalRatesResponse?.rateOptions == null || totalRatesResponse.rateOptions.Count == 0)
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "No international total rates returned";
                    return false;
                }

                foreach (UspsInternationalTotalRateOption option in totalRatesResponse.rateOptions)
                {
                    InternationalTotalRateOption mapped = MapTotalRateOption(option);
                    if (mapped.rates.Count > 0 || mapped.extraServices.Count > 0)
                    {
                        quoteResponse.totalRates.Add(mapped);
                    }
                }

                if (quoteResponse.totalRates.Count == 0)
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "No international total rates returned";
                    return false;
                }
            }
            catch (JsonException jx)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "Unable to parse international total rates response";
                _logger.Error("USPS International Prices Parse Error = {0}", jx.Message);
                return false;
            }

            return true;
        }
        #endregion

        #region Mapping helpers
        private static InternationalBaseRateOption MapBaseRates(UspsInternationalBaseRatesResponse response)
        {
            InternationalBaseRateOption option = new InternationalBaseRateOption
            {
                totalBasePrice = response.totalBasePrice ?? 0m
            };

            if (response.warnings != null)
            {
                foreach (string warning in response.warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                {
                    option.warnings.Add(warning);
                }
            }

            option.rates.AddRange(MapRates(response.rates));
            return option;
        }

        private static InternationalBaseRateOption MapRateOption(UspsInternationalRateOption option)
        {
            InternationalBaseRateOption mapped = new InternationalBaseRateOption
            {
                totalBasePrice = option.totalBasePrice ?? 0m
            };

            if (option.warnings != null)
            {
                foreach (string warning in option.warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                {
                    mapped.warnings.Add(warning);
                }
            }

            mapped.rates.AddRange(MapRates(option.rates));
            return mapped;
        }

        private static InternationalTotalRateOption MapTotalRateOption(UspsInternationalTotalRateOption option)
        {
            InternationalTotalRateOption mapped = new InternationalTotalRateOption
            {
                totalBasePrice = option.totalBasePrice ?? 0m,
                totalPrice = option.totalPrice ?? option.totalBasePrice ?? 0m
            };

            if (option.warnings != null)
            {
                foreach (string warning in option.warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                {
                    mapped.warnings.Add(warning);
                }
            }

            mapped.rates.AddRange(MapRates(option.rates));

            if (option.extraServices != null)
            {
                foreach (UspsInternationalExtraService extra in option.extraServices)
                {
                    InternationalExtraServiceQuote mappedExtra = MapExtraService(extra);
                    mapped.extraServices.Add(mappedExtra);
                }
            }

            return mapped;
        }

        private static List<InternationalBaseRateQuote> MapRates(IEnumerable<UspsInternationalRate>? rates)
        {
            List<InternationalBaseRateQuote> mapped = new List<InternationalBaseRateQuote>();
            if (rates == null)
            {
                return mapped;
            }

            foreach (UspsInternationalRate rate in rates)
            {
                if (rate == null)
                {
                    continue;
                }

                InternationalBaseRateQuote quote = new InternationalBaseRateQuote
                {
                    sku = rate.SKU ?? string.Empty,
                    description = rate.description ?? string.Empty,
                    mailClass = rate.mailClass ?? string.Empty,
                    priceType = rate.priceType ?? string.Empty,
                    zone = rate.zone ?? string.Empty,
                    price = rate.price ?? 0m,
                    weight = rate.weight,
                    dimensionalWeight = rate.dimWeight ?? rate.dimensionalWeight,
                    startDate = rate.startDate ?? string.Empty,
                    endDate = rate.endDate ?? string.Empty
                };

                if (rate.fees != null)
                {
                    foreach (UspsInternationalFee fee in rate.fees)
                    {
                        if (fee == null)
                        {
                            continue;
                        }

                        quote.fees.Add(new InternationalRateFee
                        {
                            description = fee.description ?? string.Empty,
                            price = fee.price ?? 0m
                        });
                    }
                }

                if (rate.warnings != null)
                {
                    foreach (string warning in rate.warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                    {
                        quote.warnings.Add(warning);
                    }
                }

                mapped.Add(quote);
            }

            return mapped;
        }

        private static InternationalExtraServiceQuote MapExtraService(UspsInternationalExtraService extra)
        {
            InternationalExtraServiceQuote mapped = new InternationalExtraServiceQuote
            {
                sku = extra.SKU ?? string.Empty,
                priceType = extra.priceType ?? string.Empty,
                price = extra.price ?? 0m,
                extraService = extra.extraService ?? string.Empty,
                name = extra.name ?? string.Empty
            };

            if (extra.warnings != null)
            {
                foreach (string warning in extra.warnings.Where(w => !string.IsNullOrWhiteSpace(w)))
                {
                    mapped.warnings.Add(warning);
                }
            }

            return mapped;
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

                if (method == "auth")
                {
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
                else if (method == "international-base-rates")
                {
                    url = string.Format("{0}international-prices/v3/base-rates/search", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    webRequest.Accept = "application/json";
                }
                else if (method == "international-base-rates-list")
                {
                    url = string.Format("{0}international-prices/v3/base-rates-list/search", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    webRequest.Accept = "application/json";
                }
                else if (method == "international-extra-service-rates")
                {
                    url = string.Format("{0}international-prices/v3/extra-service-rates/search", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    webRequest.Accept = "application/json";
                }
                else if (method == "international-total-rates")
                {
                    url = string.Format("{0}international-prices/v3/total-rates/search", _url);
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
    }

    #region Request models
    public sealed class InternationalPricesQuoteRequest
    {
        [JsonProperty("baseRates")]
        public InternationalBaseRatesRequest? BaseRates { get; set; }

        [JsonProperty("baseRatesList")]
        public InternationalBaseRatesListRequest? BaseRatesList { get; set; }

        [JsonProperty("extraServiceRates")]
        public InternationalExtraServiceRatesRequest? ExtraServiceRates { get; set; }

        [JsonProperty("totalRates")]
        public InternationalTotalRatesRequest? TotalRates { get; set; }
    }

    public sealed class InternationalBaseRatesRequest
    {
        [JsonProperty("originZIPCode")]
        public string OriginZIPCode { get; set; } = string.Empty;

        [JsonProperty("foreignPostalCode")]
        public string? ForeignPostalCode { get; set; }

        [JsonProperty("destinationCountryCode")]
        public string DestinationCountryCode { get; set; } = string.Empty;

        [JsonProperty("destinationEntryFacilityType")]
        public string? DestinationEntryFacilityType { get; set; }

        [JsonProperty("weight")]
        public decimal? Weight { get; set; }

        [JsonProperty("length")]
        public decimal? Length { get; set; }

        [JsonProperty("width")]
        public decimal? Width { get; set; }

        [JsonProperty("height")]
        public decimal? Height { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("processingCategory")]
        public string? ProcessingCategory { get; set; }

        [JsonProperty("rateIndicator")]
        public string? RateIndicator { get; set; }

        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }
    }

    public sealed class InternationalBaseRatesListRequest
    {
        [JsonProperty("originZIPCode")]
        public string OriginZIPCode { get; set; } = string.Empty;

        [JsonProperty("foreignPostalCode")]
        public string? ForeignPostalCode { get; set; }

        [JsonProperty("destinationCountryCode")]
        public string DestinationCountryCode { get; set; } = string.Empty;

        [JsonProperty("weight")]
        public decimal? Weight { get; set; }

        [JsonProperty("length")]
        public decimal? Length { get; set; }

        [JsonProperty("width")]
        public decimal? Width { get; set; }

        [JsonProperty("height")]
        public decimal? Height { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }
    }

    public sealed class InternationalExtraServiceRatesRequest
    {
        [JsonProperty("extraService")]
        public int? ExtraService { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("itemValue")]
        public decimal? ItemValue { get; set; }

        [JsonProperty("weight")]
        public decimal? Weight { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }

        [JsonProperty("rateIndicator")]
        public string? RateIndicator { get; set; }

        [JsonProperty("destinationCountryCode")]
        public string? DestinationCountryCode { get; set; }

        [JsonProperty("foreignPostalCode")]
        public string? ForeignPostalCode { get; set; }

        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }
    }

    public sealed class InternationalTotalRatesRequest
    {
        [JsonProperty("originZIPCode")]
        public string OriginZIPCode { get; set; } = string.Empty;

        [JsonProperty("foreignPostalCode")]
        public string? ForeignPostalCode { get; set; }

        [JsonProperty("destinationCountryCode")]
        public string DestinationCountryCode { get; set; } = string.Empty;

        [JsonProperty("weight")]
        public decimal? Weight { get; set; }

        [JsonProperty("length")]
        public decimal? Length { get; set; }

        [JsonProperty("width")]
        public decimal? Width { get; set; }

        [JsonProperty("height")]
        public decimal? Height { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }

        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonProperty("itemValue")]
        public decimal? ItemValue { get; set; }

        [JsonProperty("extraServices")]
        public List<int>? ExtraServices { get; set; }

        [JsonProperty("processingCategory")]
        public string? ProcessingCategory { get; set; }

        [JsonProperty("rateIndicator")]
        public string? RateIndicator { get; set; }
    }
    #endregion

    #region Response models
    public sealed class InternationalPricesQuoteResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public List<InternationalBaseRateOption> baseRates { get; set; } = new List<InternationalBaseRateOption>();
        public List<InternationalBaseRateOption> baseRateOptions { get; set; } = new List<InternationalBaseRateOption>();
        public List<InternationalExtraServiceQuote> extraServiceRates { get; set; } = new List<InternationalExtraServiceQuote>();
        public List<InternationalTotalRateOption> totalRates { get; set; } = new List<InternationalTotalRateOption>();
    }

    public sealed class InternationalBaseRateOption
    {
        public decimal totalBasePrice { get; set; }
        public List<InternationalBaseRateQuote> rates { get; set; } = new List<InternationalBaseRateQuote>();
        public List<string> warnings { get; set; } = new List<string>();
    }

    public sealed class InternationalTotalRateOption : InternationalBaseRateOption
    {
        public decimal totalPrice { get; set; }
        public List<InternationalExtraServiceQuote> extraServices { get; set; } = new List<InternationalExtraServiceQuote>();
    }

    public sealed class InternationalBaseRateQuote
    {
        public string sku { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string mailClass { get; set; } = string.Empty;
        public string priceType { get; set; } = string.Empty;
        public string zone { get; set; } = string.Empty;
        public decimal price { get; set; }
        public decimal? weight { get; set; }
        public decimal? dimensionalWeight { get; set; }
        public string startDate { get; set; } = string.Empty;
        public string endDate { get; set; } = string.Empty;
        public List<InternationalRateFee> fees { get; set; } = new List<InternationalRateFee>();
        public List<string> warnings { get; set; } = new List<string>();
    }

    public sealed class InternationalRateFee
    {
        public string description { get; set; } = string.Empty;
        public decimal price { get; set; }
    }

    public sealed class InternationalExtraServiceQuote
    {
        public string sku { get; set; } = string.Empty;
        public string priceType { get; set; } = string.Empty;
        public decimal price { get; set; }
        public string extraService { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public List<string> warnings { get; set; } = new List<string>();
    }
    #endregion

    #region Internal USPS response models
    internal sealed class UspsInternationalBaseRatesResponse
    {
        [JsonProperty("rates")]
        public List<UspsInternationalRate>? rates { get; set; }

        [JsonProperty("totalBasePrice")]
        public decimal? totalBasePrice { get; set; }

        [JsonProperty("warnings")]
        public List<string>? warnings { get; set; }
    }

    internal sealed class UspsInternationalRateOptionsResponse
    {
        [JsonProperty("rateOptions")]
        public List<UspsInternationalRateOption> rateOptions { get; set; } = new List<UspsInternationalRateOption>();
    }

    internal sealed class UspsInternationalRateOption
    {
        [JsonProperty("rates")]
        public List<UspsInternationalRate>? rates { get; set; }

        [JsonProperty("totalBasePrice")]
        public decimal? totalBasePrice { get; set; }

        [JsonProperty("warnings")]
        public List<string>? warnings { get; set; }
    }

    internal sealed class UspsInternationalTotalRatesResponse
    {
        [JsonProperty("rateOptions")]
        public List<UspsInternationalTotalRateOption> rateOptions { get; set; } = new List<UspsInternationalTotalRateOption>();
    }

    internal sealed class UspsInternationalTotalRateOption
    {
        [JsonProperty("rates")]
        public List<UspsInternationalRate>? rates { get; set; }

        [JsonProperty("extraServices")]
        public List<UspsInternationalExtraService>? extraServices { get; set; }

        [JsonProperty("totalBasePrice")]
        public decimal? totalBasePrice { get; set; }

        [JsonProperty("totalPrice")]
        public decimal? totalPrice { get; set; }

        [JsonProperty("warnings")]
        public List<string>? warnings { get; set; }
    }

    internal sealed class UspsInternationalRate
    {
        [JsonProperty("SKU")]
        public string? SKU { get; set; }

        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("mailClass")]
        public string? mailClass { get; set; }

        [JsonProperty("priceType")]
        public string? priceType { get; set; }

        [JsonProperty("zone")]
        public string? zone { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }

        [JsonProperty("weight")]
        public decimal? weight { get; set; }

        [JsonProperty("dimWeight")]
        public decimal? dimWeight { get; set; }

        [JsonProperty("dimensionalWeight")]
        public decimal? dimensionalWeight { get; set; }

        [JsonProperty("fees")]
        public List<UspsInternationalFee>? fees { get; set; }

        [JsonProperty("startDate")]
        public string? startDate { get; set; }

        [JsonProperty("endDate")]
        public string? endDate { get; set; }

        [JsonProperty("warnings")]
        public List<string>? warnings { get; set; }
    }

    internal sealed class UspsInternationalFee
    {
        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }
    }

    internal sealed class UspsInternationalExtraService
    {
        [JsonProperty("SKU")]
        public string? SKU { get; set; }

        [JsonProperty("priceType")]
        public string? priceType { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }

        [JsonProperty("extraService")]
        public string? extraService { get; set; }

        [JsonProperty("name")]
        public string? name { get; set; }

        [JsonProperty("warnings")]
        public List<string>? warnings { get; set; }
    }
    #endregion
}
