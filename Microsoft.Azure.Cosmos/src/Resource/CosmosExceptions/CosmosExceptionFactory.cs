﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Resource.CosmosExceptions
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using Microsoft.Azure.Documents;

    internal static class CosmosExceptionFactory
    {
        internal static CosmosException Create(
            DocumentClientException dce,
            CosmosDiagnosticsContext diagnosticsContext)
        {
            Headers headers = new Headers();
            if (dce.Headers != null)
            {
                foreach (string key in dce.Headers)
                {
                    headers.Add(key, dce.Headers[key]);
                }
            }

            HttpStatusCode httpStatusCode;
            if (dce.StatusCode.HasValue)
            {
                httpStatusCode = dce.StatusCode.Value;
            }
            else if (dce.InnerException != null && dce.InnerException is TransportException)
            {
                httpStatusCode = HttpStatusCode.RequestTimeout;
            }
            else
            {
                httpStatusCode = HttpStatusCode.InternalServerError;
            }

            return CosmosExceptionFactory.Create(
                httpStatusCode,
                (int)dce.GetSubStatus(),
                dce.Message,
                new StackTrace(dce),
                dce.ActivityId,
                dce.RequestCharge,
                dce.RetryAfter,
                headers,
                diagnosticsContext,
                dce.InnerException);
        }

        internal static CosmosException Create(
            HttpStatusCode statusCode,
            RequestMessage requestMessage,
            string errorMessage)
        {
            return CosmosExceptionFactory.Create(
                statusCode: statusCode,
                subStatusCode: default,
                message: errorMessage,
                stackTrace: new StackTrace(1),
                activityId: requestMessage?.Headers?.ActivityId,
                requestCharge: 0,
                retryAfter: default,
                headers: requestMessage?.Headers,
                diagnosticsContext: default,
                innerException: default);
        }

        internal static CosmosException Create(
            ResponseMessage responseMessage)
        {
            // If there is no content and there is cosmos exception
            // then use the existing exception
            if (responseMessage.Content == null
                && responseMessage.CosmosException != null)
            {
                return responseMessage.CosmosException;
            }

            // If content was added after the response message
            // creation the exception should be updated.
            string errorMessage = responseMessage.ErrorMessage;
            string contentMessage = GetErrorMessageFromStream(responseMessage.Content);
            if (!string.IsNullOrEmpty(contentMessage))
            {
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = contentMessage;
                }
                else
                {
                    errorMessage = $"Error Message: {errorMessage}; Content {contentMessage};";
                }
            }

            StackTrace stackTrace;
            if (responseMessage.CosmosException != null)
            {
                stackTrace = new StackTrace(responseMessage.CosmosException);
            }
            else
            {
                stackTrace = new StackTrace(1);
            }

            return CosmosExceptionFactory.Create(
                responseMessage.StatusCode,
                (int)responseMessage.Headers.SubStatusCode,
                errorMessage,
                stackTrace,
                responseMessage.Headers.ActivityId,
                responseMessage.Headers.RequestCharge,
                responseMessage.Headers.RetryAfter,
                responseMessage.Headers,
                responseMessage.DiagnosticsContext,
                responseMessage.CosmosException?.InnerException);
        }

        internal static CosmosException Create(
            StoreResponse storeResponse,
            RequestMessage requestMessage)
        {
            if (storeResponse == null)
            {
                throw new ArgumentNullException(nameof(storeResponse));
            }

            if (requestMessage == null)
            {
                throw new ArgumentNullException(nameof(requestMessage));
            }

            string errorMessage = CosmosExceptionFactory.GetErrorMessageFromStream(storeResponse.ResponseBody);
            Headers headers = storeResponse.ToCosmosHeaders();

            return CosmosExceptionFactory.Create(
                storeResponse.StatusCode,
                (int)headers.SubStatusCode,
                errorMessage,
                new StackTrace(-1),
                headers.ActivityId,
                headers.RequestCharge,
                headers.RetryAfter,
                headers,
                requestMessage.DiagnosticsContext,
                null);
        }

        internal static string GetErrorMessageFromStream(
            Stream content)
        {
            using (content)
            {
                if (content != null
               && content.CanRead)
                {
                    try
                    {
                        Error error = Documents.Resource.LoadFrom<Error>(content);
                        if (error != null)
                        {
                            // Error format is not consistent across modes
                            if (!string.IsNullOrEmpty(error.Message))
                            {
                                return error.Message;
                            }
                            else
                            {
                                return error.ToString();
                            }
                        }
                    }
                    catch (Newtonsoft.Json.JsonReaderException)
                    {
                        // Content is not Json
                        content.Position = 0;
                        using (StreamReader streamReader = new StreamReader(content))
                        {
                            return streamReader.ReadToEnd();
                        }
                    }
                }

                return null;
            }
        }

        internal static CosmosException Create(
            HttpStatusCode statusCode,
            int subStatusCode,
            string message,
            StackTrace stackTrace,
            string activityId,
            double requestCharge,
            TimeSpan? retryAfter,
            Headers headers,
            CosmosDiagnosticsContext diagnosticsContext,
            Exception innerException)
        {
            switch (statusCode)
            {
                case HttpStatusCode.NotFound:
                    return new CosmosNotFoundException(
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
                case HttpStatusCode.InternalServerError:
                    return new CosmosInternalServerErrorException(
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
                case HttpStatusCode.BadRequest:
                    return new CosmosBadRequestException(
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
                case HttpStatusCode.RequestTimeout:
                    return new CosmosRequestTimeoutException(
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
                case (HttpStatusCode)429:
                    return new CosmosThrottledException(
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
                default:
                    return new CosmosException(
                        statusCode,
                        message,
                        subStatusCode,
                        stackTrace,
                        activityId,
                        requestCharge,
                        retryAfter,
                        headers,
                        diagnosticsContext,
                        innerException);
            }
        }
    }
}