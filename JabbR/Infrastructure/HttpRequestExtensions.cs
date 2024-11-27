using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using JabbR.WebApi.Model;
using Microsoft.AspNetCore.Http.Features;

namespace JabbR.Infrastructure
{
    public static class HttpRequestExtensions 
    {
        /// <summary>
        /// Returns a success message for the given data. This is returned to the client using the supplied status code
        /// </summary>
        /// <typeparam name="T">Type of the payload (usually inferred).</typeparam>
        /// <param name="request">Request message that is used to create output message.</param>
        /// <param name="statusCode">Status code to return to the client</param>
        /// <param name="data">API payload</param>
        /// <param name="filenamePrefix">Filename to return to the client, if client requests so.</param>
        /// <returns>
        /// HttpResponseMessage that wraps the given payload
        /// </returns>
        public static HttpResponseMessage CreateJabbrSuccessMessage<T>(this HttpRequestMessage request, HttpStatusCode statusCode, T data, string filenamePrefix)
        {
            var responseMessage = new HttpResponseMessage(statusCode);
            responseMessage.Content = new ObjectContent<T>(data, new System.Net.Http.Formatting.JsonMediaTypeFormatter());
            return AddResponseHeaders(request, responseMessage, filenamePrefix);
        }
        /// <summary>
        /// Returns a success message for the given data. This is returned to the client using the supplied status code
        /// </summary>
        /// <typeparam name="T">Type of the payload (usually inferred).</typeparam>
        /// <param name="data">API payoad</param>
        /// <param name="request">Request message that is used to create output message.</param>
        /// <param name="statusCode">Status code to return to the client</param>
        /// <param name="filenamePrefix">Filename to return to the client, if client requests so.</param>
        /// <returns>HttpResponseMessage that wraps the given payload</returns>
        public static HttpResponseMessage CreateJabbrSuccessMessage<T>(this HttpRequestMessage Request, HttpStatusCode statusCode, T data)
        {
            var responseMessage = new HttpResponseMessage(statusCode);
            responseMessage.Content = new ObjectContent<T>(data, new System.Net.Http.Formatting.JsonMediaTypeFormatter());
            return AddResponseHeaders(Request, responseMessage, null);
        }

        /// <summary>
        /// Returns an error message with the given message. This is returned to the client using the supplied status code
        /// </summary>
        /// <param name="request">Request message that is used to create output message.</param>
        /// <param name="statusCode">Status code to return to the client</param>
        /// <param name="data">Error response that is sent to the client.</param>
        /// <returns>HttpResponseMessage that wraps the given payload</returns>
        public static HttpResponseMessage CreateJabbrErrorMessage(this HttpRequestMessage request, HttpStatusCode statusCode, string message, string filenamePrefix)
        {
            var responseMessage = new HttpResponseMessage(statusCode);
            responseMessage.Content = new ObjectContent<ErrorModel>(
                new ErrorModel { Message = message },
                new System.Net.Http.Formatting.JsonMediaTypeFormatter(),
                "application/json");

            return AddResponseHeaders(request, responseMessage, filenamePrefix);
        }
        /// <summary>
        /// Returns an error message with the given message. This is returned to the client using the supplied status code
        /// </summary>
        /// <param name="request">Request message that is used to create output message.</param>
        /// <param name="statusCode">Status code to return to the client</param>
        /// <param name="data">Error response that is sent to the client.</param>
        /// <returns>HttpResponseMessage that wraps the given payload</returns>
        public static HttpResponseMessage CreateJabbrErrorMessage(this HttpRequestMessage request, HttpStatusCode statusCode, string message)
        {
            var responseMessage = new HttpResponseMessage(statusCode);
            responseMessage.Content = new ObjectContent<ErrorModel>(
                new ErrorModel { Message = message },
                new System.Net.Http.Formatting.JsonMediaTypeFormatter(),
                "application/json");

            return AddResponseHeaders(request, responseMessage, null);
        }

        private static HttpResponseMessage AddResponseHeaders(HttpRequestMessage request, HttpResponseMessage responseMessage, string filenamePrefix)
        {
            return AddDownloadHeader(request, responseMessage, filenamePrefix);
        }
        private static HttpResponseMessage AddDownloadHeader(HttpRequestMessage request, HttpResponseMessage responseMessage, string filenamePrefix)
        {
            var queryString = new QueryStringCollection(request.RequestUri);
            bool download;
            if (queryString.TryGetAndConvert<bool>("download", out download))
            {
                if (download)
                {
                    responseMessage.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = filenamePrefix + ".json" };
                }
            }
            else
            {
                var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
                response.Content = new ObjectContent<ErrorModel>(
                    new ErrorModel { Message = "Value for download was specified but cannot be converted to true or false." },
                    new System.Net.Http.Formatting.JsonMediaTypeFormatter(),
                    "application/json");
                return response;
            }

            return responseMessage;
        }

        /// <summary>
        /// Determines whether the specified request is local. 
        /// This seems like reverse engineering the actual implementation, so it might change in future.
        /// </summary>
        /// <param name="requestMessage">The request message.</param>
        /// <returns>
        ///   <c>true</c> if the specified request message is local; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsLocal(this HttpRequestMessage requestMessage)
        {
            // Try to get the IsLocal value from Options
            if (requestMessage.Options.TryGetValue("MS_IsLocal", out var isLocalObj) && isLocalObj is bool isLocal)
            {
                return isLocal;
            }

            // If not found in Options, try to determine based on the host
            var host = requestMessage.RequestUri?.Host;
            return host != null && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                    host.Equals("127.0.0.1") ||
                                    host.Equals("::1"));
        }


        /// <summary>
        /// Sets IsLocal for the specified HttpRequestMessage
        /// Do not use outside of unit tests
        /// </summary>
        /// <param name="requestMessage">The request message.</param>
        /// <param name="value">New value of isLocal</param>
        public static void SetIsLocal(this HttpRequestMessage requestMessage, bool value)
        {
            // Set the IsLocal value in Options
            requestMessage.Options["MS_IsLocal"] = value;
        }

        /// <summary>
        /// Gets the absolute URI of the current server, even if the app is running behind a load balancer.
        /// Taken from AppHarbour blog and adapted to use request protocol and for use with Web API.
        /// </summary>
        /// <param name="requestMessage">The request message.</param>
        /// <param name="relativeUri">The relative URI.</param>
        /// <returns></returns>
        public static Uri GetAbsoluteUri(this HttpRequestMessage requestMessage, string relativeUri)
        {
            var proto = "http";
            IEnumerable<string> headerValues;

            if (requestMessage.Headers.TryGetValues("X-Forwarded-Proto", out headerValues))
            {
                proto = headerValues.FirstOrDefault();
            }

            var uriBuilder = new UriBuilder
            {
                Host = requestMessage.RequestUri.Host,
                Path = "/",
                Scheme = proto,
            };

            uriBuilder.Port = requestMessage.RequestUri.Port;

            return new Uri(uriBuilder.Uri, relativeUri);
        }
    }
}