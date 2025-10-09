using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;

namespace UspsProcessor
{
    public class UspsShippingOptionsProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsShippingOptionsProcessor()
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
        public async Task<ShippingOptionsQuoteResponse> QuoteAsync(ShippingOptionsQuoteRequest request)
        {
            ShippingOptionsQuoteResponse quoteResponse = new ShippingOptionsQuoteResponse
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

                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("shipping-options", payload);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            try
                            {
                                UspsShippingOptionsResponse? shippingResponse = JsonConvert.DeserializeObject<UspsShippingOptionsResponse>(genericResponse.message);
                                List<ShippingOptionQuote> mappedQuotes = MapShippingOptions(shippingResponse, request).ToList();

                                if (mappedQuotes.Count > 0)
                                {
                                    quoteResponse.options.AddRange(mappedQuotes);
                                }
                                else
                                {
                                    quoteResponse.isSuccess = false;
                                    quoteResponse.errorDesc = "No shipping options returned";
                                }
                            }
                            catch (JsonException jx)
                            {
                                quoteResponse.isSuccess = false;
                                quoteResponse.errorDesc = "Unable to parse shipping options response";
                                _logger.Error("USPS Shipping Options Parse Error = {0}", jx.Message);
                            }
                        }
                        else
                        {
                            quoteResponse.isSuccess = false;
                            quoteResponse.errorDesc = "Blank response received from shipping options call";
                        }
                    }
                    else
                    {
                        quoteResponse.isSuccess = false;
                        quoteResponse.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error) ? genericResponse.error : "Error processing shipping options request";
                        _logger.Error("USPS Shipping Options Error = {0}", quoteResponse.errorDesc);
                    }
                }
                else
                {
                    quoteResponse.isSuccess = false;
                    quoteResponse.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (Exception ex)
            {
                quoteResponse.isSuccess = false;
                quoteResponse.errorDesc = "unavailable";
                _logger.Error("USPS Shipping Options Exception = {0}", ex.Message);
            }

            return quoteResponse;
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
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";

                    dynamic body = new System.Dynamic.ExpandoObject();
                    body.grant_type = "client_credentials";
                    body.client_id = _clientId;
                    body.client_secret = _clientSecret;
                    payload = JsonConvert.SerializeObject(body);
                }
                else if (method == "shipping-options")
                {
                    url = string.Format("{0}shipments/v3/options/search", _url);
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

        private static IEnumerable<ShippingOptionQuote> MapShippingOptions(UspsShippingOptionsResponse? response, ShippingOptionsQuoteRequest request)
        {
            if (response?.pricingOptions == null)
            {
                yield break;
            }

            string? mailingDate = request?.PackageDescription?.MailingDate;

            foreach (UspsShippingPricingOption? pricingOption in response.pricingOptions)
            {
                if (pricingOption?.shippingOptions == null)
                {
                    continue;
                }

                foreach (UspsShippingOption? shippingOption in pricingOption.shippingOptions)
                {
                    if (shippingOption?.rateOptions == null || shippingOption.rateOptions.Count == 0)
                    {
                        continue;
                    }

                    foreach (UspsShippingRateOption? rateOption in shippingOption.rateOptions)
                    {
                        if (rateOption == null)
                        {
                            continue;
                        }

                        decimal price = rateOption.totalPrice
                            ?? rateOption.totalBasePrice
                            ?? rateOption.rates?.FirstOrDefault()?.price
                            ?? 0m;

                        string currency = rateOption.currencyCode
                            ?? rateOption.rates?.FirstOrDefault()?.currency
                            ?? "USD";

                        string service = !string.IsNullOrWhiteSpace(shippingOption.mailClass)
                            ? shippingOption.mailClass
                            : rateOption.rates?.FirstOrDefault()?.description
                                ?? "USPS";

                        int estimatedDays = EstimateDays(rateOption.commitment, mailingDate);

                        yield return new ShippingOptionQuote
                        {
                            service = service,
                            price = price,
                            currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
                            estimatedDays = estimatedDays
                        };
                    }
                }
            }
        }

        private static int EstimateDays(UspsShippingCommitment? commitment, string? mailingDate)
        {
            if (commitment == null)
            {
                return 0;
            }

            if (commitment.estimatedDays.HasValue && commitment.estimatedDays.Value >= 0)
            {
                return commitment.estimatedDays.Value;
            }

            if (!string.IsNullOrWhiteSpace(commitment.name))
            {
                Match match = Regex.Match(commitment.name, "(\\d+)");
                if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDays))
                {
                    return parsedDays;
                }
            }

            if (!string.IsNullOrWhiteSpace(commitment.scheduleDeliveryDate) && !string.IsNullOrWhiteSpace(mailingDate))
            {
                if (DateTime.TryParse(mailingDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime mailingDt)
                    && DateTime.TryParse(commitment.scheduleDeliveryDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime deliveryDt))
                {
                    int days = (int)Math.Round((deliveryDt.Date - mailingDt.Date).TotalDays);
                    if (days >= 0)
                    {
                        return days;
                    }
                }
            }

            return 0;
        }
    }

    public sealed class ShippingOptionsQuoteRequest
    {
        [JsonProperty("pricingOptions")]
        public List<ShippingOptionsPricingOption>? PricingOptions { get; set; }

        [JsonProperty("originZIPCode")]
        public string OriginZIPCode { get; set; } = string.Empty;

        [JsonProperty("destinationZIPCode")]
        public string DestinationZIPCode { get; set; } = string.Empty;

        [JsonProperty("originCountryCode")]
        public string? OriginCountryCode { get; set; }

        [JsonProperty("destinationCountryCode")]
        public string? DestinationCountryCode { get; set; }

        [JsonProperty("packageDescription")]
        public ShippingPackageDescription? PackageDescription { get; set; } = new ShippingPackageDescription();

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

        [JsonIgnore]
        public decimal WeightOz
        {
            get => PackageDescription?.Weight ?? 0m;
            set => EnsurePackageDescription().Weight = value;
        }

        [JsonIgnore]
        public PackageDimensions? Dimensions
        {
            get
            {
                if (PackageDescription == null)
                {
                    return null;
                }

                if (PackageDescription.Length.HasValue
                    || PackageDescription.Width.HasValue
                    || PackageDescription.Height.HasValue)
                {
                    return new PackageDimensions
                    {
                        LengthIn = PackageDescription.Length ?? 0m,
                        WidthIn = PackageDescription.Width ?? 0m,
                        HeightIn = PackageDescription.Height ?? 0m
                    };
                }

                return null;
            }
            set
            {
                ShippingPackageDescription package = EnsurePackageDescription();
                if (value == null)
                {
                    package.Length = null;
                    package.Width = null;
                    package.Height = null;
                }
                else
                {
                    package.Length = value.LengthIn;
                    package.Width = value.WidthIn;
                    package.Height = value.HeightIn;
                }
            }
        }

        private ShippingPackageDescription EnsurePackageDescription()
        {
            if (PackageDescription == null)
            {
                PackageDescription = new ShippingPackageDescription();
            }

            return PackageDescription;
        }
    }

    public sealed class ShippingOptionsPricingOption
    {
        [JsonProperty("priceType")]
        public string? PriceType { get; set; }

        [JsonProperty("paymentAccount")]
        public ShippingOptionsPaymentAccount? PaymentAccount { get; set; }
    }

    public sealed class ShippingOptionsPaymentAccount
    {
        [JsonProperty("accountType")]
        public string? AccountType { get; set; }

        [JsonProperty("accountNumber")]
        public string? AccountNumber { get; set; }
    }

    public sealed class ShippingPackageDescription
    {
        [JsonProperty("weight")]
        public decimal? Weight { get; set; }

        [JsonProperty("length")]
        public decimal? Length { get; set; }

        [JsonProperty("height")]
        public decimal? Height { get; set; }

        [JsonProperty("width")]
        public decimal? Width { get; set; }

        [JsonProperty("girth")]
        public decimal? Girth { get; set; }

        [JsonProperty("mailClass")]
        public string? MailClass { get; set; }

        [JsonProperty("extraServices")]
        public List<int>? ExtraServices { get; set; }

        [JsonProperty("mailingDate")]
        public string? MailingDate { get; set; }

        [JsonProperty("packageValue")]
        public decimal? PackageValue { get; set; }
    }

    public sealed class ShippingOptionsQuoteResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public List<ShippingOptionQuote> options { get; set; } = new List<ShippingOptionQuote>();
    }

    public sealed class ShippingOptionQuote
    {
        public string service { get; set; } = string.Empty;
        public decimal price { get; set; }
        public string currency { get; set; } = "USD";
        public int estimatedDays { get; set; }
    }

    internal sealed class UspsShippingOptionsResponse
    {
        [JsonProperty("pricingOptions")]
        public List<UspsShippingPricingOption>? pricingOptions { get; set; }
    }

    internal sealed class UspsShippingPricingOption
    {
        [JsonProperty("shippingOptions")]
        public List<UspsShippingOption>? shippingOptions { get; set; }
    }

    internal sealed class UspsShippingOption
    {
        [JsonProperty("mailClass")]
        public string? mailClass { get; set; }

        [JsonProperty("rateOptions")]
        public List<UspsShippingRateOption>? rateOptions { get; set; }
    }

    internal sealed class UspsShippingRateOption
    {
        [JsonProperty("commitment")]
        public UspsShippingCommitment? commitment { get; set; }

        [JsonProperty("totalPrice")]
        public decimal? totalPrice { get; set; }

        [JsonProperty("totalBasePrice")]
        public decimal? totalBasePrice { get; set; }

        [JsonProperty("currencyCode")]
        public string? currencyCode { get; set; }

        [JsonProperty("rates")]
        public List<UspsShippingRate>? rates { get; set; }

        [JsonProperty("extraServices")]
        public List<UspsShippingExtraService>? extraServices { get; set; }
    }

    internal sealed class UspsShippingCommitment
    {
        [JsonProperty("name")]
        public string? name { get; set; }

        [JsonProperty("scheduleDeliveryDate")]
        public string? scheduleDeliveryDate { get; set; }

        [JsonProperty("estimatedDays")]
        public int? estimatedDays { get; set; }
    }

    internal sealed class UspsShippingRate
    {
        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }

        [JsonProperty("currency")]
        public string? currency { get; set; }

        [JsonProperty("zone")]
        public string? zone { get; set; }

        [JsonProperty("weight")]
        public decimal? weight { get; set; }

        [JsonProperty("dimensionalWeight")]
        public decimal? dimensionalWeight { get; set; }

        [JsonProperty("fees")]
        public List<UspsShippingFee>? fees { get; set; }

        [JsonProperty("SKU")]
        public string? sku { get; set; }
    }

    internal sealed class UspsShippingFee
    {
        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }
    }

    internal sealed class UspsShippingExtraService
    {
        [JsonProperty("name")]
        public string? name { get; set; }

        [JsonProperty("price")]
        public decimal? price { get; set; }

        [JsonProperty("SKU")]
        public string? sku { get; set; }
    }
}
