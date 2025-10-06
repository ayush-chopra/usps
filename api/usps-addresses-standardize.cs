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
    public class UspsAddressesStandardizeProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsAddressesStandardizeProcessor()
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

        #region StandardizeAsync
        public async Task<StandardizeAddressResult> StandardizeAsync(StandardizeAddressRequest request)
        {
            StandardizeAddressResult result = new StandardizeAddressResult
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
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("addresses-standardize", payload);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            UspsStandardizeAddressResponse stdResponse = JsonConvert.DeserializeObject<UspsStandardizeAddressResponse>(genericResponse.message);
                            if (stdResponse != null && stdResponse.addresses != null && stdResponse.addresses.Count > 0)
                            {
                                foreach (UspsStandardizedAddress addr in stdResponse.addresses)
                                {
                                    result.addresses.Add(new StandardizedAddress
                                    {
                                        addressLine1 = addr.addressLine1,
                                        addressLine2 = addr.addressLine2,
                                        city = addr.city,
                                        state = addr.state,
                                        zipCode = addr.zipCode,
                                        valid = addr.valid
                                    });
                                }
                            }
                            else
                            {
                                result.isSuccess = false;
                                result.errorDesc = "No addresses returned from standardize call";
                            }
                        }
                        else
                        {
                            result.isSuccess = false;
                            result.errorDesc = "Blank response received from address standardize call";
                        }
                    }
                    else
                    {
                        result.isSuccess = false;
                        result.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error) ? genericResponse.error : "Error processing address standardize request";
                        _logger.Error("USPS Address Standardize Error = {0}", result.errorDesc);
                    }
                }
                else
                {
                    result.isSuccess = false;
                    result.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (Exception ex)
            {
                result.isSuccess = false;
                result.errorDesc = "unavailable";
                _logger.Error("USPS Address Standardize Exception = {0}", ex.Message);
            }

            return result;
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
                else if (method == "addresses-standardize")
                {
                    url = string.Format("{0}addresses/v3/standardize", _url);
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

    public sealed class StandardizeAddressRequest
    {
        [JsonProperty("addresses")]
        public List<AddressStandardizationInput> Addresses { get; set; } = new List<AddressStandardizationInput>();
    }

    public sealed class AddressStandardizationInput
    {
        [JsonProperty("addressLine1")]
        public string AddressLine1 { get; set; } = string.Empty;

        [JsonProperty("addressLine2")]
        public string? AddressLine2 { get; set; }

        [JsonProperty("city")]
        public string? City { get; set; }

        [JsonProperty("state")]
        public string? State { get; set; }

        [JsonProperty("zipCode")]
        public string? ZipCode { get; set; }
    }

    public sealed class StandardizeAddressResult
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public List<StandardizedAddress> addresses { get; set; } = new List<StandardizedAddress>();
    }

    public sealed class StandardizedAddress
    {
        public string addressLine1 { get; set; } = string.Empty;
        public string? addressLine2 { get; set; }
        public string city { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public string zipCode { get; set; } = string.Empty;
        public bool valid { get; set; }
    }

    internal sealed class UspsStandardizeAddressResponse
    {
        public List<UspsStandardizedAddress> addresses { get; set; } = new List<UspsStandardizedAddress>();
    }

    internal sealed class UspsStandardizedAddress
    {
        public string addressLine1 { get; set; } = string.Empty;
        public string? addressLine2 { get; set; }
        public string city { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public string zipCode { get; set; } = string.Empty;
        public bool valid { get; set; }
    }
}
