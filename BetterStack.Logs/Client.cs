using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BetterStack.Logs
{
    /// <summary>
    /// The Client class is responsible for reliable delivery of logs to the Better Stack servers.
    /// </summary>
    public sealed class Client
    {
        private readonly HttpClient httpClient;
        private readonly int retries;
        private readonly StringBuilder _payloadBuilder = new StringBuilder();
        private readonly System.Net.Http.Headers.MediaTypeHeaderValue _contentTypeJson = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        private readonly char[] _reusableEncodingBuffer = new char[40 * 1024];  // Avoid Large-Object-Heap (LOH)

        public Client(
            string sourceToken,
            string endpoint = "https://in.logs.betterstack.com",
            TimeSpan? timeout = null,
            int retries = 10
        )
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {sourceToken}");
            httpClient.BaseAddress = new Uri(endpoint);
            httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(10);

            this.retries = retries;
        }

        /// <summary>
        /// Sends a collection of logs to the server with several retries
        /// if an error occures.
        /// </summary>
        public async Task Send(List<string> logs)
        {
            var content = serialize(logs);
            logs.Clear();  // Allow garbage collection, while waiting for the request to complete

            for (int i = 0; i < retries; ++i) {
                var success = await sendOnce(content);
                if (success) break;

                await Task.Delay(TimeSpan.FromSeconds(i));
            }
        }

        private async Task<bool> sendOnce(HttpContent content)
        {
            var httpStatusCode = default(HttpStatusCode);
            try
            {
                var response = await httpClient.PostAsync("/", content);
                httpStatusCode = response.StatusCode;
                response.EnsureSuccessStatusCode();  // Throw if not a success code
                return true;
            }
            catch (HttpRequestException ex)
            {
                global::NLog.Common.InternalLogger.Warn(ex, "BetterStackLogsTarget: HTTP request failed with status code {0}", (int)httpStatusCode);

#if NET || NETSTANDARD2_1_OR_GREATER
                if (httpStatusCode == HttpStatusCode.TooManyRequests || httpStatusCode == HttpStatusCode.RequestTimeout || ((int)httpStatusCode >= 500 && httpStatusCode != HttpStatusCode.NetworkAuthenticationRequired))
#else
                if ((int)httpStatusCode == 429 || httpStatusCode == HttpStatusCode.RequestTimeout || ((int)httpStatusCode >= 500 && (int)httpStatusCode != 511))
#endif
                {
                    // TODO retry only 429 + 408 + 5xx (server errors, typically transient)
                }
            }
            catch (OperationCanceledException ex)
            {
                global::NLog.Common.InternalLogger.Warn(ex, "BetterStackLogsTarget: Http Request timed out.");
            }
            catch (Exception ex)
            {
                global::NLog.Common.InternalLogger.Warn(ex, "BetterStackLogsTarget: Http Request failed.");
            }

            return false;
        }

        private HttpContent serialize(List<string> logs) {
            lock (_payloadBuilder)
            {
                try
                {
                    _payloadBuilder.Length = 0;
                    _payloadBuilder.Append('[');
                    foreach (var log in logs)
                    {
                        if (_payloadBuilder.Length > 1)
                            _payloadBuilder.Append(',');
                        _payloadBuilder.Append(log);
                    }
                    _payloadBuilder.Append(']');
                    var content = new ByteArrayContent(EncodePayload(Encoding.UTF8, _payloadBuilder));
                    content.Headers.ContentType = _contentTypeJson;
                    return content;
                }
                finally
                {
                    if (_payloadBuilder.Length > _reusableEncodingBuffer.Length)
                        _payloadBuilder.Remove(0, _payloadBuilder.Length - 1);  // Attempt soft clear that skips Large-Object-Heap (LOH) re-allocation
                    _payloadBuilder.Length = 0;
                }
            }
        }

        byte[] EncodePayload(Encoding encoder, StringBuilder payload)
        {
            lock (_reusableEncodingBuffer)
            {
                var payloadLength = payload.Length;
                if (payloadLength < _reusableEncodingBuffer.Length)
                {
                    payload.CopyTo(0, _reusableEncodingBuffer, 0, payloadLength);
                    return encoder.GetBytes(_reusableEncodingBuffer, 0, payloadLength);
                }

                return encoder.GetBytes(payload.ToString());
            }
        }
    }
}
