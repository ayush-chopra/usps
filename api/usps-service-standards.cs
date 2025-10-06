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
    public class UspsServiceStandardsProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsServiceStandardsProcessor()
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

        #region LookupAsync
        public async Task<ServiceStandardsLookupResult> LookupAsync(ServiceStandardsLookupRequest request)
        {
            ServiceStandardsLookupResult result = new ServiceStandardsLookupResult
            {
                isSuccess = true,
                errorDesc = string.Empty,
                lookup = null
            };

            try
            {
                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    string payload = JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("service-standards-lookup", payload);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            UspsServiceStandardsLookupResponse lookupResponse = JsonConvert.DeserializeObject<UspsServiceStandardsLookupResponse>(genericResponse.message);
                            if (lookupResponse != null && !string.IsNullOrWhiteSpace(lookupResponse.service))
                            {
                                result.lookup = new ServiceStandardsLookup
                                {
                                    service = lookupResponse.service,
                                    estimatedDays = lookupResponse.estimatedDays,
                                    estimatedDeliveryDate = lookupResponse.estimatedDeliveryDate
                                };
                            }
                            else
                            {
                                result.isSuccess = false;
                                result.errorDesc = "No service standard data returned";
                            }
                        }
                        else
                        {
                            result.isSuccess = false;
                            result.errorDesc = "Blank response received from service standards lookup";
                        }
                    }
                    else
                    {
                        result.isSuccess = false;
                        result.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error) ? genericResponse.error : "Error processing service standards lookup";
                        _logger.Error("USPS Service Standards Lookup Error = {0}", result.errorDesc);
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
                _logger.Error("USPS Service Standards Lookup Exception = {0}", ex.Message);
            }

            return result;
        }
        #endregion

        #region GetFilesAsync
        public async Task<ServiceStandardsFilesResult> GetFilesAsync()
        {
            ServiceStandardsFilesResult result = new ServiceStandardsFilesResult
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            try
            {
                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    UspsGenericResponse genericResponse = await MakeRestRequestAsync("service-standards-files", string.Empty);
                    if (genericResponse.isSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(genericResponse.message))
                        {
                            UspsServiceStandardsFilesResponse filesResponse = JsonConvert.DeserializeObject<UspsServiceStandardsFilesResponse>(genericResponse.message);
                            if (filesResponse != null && filesResponse.files != null && filesResponse.files.Count > 0)
                            {
                                result.files.AddRange(filesResponse.files);
                            }
                            else
                            {
                                result.isSuccess = false;
                                result.errorDesc = "No service standard files returned";
                            }
                        }
                        else
                        {
                            result.isSuccess = false;
                            result.errorDesc = "Blank response received from service standards files call";
                        }
                    }
                    else
                    {
                        result.isSuccess = false;
                        result.errorDesc = !string.IsNullOrWhiteSpace(genericResponse.error) ? genericResponse.error : "Error processing service standards files request";
                        _logger.Error("USPS Service Standards Files Error = {0}", result.errorDesc);
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
                _logger.Error("USPS Service Standards Files Exception = {0}", ex.Message);
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
                bool sendPayload = false;

                if (method == "auth")
                {
                    url = string.Format("{0}oauth2/v3/token", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    sendPayload = true;

                    dynamic body = new System.Dynamic.ExpandoObject();
                    body.grant_type = "client_credentials";
                    body.client_id = _clientId;
                    body.client_secret = _clientSecret;
                    payload = JsonConvert.SerializeObject(body);
                }
                else if (method == "service-standards-lookup")
                {
                    url = string.Format("{0}servicestandards/v3/lookup", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";
                    sendPayload = true;
                }
                else if (method == "service-standards-files")
                {
                    url = string.Format("{0}servicestandards/v3/files", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string token = MemoryCache.Default["uspsToken"] as string ?? string.Empty;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "GET";
                }
                else
                {
                    throw new InvalidOperationException(string.Format("Unsupported USPS method '{0}'", method));
                }

                if (sendPayload)
                {
                    byte[] byteData = Encoding.UTF8.GetBytes(payload ?? string.Empty);
                    webRequest.ContentLength = byteData.Length;
                    using (Stream postStream = webRequest.GetRequestStream())
                    {
                        postStream.Write(byteData, 0, byteData.Length);
                    }
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

    public sealed class ServiceStandardsLookupRequest
    {
        [JsonProperty("originZip")]
        public string OriginZip { get; set; } = string.Empty;

        [JsonProperty("destinationZip")]
        public string DestinationZip { get; set; } = string.Empty;

        [JsonProperty("service")]
        public string Service { get; set; } = string.Empty;
    }

    public sealed class ServiceStandardsLookupResult
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public ServiceStandardsLookup? lookup { get; set; }
    }

    public sealed class ServiceStandardsLookup
    {
        public string service { get; set; } = string.Empty;
        public int estimatedDays { get; set; }
        public DateTime? estimatedDeliveryDate { get; set; }
    }

    public sealed class ServiceStandardsFilesResult
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public List<string> files { get; set; } = new List<string>();
    }

    internal sealed class UspsServiceStandardsLookupResponse
    {
        public string service { get; set; } = string.Empty;
        public int estimatedDays { get; set; }
        public DateTime? estimatedDeliveryDate { get; set; }
    }

    internal sealed class UspsServiceStandardsFilesResponse
    {
        public List<string> files { get; set; } = new List<string>();
    }
}
