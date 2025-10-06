using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using NLog;
using Newtonsoft.Json;

namespace UspsProcessor
{
    public class UspsRestProcessor
    {
        private static string _clientId = string.Empty;
        private static string _clientSecret = string.Empty;
        private static string _url = string.Empty;

        static UspsRestProcessor()
        {
            // USPSServiceUrl = https://apis.usps.com/

            _logger = LogManager.GetCurrentClassLogger();

            // add blank restToken and expiryDate on startup
            MemoryCache.Default["uspsToken"] = string.Empty;
            MemoryCache.Default["uspsTknExpiry"] = DateTime.UtcNow.AddHours(-1);
        }


        #region ValidateAddress
        public async Task<AddressCheckResponse> ValidateAddress(Address addr)
        {
            AddressCheckResponse acr = new AddressCheckResponse();
            acr.isSuccess = true;
            acr.errorDesc = string.Empty;

            try
            {
                string tkn = await ValidateRestTokenAsync();
                if (!string.IsNullOrWhiteSpace(tkn))
                {
                    if (addr.country.Length == 0 || (addr.country.Length > 0 && addr.country == "US"))
                    {
                        // if zip > 5 digits then grab first 5
                        string zip5 = (addr.postalCode.Length > 5) ? addr.postalCode.Substring(0, 5) : addr.postalCode;

                        StringBuilder sb = new StringBuilder();
                        sb.Append("streetAddress=" + HttpUtility.HtmlEncode(addr.address1));
                        sb.Append("&secondaryAddress=" + HttpUtility.HtmlEncode(addr.address2));
                        sb.Append("&city=" + HttpUtility.HtmlEncode(addr.city));
                        sb.Append("&state=" + addr.state);
                        sb.Append("&ZIPCode=" + zip5);

                        UspsGenericResponse ugr = await MakeRestRequestAsync("address", sb.ToString());
                        if (ugr.isSuccess)
                        {
                            if (!string.IsNullOrWhiteSpace(ugr.message))
                            {
                                UspsAddrCheckResponse uacr = JsonConvert.DeserializeObject<UspsAddrCheckResponse>(ugr.message);
                                if (uacr != null && uacr.additionalInfo != null && uacr.address != null)
                                {
                                    // DPVConfirmation values are
                                    // Y 'Address was DPV confirmed for both primary and (if present) secondary numbers.'
                                    // D 'Address was DPV confirmed for the primary number only, and the secondary number information was missing.'
                                    // S 'Address was DPV confirmed for the primary number only, and the secondary number information was present but not confirmed.'
                                    // N 'Both primary and (if present) secondary number information failed to DPV confirm.'
                                    if (!string.IsNullOrWhiteSpace(uacr.additionalInfo.DPVConfirmation) && uacr.additionalInfo.DPVConfirmation.ToUpper() != "N")
                                    {
                                        // valid address
                                        acr.isValid = true;
                                        acr.address1 = uacr.address.streetAddress;
                                        acr.address2 = uacr.address.secondaryAddress;
                                        acr.city = uacr.address.city;
                                        acr.state = uacr.address.state;
                                        acr.postalCode = uacr.address.ZIPCode;
                                        acr.country = "US";
                                        acr.isResidential = (uacr.additionalInfo.business.ToUpper() == "N") ? true : false;

                                        if (uacr.corrections != null && uacr.corrections.Count > 0)
                                        {
                                            if (uacr.corrections.Any(o => o.code == "32"))
                                            {
                                                FedExAddressCheckRest fdxHelper = new FedExAddressCheckRest();
                                                AddressCheckResponse facr = await fdxHelper.ValidateAddress(addr);
                                                if (facr.isSuccess && facr.isValid)
                                                {
                                                    acr.isResidential = facr.isResidential;
                                                }
                                            }
                                        }

                                        // populate isAdjusted
                                        // remove all special chars from city 
                                        string city = Util.SnipSpecialChars(addr.city, true);
                                        string respCity = Util.SnipSpecialChars(acr.city, true);
                                        // check city, state and zip in response and compare(case insensitive) to request 
                                        if (city.ToLower() != respCity.ToLower() || addr.state.ToUpper() != acr.state.ToUpper() || zip5 != acr.postalCode)
                                        {
                                            acr.isAdjusted = true;
                                        }

                                        // usps will not deliver if notes contain R7
                                        // R7 — R777 Phantom Route Matching. Physical addresses that are assigned to a phantom are not eligible for street delivery
                                        if (uacr.additionalInfo.carrierRoute.ToUpper().Contains("R7"))
                                        {
                                            acr.isValid = false;
                                            acr.errorDesc = "Delivery not available by USPS";
                                        }
                                    }
                                    else
                                    {
                                        // invalid address
                                        acr.isValid = false;
                                        acr.errorDesc = "Invalid address";
                                    }
                                }
                                else
                                {
                                    acr.isValid = false;
                                    acr.errorDesc = "Unknown error during address validation call";
                                }
                            }
                            else
                            {
                                acr.isValid = false;
                                acr.errorDesc = "Blank response received from address validation call";
                            }
                        }
                        else
                        {
                            acr.isSuccess = false;
                            acr.isValid = false;
                            acr.errorDesc = (!string.IsNullOrWhiteSpace(ugr.error)) ? ugr.error : "Error processing adress request";
                            _logger.Error("USPS Error = {0}", acr.errorDesc);
                        }
                    }
                }
                else
                {
                    acr.isValid = false;
                    acr.errorDesc = "Invalid token for USPS API";
                }
            }
            catch (Exception ex)
            {
                acr.isValid = false;
                acr.errorDesc = "unavailable";
                _logger.Error("USPS Address Exception = {0}", ex.Message);
            }

            return acr;
        }
        #endregion


