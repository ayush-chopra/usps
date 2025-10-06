using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Caching;
using System.Text;
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
                    string payload = JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("shipping-options", payload);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            UspsShippingOptionsResponse shippingResponse = JsonConvert.DeserializeObject<UspsShippingOptionsResponse>(genericResponse.message);
                            if (shippingResponse != null && shippingResponse.options != null && shippingResponse.options.Count > 0)
                            {
                                foreach (UspsShippingOption option in shippingResponse.options)
                                {
                                    quoteResponse.options.Add(new ShippingOptionQuote
                                    {
                                        service = option.service,
                                        price = option.price,
                                        currency = string.IsNullOrWhiteSpace(option.currency) ? "USD" : option.currency,
                                        estimatedDays = option.estimatedDays
                                    });
                                }
                            }
                            else
                            {
                                quoteResponse.isSuccess = false;
                                quoteResponse.errorDesc = "No shipping options returned";
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
                    url = string.Format("{0}shippingoptions/v3/quote", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
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

    public sealed class ShippingOptionsQuoteRequest
    {
        [JsonProperty("originZip")]
        public string OriginZip { get; set; } = string.Empty;

        [JsonProperty("destinationZip")]
        public string DestinationZip { get; set; } = string.Empty;

        [JsonProperty("weightOz")]
        public decimal WeightOz { get; set; }

        [JsonProperty("dimensions")]
        public PackageDimensions? Dimensions { get; set; }
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
        public List<UspsShippingOption> options { get; set; } = new List<UspsShippingOption>();
    }

    internal sealed class UspsShippingOption
    {
        public string service { get; set; } = string.Empty;
        public decimal price { get; set; }
        public string currency { get; set; } = "USD";
        public int estimatedDays { get; set; }
    }
}
