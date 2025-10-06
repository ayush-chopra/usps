using System;
using System.IO;
using System.Net;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;

namespace UspsProcessor
{
    public class UspsDomesticLabelsProcessor
    {
        private static readonly Logger _logger;
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsDomesticLabelsProcessor()
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

        #region CreateAsync
        public async Task<DomesticLabelCreationResponse> CreateAsync(DomesticLabelCreationRequest request)
        {
            DomesticLabelCreationResponse response = new DomesticLabelCreationResponse
            {
                isSuccess = true,
                errorDesc = string.Empty
            };

            try
            {
                string token = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    string acceptHeader = ResolveAcceptHeader(request.Format);
                    string payload = JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    string url = string.Format("{0}labels/v3/domestic", _url);
                    HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.Method = "POST";
                    webRequest.ContentType = "application/json";
                    webRequest.Accept = acceptHeader;
                    webRequest.Headers.Add("Authorization", "Bearer " + token);

                    byte[] byteData = Encoding.UTF8.GetBytes(payload);
                    webRequest.ContentLength = byteData.Length;
                    using (Stream postStream = webRequest.GetRequestStream())
                    {
                        postStream.Write(byteData, 0, byteData.Length);
                    }

                    using (HttpWebResponse webResponse = (HttpWebResponse)await webRequest.GetResponseAsync())
                    {
                        response.trackingNumber = webResponse.Headers["X-Tracking-Number"] ?? string.Empty;
                        response.contentType = string.IsNullOrWhiteSpace(webResponse.ContentType) ? acceptHeader : webResponse.ContentType;

                        using (Stream dataStream = webResponse.GetResponseStream())
                        {
                            if (dataStream != null)
                            {
                                using MemoryStream ms = new MemoryStream();
                                dataStream.CopyTo(ms);
                                response.content = ms.ToArray();
                            }
                        }
                    }
                }
                else
                {
                    response.isSuccess = false;
                    response.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (WebException wx)
            {
                response.isSuccess = false;
                response.errorDesc = "Error creating USPS label";
                _logger.Error("WebException - Message->{0}\nInnerException->{1}", wx.Message, wx.InnerException);

                HttpWebResponse? webResp = wx.Response as HttpWebResponse;
                if (webResp != null)
                {
                    Stream? stream = webResp.GetResponseStream();
                    if (stream != null)
                    {
                        using StreamReader reader = new StreamReader(stream);
                        string errorBody = reader.ReadToEnd();
                        response.errorDesc = string.IsNullOrWhiteSpace(errorBody) ? response.errorDesc : errorBody;
                        _logger.Error("WebException = {0}", errorBody);
                    }
                }
            }
            catch (Exception ex)
            {
                response.isSuccess = false;
                response.errorDesc = "unavailable";
                _logger.Error("USPS Domestic Labels Exception = {0}", ex.Message);
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
                if (method != "auth")
                {
                    throw new InvalidOperationException(string.Format("Unsupported USPS method '{0}'", method));
                }

                string url = string.Format("{0}oauth2/v3/token", _url);
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                webRequest.ContentType = "application/json";
                webRequest.Method = "POST";

                dynamic body = new System.Dynamic.ExpandoObject();
                body.grant_type = "client_credentials";
                body.client_id = _clientId;
                body.client_secret = _clientSecret;
                payload = JsonConvert.SerializeObject(body);

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
        private static string ResolveAcceptHeader(string? format)
        {
            string fmt = string.IsNullOrWhiteSpace(format) ? "pdf" : format.ToLowerInvariant();
            return fmt switch
            {
                "svg" => "image/svg+xml",
                "zpl" => "text/plain",
                _ => "application/pdf"
            };
        }
        #endregion
    }

    public sealed class DomesticLabelCreationRequest
    {
        [JsonProperty("service")]
        public string Service { get; set; } = string.Empty;

        [JsonProperty("format")]
        public string Format { get; set; } = "pdf";

        [JsonProperty("shipFrom")]
        public LabelAddress ShipFrom { get; set; } = new LabelAddress();

        [JsonProperty("shipTo")]
        public LabelAddress ShipTo { get; set; } = new LabelAddress();

        [JsonProperty("weightOz")]
        public decimal WeightOz { get; set; }

        [JsonProperty("reference")]
        public string? Reference { get; set; }
    }

    public sealed class LabelAddress
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("addressLine1")]
        public string AddressLine1 { get; set; } = string.Empty;

        [JsonProperty("addressLine2")]
        public string? AddressLine2 { get; set; }

        [JsonProperty("city")]
        public string City { get; set; } = string.Empty;

        [JsonProperty("state")]
        public string State { get; set; } = string.Empty;

        [JsonProperty("zipCode")]
        public string ZipCode { get; set; } = string.Empty;
    }

    public sealed class DomesticLabelCreationResponse
    {
        public bool isSuccess { get; set; }
        public string errorDesc { get; set; } = string.Empty;
        public string trackingNumber { get; set; } = string.Empty;
        public string contentType { get; set; } = string.Empty;
        public byte[] content { get; set; } = Array.Empty<byte>();
    }
}
