using Newtonsoft.Json;

namespace UspsProcessor
{
    internal sealed class UspsGenericResponse
    {
        public bool isSuccess { get; set; }
        public string? message { get; set; }
        public string? error { get; set; }
    }

    internal sealed class UspsAuthResponse
    {
        [JsonProperty("access_token")]
        public string? access_token { get; set; }

        [JsonProperty("expires_in")]
        public int expires_in { get; set; }

        public string? error { get; set; }
    }

    public sealed class PackageDimensions
    {
        [JsonProperty("lengthIn")]
        public decimal LengthIn { get; set; }

        [JsonProperty("widthIn")]
        public decimal WidthIn { get; set; }

        [JsonProperty("heightIn")]
        public decimal HeightIn { get; set; }
    }
}
