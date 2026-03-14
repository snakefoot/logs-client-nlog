using System;
using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Layouts;
using NLog.Common;

namespace BetterStack.Logs.NLog
{
    /// <summary>
    /// NLog target for Better Stack Logs. This target does not send all the events individually
    /// to the Better Stack server but it sends them periodically in batches.
    /// </summary>
    [Target("BetterStack.Logs")]
    public sealed class BetterStackLogsTarget : TargetWithLayout
    {
        private readonly BetterStackJsonLayout _jsonLayout = new BetterStackJsonLayout();
        private Drain betterStackDrain = null;

        /// <summary>
        /// Gets the JSON layout configuration used for formatting log entries.
        /// </summary>
        public JsonLayout JsonLayoutt => _jsonLayout;

        /// <inheritdoc cref="BetterStackJsonLayout.Message"/>
        public override Layout Layout
        {
            get => _jsonLayout.Message;
            set => _jsonLayout.Message = value;
        }

        /// <summary>
        /// Gets or sets the Better Stack Logs source token.
        /// </summary>
        public Layout SourceToken { get; set; }

        /// <summary>
        /// The Better Stack Logs endpoint.
        /// </summary>
        public Layout Endpoint { get; set; } = "https://in.logs.betterstack.com";

        /// <summary>
        /// Maximum logs sent to the server in one batch.
        /// </summary>
        public int MaxBatchSize { get; set; } = 1000;

        /// <summary>
        /// The flushing period in milliseconds.
        /// </summary>
        public int FlushPeriodMilliseconds { get; set; } = 250;

        /// <summary>
        /// The number of retries of failing HTTP requests.
        /// </summary>
        public int Retries { get; set; } = 10;

        /// <inheritdoc cref="BetterStackJsonLayout.IncludeEventProperties"/>
        public bool IncludeEventProperties
        {
            get => _jsonLayout.IncludeEventProperties;
            set => _jsonLayout.IncludeEventProperties = value;
        }

        /// <inheritdoc cref="BetterStackJsonLayout.IncludeScopeProperties"/>
        public bool IncludeScopeProperties
        {
            get => _jsonLayout.IncludeScopeProperties;
            set => _jsonLayout.IncludeScopeProperties = value;
        }

        /// <inheritdoc cref="BetterStackJsonLayout.IncludeGdcProperties"/>
        public bool IncludeGlobalDiagnosticContext
        {
            get => _jsonLayout.IncludeGdcProperties;
            set => _jsonLayout.IncludeGdcProperties = value;
        }

        /// <summary>
        /// Capture the file and line of every log-message.
        /// </summary>
        /// <remarks>Enabling this will hurt application performance as NLog will capture StackTrace for each log-message</remarks>
        public bool CaptureSourceLocation
        {
            get => StackTraceUsage != StackTraceUsage.None;
            set => StackTraceUsage = value ? StackTraceUsage.Max : StackTraceUsage.None;
        }

        /// <inheritdoc cref="BetterStackJsonLayout.StackTraceUsage"/>
        public StackTraceUsage StackTraceUsage
        {
            get => _jsonLayout.StackTraceUsage;
            set => _jsonLayout.StackTraceUsage = value;
        }

        /// <inheritdoc cref="BetterStackJsonLayout.Context"/>
        /// <remarks>To replicate <see cref="TargetWithContext.ContextProperties"/></remarks>
        [ArrayParameter(typeof(JsonAttribute), "contextproperty")]
        public IList<JsonAttribute> ContextProperties => _jsonLayout.Context;

        /// <summary>
        /// Initializes a new instance of the BetterStack.Logs.NLog.BetterStackLogsTarget class.
        /// </summary>
        public BetterStackLogsTarget()
        {
            base.Layout = _jsonLayout;
        }

        /// <inheritdoc/>
        protected override void InitializeTarget()
        {
            betterStackDrain?.Stop().Wait();

            var sourceToken = RenderLogEvent(SourceToken, LogEventInfo.CreateNullEvent());
            if (string.IsNullOrEmpty(sourceToken))
                throw new NLogConfigurationException("SourceToken is required for BetterStackLogsTarget.");

            var endpoint = RenderLogEvent(Endpoint, LogEventInfo.CreateNullEvent());
            if (string.IsNullOrEmpty(endpoint))
                throw new NLogConfigurationException("Endpoint is required for BetterStackLogsTarget.");

            var client = new Client(
                sourceToken,
                endpoint: endpoint,
                retries: Retries
            );

            betterStackDrain = new Drain(
                client,
                period: TimeSpan.FromMilliseconds(FlushPeriodMilliseconds),
                maxBatchSize: MaxBatchSize
            );

            base.InitializeTarget();
        }

        /// <inheritdoc/>
        protected override void CloseTarget()
        {
            if (betterStackDrain != null && !betterStackDrain.Stop().Wait(TimeSpan.FromSeconds(15)))
                global::NLog.Common.InternalLogger.Warn("BetterStackLogsTarget: Failed to Stop. Check for network connectivity issues.");
            base.CloseTarget();
        }

        /// <inheritdoc/>
        protected override void Write(LogEventInfo logEvent)
        {
            var payload = RenderLogEvent(_jsonLayout, logEvent);
            if (!string.IsNullOrEmpty(payload))
                betterStackDrain.Enqueue(payload);
        }

        /// <inheritdoc/>
        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            if (betterStackDrain != null)
                betterStackDrain.Flush().ContinueWith(t => asyncContinuation(t.Exception));
            else
                base.FlushAsync(asyncContinuation);
        }
    }
}