        #region ValidateRestTokenAsync
        // we don't need to return anything as we are saving token in memory but since it is 
        // async method so we want to return a task that produced a token when it was done
        private async Task<string> ValidateRestTokenAsync()
        {
            bool isSuccess = false;
            string tkn = (string)MemoryCache.Default["uspsToken"];
            DateTime expiryDt = (DateTime)MemoryCache.Default["uspsTknExpiry"];
            // if token is blank or null OR DateTime.UtcNow is greater than or equal to expiryDate then authenticate again
            if (string.IsNullOrWhiteSpace(tkn) || (!string.IsNullOrWhiteSpace(tkn) && DateTime.UtcNow >= expiryDt))
            {
                UspsGenericResponse ugr = await MakeRestRequestAsync("auth", string.Empty);
                if (ugr.isSuccess && !string.IsNullOrWhiteSpace(ugr.message))
                {
                    UspsAuthResponse authResp = JsonConvert.DeserializeObject<UspsAuthResponse>(ugr.message);
                    if (authResp != null && !string.IsNullOrWhiteSpace(authResp.access_token))
                    {
                        // get utc time now and add ttl-20 secs
                        MemoryCache.Default["uspsTknExpiry"] = DateTime.UtcNow.AddSeconds(authResp.expires_in - 20);
                        MemoryCache.Default["uspsToken"] = authResp.access_token;
                        isSuccess = true;
                    }
                }
                else
                {
                    UspsAuthResponse authResp = JsonConvert.DeserializeObject<UspsAuthResponse>(ugr.message);
                    if (!string.IsNullOrWhiteSpace(authResp.error))
                    {
                        isSuccess = false;
                        _logger.Error("ValidateRestTokenAsync error = {0}", authResp.error);
                    }
                }
            }
            else
            {
                isSuccess = true;
            }
            tkn = (isSuccess) ? (string)MemoryCache.Default["uspsToken"] : string.Empty;
            return tkn;
        }
        #endregion


        #region MakeRestRequestAsync
        private async Task<UspsGenericResponse> MakeRestRequestAsync(string method, string payload)
        {
            UspsGenericResponse response = new UspsGenericResponse();
            response.isSuccess = true;

            try
            {
                HttpWebRequest webRequest = null;

                string url = string.Empty;
                if (method == "auth")
                {
                    url = string.Format("{0}oauth2/v3/token", _url);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "POST";

                    dynamic odyn = new System.Dynamic.ExpandoObject();
                    odyn.grant_type = "client_credentials";
                    odyn.client_id = _clientId;
                    odyn.client_secret = _clientSecret;
                    //odyn.scope = "oauth2-oidc addresses domestic-prices international-prices labels international-labels service-standards service-standards-files locations prices tracking pickup scan-form payments usps:payment_methods shipments";
                    // when no scope passed in - it returns default scope - shown below
                    //domestic-prices  oauth2-oidc addresses international-prices  usps:payment_methods openid service-standards-files service-standards locations  usps:MIDs shipments
                    // seems to not include tracking labels scan-form etc - how to change it?? 2025.07.15
                    payload = JsonConvert.SerializeObject(odyn);
                }
                else
                {
                    url = string.Format("{0}addresses/v3/address?{1}", _url, payload);
                    webRequest = (HttpWebRequest)WebRequest.Create(url);
                    string tkn = (string)MemoryCache.Default["uspsToken"];
                    webRequest.Headers.Add("Authorization", "Bearer " + tkn);
                    webRequest.ContentType = "application/json";
                    webRequest.Method = "GET";
                }

                if (method == "auth")
                {
                    byte[] byteData = Encoding.UTF8.GetBytes(payload);
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
                HttpWebResponse webResp = (HttpWebResponse)wx.Response;
                if (webResp != null)
                {
                    var stream = webResp.GetResponseStream();
                    if (stream != null)
                    {
                        byte[] byteResponse = new byte[webResp.ContentLength];
                        stream.Read(byteResponse, 0, Convert.ToInt32(webResp.ContentLength));
                        response.message = Encoding.ASCII.GetString(byteResponse);
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
}
