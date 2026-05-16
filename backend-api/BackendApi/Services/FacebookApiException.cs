using System.Net;
using Page_API.Models;

namespace Page_API.Services
{
    public sealed class FacebookApiException : Exception
    {
        public HttpStatusCode UpstreamStatusCode { get; }
        public FacebookApiError? FacebookError { get; }
        public string? RawBody { get; }

        public FacebookApiException(
            HttpStatusCode upstreamStatusCode,
            FacebookApiError? facebookError,
            string? rawBody,
            string message) : base(message)
        {
            UpstreamStatusCode = upstreamStatusCode;
            FacebookError = facebookError;
            RawBody = rawBody;
        }
    }
}

