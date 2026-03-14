using System;
using System.Collections.Generic;
using System.Text;
using NLog.Config;
using NLog.Layouts;

namespace BetterStack.Logs.NLog
{
    internal class BetterStackJsonLayout : JsonLayout
    {
        /// <summary>
        /// Gets or sets the layout used to format log messages.
        /// </summary>
        /// <remarks>
        /// The default value of the layout is: <code>${message}</code>
        /// </remarks>
        public Layout Message
        {
            get => ResolveJsonAttribute(nameof(Message))?.Layout;
            set
            {
                var attribute = ResolveJsonAttribute(nameof(Message));
                if (attribute != null && value != null)
                    attribute.Layout = value;
            }
        }
        /// <inheritdoc cref="JsonLayout.IncludeEventProperties"/>
        public new bool IncludeEventProperties
        {
            get
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                var propertiesLayout = ResolveJsonAttribute("properties", attribute.Layout as JsonLayout)?.Layout as JsonLayout;
                return propertiesLayout?.IncludeEventProperties ?? false;
            }
            set
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                var propertiesLayout = ResolveJsonAttribute("properties", attribute.Layout as JsonLayout)?.Layout as JsonLayout;
                if (propertiesLayout != null)
                    propertiesLayout.IncludeEventProperties = value;
            }
        }

        /// <inheritdoc cref="JsonLayout.IncludeScopeProperties"/>
        public new bool IncludeScopeProperties
        {
            get => (ResolveJsonAttribute(nameof(Context))?.Layout as JsonLayout)?.IncludeScopeProperties ?? false;
            set
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                if (attribute != null && attribute.Layout is JsonLayout jsonLayout)
                    jsonLayout.IncludeScopeProperties = value;
            }
        }

        /// <inheritdoc cref="JsonLayout.IncludeGdc"/>
        public bool IncludeGdcProperties
        {
            get => (ResolveJsonAttribute(nameof(Context))?.Layout as JsonLayout)?.IncludeGdc ?? false;
            set
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                if (attribute != null && attribute.Layout is JsonLayout jsonLayout)
                    jsonLayout.IncludeGdc = value;
            }
        }

        /// <summary>
        /// Gets the array of attributes for the "properties"-section
        /// </summary>
        [ArrayParameter(typeof(JsonAttribute), "ContextProperty")]
        public IList<JsonAttribute> Context
        {
            get
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                return (attribute?.Layout as JsonLayout)?.Attributes;
            }
        }

        /// <summary>
        /// Control callsite capture of source-file and source-linenumber.
        /// </summary>
        /// <remarks>Enabling this will hurt application performance as NLog will capture StackTrace for each log-message</remarks>
        public StackTraceUsage StackTraceUsage
        {
            get
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                var runtimeLayout = ResolveJsonAttribute("runtime", attribute.Layout as JsonLayout)?.Layout as JsonLayout;
                return runtimeLayout?.Attributes?.Count > 0 ? StackTraceUsage.WithCallSite : StackTraceUsage.None;
            }
            set
            {
                var attribute = ResolveJsonAttribute(nameof(Context));
                var runtimeLayout = ResolveJsonAttribute("runtime", attribute.Layout as JsonLayout)?.Layout as JsonLayout;
                if (value == StackTraceUsage.None && runtimeLayout != null)
                {
                    runtimeLayout.Attributes.Clear();
                }
                else if (value != StackTraceUsage.None && runtimeLayout != null)
                {
                    runtimeLayout.Attributes.Clear();
                    runtimeLayout.Attributes.Add(new JsonAttribute("class", "${callsite:classname=true:methodName=false}"));
                    runtimeLayout.Attributes.Add(new JsonAttribute("member", "${callsite:classname=false:methodName=true}"));
                    if (value.HasFlag(StackTraceUsage.WithFileNameAndLineNumber) || value.HasFlag(StackTraceUsage.WithStackTrace))
                    {
                        runtimeLayout.Attributes.Add(new JsonAttribute("file", "${callsite-filename}"));
                        runtimeLayout.Attributes.Add(new JsonAttribute("line", "${callsite-linenumber}"));
                    }
                }
            }
        }

        public BetterStackJsonLayout()
        {
            Attributes.Add(new JsonAttribute("dt", "\"${date:universalTime=true:format=o}\"", encode: false));
            Attributes.Add(new JsonAttribute("level", "${level}"));
            Attributes.Add(new JsonAttribute("message", "${message}"));
            Attributes.Add(new JsonAttribute("logger", "${logger}"));
            Attributes.Add(new JsonAttribute("exception", "${exception:format=tostring}"));
            var contextLayout = new JsonLayout() { SuppressSpaces = true };
            contextLayout.Attributes.Add(new JsonAttribute("properties", new JsonLayout() { IncludeEventProperties = true, SuppressSpaces = true }, encode: false));
            contextLayout.Attributes.Add(new JsonAttribute("runtime", new JsonLayout() { SuppressSpaces = true }, encode: false));
            Attributes.Add(new JsonAttribute("context", contextLayout, encode: false));
            SuppressSpaces = true;
        }

        protected override void InitializeLayout()
        {
            base.IncludeEventProperties = false;
            base.IncludeScopeProperties = false;
            base.InitializeLayout();
        }

        private JsonAttribute ResolveJsonAttribute(string attributeName, JsonLayout jsonLayout = null)
        {
            var attributes = jsonLayout?.Attributes ?? Attributes;
            for (int i = 0; i < attributes.Count; ++i)
            {
                if (attributeName.Equals(attributes[i].Name, StringComparison.OrdinalIgnoreCase))
                {
                    return attributes[i];
                }
            }
            return null;
        }
    }
}
