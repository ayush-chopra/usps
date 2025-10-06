using System.Net;

namespace Usps.V3.Http;

public sealed class UspsApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ErrorCode { get; }
    public string? ResponseBody { get; }

    public UspsApiException(HttpStatusCode statusCode, string message, string? errorCode = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }
}

