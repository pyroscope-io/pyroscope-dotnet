// <copyright file="Sources.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Datadog.Trace.ClrProfiler;

namespace Datadog.Trace.SourceGenerators.InstrumentationDefinitions
{
    internal static class Sources
    {
        private const string InstrumentationsCollectionName = "Instrumentations";
        private const string DerivedInstrumentationsCollectionName = "DerivedInstrumentations";
        private const string InterfaceInstrumentationsCollectionName = "InterfaceInstrumentations";

        public static string CreateCallTargetDefinitions(IReadOnlyCollection<CallTargetDefinitionSource> definitions)
        {
            void BuildInstrumentationDefinitions(StringBuilder sb, List<CallTargetDefinitionSource> orderedDefinitions, int integrationType, string instrumentationsCollectionName)
            {
                string? integrationName = null;
                var enumType = typeof(InstrumentationCategory);
                var enumNames = Enum.GetNames(enumType);
                var filteredDefs = orderedDefinitions.Where(o => o.IntegrationType == integrationType);

                foreach (var name in enumNames)
                {
                    var defAttri = enumType.GetField(name).GetCustomAttributes(false).OfType<DefinitionsIdAttribute>().Single();
                    var enumDefinitions = filteredDefs.Where(d => d.InstrumentationCategory == (InstrumentationCategory)Enum.Parse(enumType, name));

                    // Template values
                    var integrationTypeString = integrationType switch
                    {
                        0 => "root",
                        1 => "derived",
                        2 => "interface",
                        _ => throw new ArgumentException(nameof(integrationType)),
                    };
                    Func<DefinitionsIdAttribute, string> getDefinitionsId = integrationType switch
                    {
                        0 => (DefinitionsIdAttribute def) => def.DefinitionsId,
                        1 => (DefinitionsIdAttribute def) => def.DerivedDefinitionsId,
                        2 => (DefinitionsIdAttribute def) => def.InterfaceDefinitionsId,
                        _ => throw new ArgumentException(nameof(integrationType)),
                    };

                    sb.Append($@"
                // {integrationTypeString} types for InstrumentationCategory {name}
                payload = new Payload
                {{
                    DefinitionsId = ""{getDefinitionsId(defAttri)}"",
                    Definitions = new NativeCallTargetDefinition[]
                    {{");
                    foreach (var definition in enumDefinitions)
                    {
                        integrationName = WriteDefinition(definition, integrationName, sb);
                    }

                    sb.Append($@"
                    }}
                }};
                {instrumentationsCollectionName}.Add(InstrumentationCategory.{name}, payload);
                {instrumentationsCollectionName}Natives = {instrumentationsCollectionName}Natives.Concat(payload.Definitions);
                ");
                }
            }

            var sb = new StringBuilder();
            sb.Append($@"// <auto-generated/>
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Datadog.Trace.ClrProfiler
{{
    internal static partial class InstrumentationDefinitions
    {{
        private static IDictionary<InstrumentationCategory, Payload> {InstrumentationsCollectionName} = new Dictionary<InstrumentationCategory, Payload>();
        private static IDictionary<InstrumentationCategory, Payload> {DerivedInstrumentationsCollectionName} = new Dictionary<InstrumentationCategory, Payload>();
        private static IDictionary<InstrumentationCategory, Payload> {InterfaceInstrumentationsCollectionName} = new Dictionary<InstrumentationCategory, Payload>();
        private static IEnumerable<NativeCallTargetDefinition> {InstrumentationsCollectionName}Natives = new List<NativeCallTargetDefinition>();
        private static IEnumerable<NativeCallTargetDefinition> {DerivedInstrumentationsCollectionName}Natives = new List<NativeCallTargetDefinition>();
        private static IEnumerable<NativeCallTargetDefinition> {InterfaceInstrumentationsCollectionName}Natives = new List<NativeCallTargetDefinition>();

        static InstrumentationDefinitions()
        {{
            Payload payload = default;");
            var orderedDefinitions = definitions
                                    .OrderBy(static x => x.IntegrationName)
                                    .ThenBy(static x => x.AssemblyName)
                                    .ThenBy(static x => x.TargetTypeName)
                                    .ThenBy(static x => x.TargetMethodName)
                                    .ToList();

            string? integrationName = null;
            var enumType = typeof(InstrumentationCategory);
            var enumNames = Enum.GetNames(enumType);

            BuildInstrumentationDefinitions(sb, orderedDefinitions, 0, InstrumentationsCollectionName);
            BuildInstrumentationDefinitions(sb, orderedDefinitions, 1, DerivedInstrumentationsCollectionName);
            BuildInstrumentationDefinitions(sb, orderedDefinitions, 2, InterfaceInstrumentationsCollectionName);

            sb.Append(@"
        }

        private static Payload GetDefinitionsArray(InstrumentationCategory instrumentationCategory = InstrumentationCategory.Tracing)
            => Instrumentations[instrumentationCategory];

        private static Payload GetDerivedDefinitionsArray(InstrumentationCategory instrumentationCategory = InstrumentationCategory.Tracing)
            => DerivedInstrumentations[instrumentationCategory];

        private static Payload GetInterfaceDefinitionsArray(InstrumentationCategory instrumentationCategory = InstrumentationCategory.Tracing)
            => InterfaceInstrumentations[instrumentationCategory];

        internal static Datadog.Trace.Configuration.IntegrationId? GetIntegrationId(
            string? integrationTypeName, System.Type targetType)
        {
            return integrationTypeName switch
            {
                // integrations with a single IntegrationId per implementation type
                ");
            integrationName = null;
            foreach (var definition in orderedDefinitions)
            {
                if (definition.IsAdoNetIntegration)
                {
                    // Only "normal" integrations
                    continue;
                }

                // This assumes that each type is only associated with a single integration name
                // will cause compilation failures if that's not the case (so "safe" in that sense)
                if (definition.IntegrationName == integrationName)
                {
                    sb.Append(@"or ");
                }
                else if (definition.IntegrationName != integrationName)
                {
                    if (integrationName is not null)
                    {
                        // write the previous result
                        // Assumes that IntegrationName is a valid IntegrationId
                        // (Will cause compilation failures if not (so "safe")
                        sb.Append(@"=> Datadog.Trace.Configuration.IntegrationId.")
                          .Append(integrationName)
                          .Append(@",
                ");
                    }

                    integrationName = definition.IntegrationName;
                }

                sb.Append('"')
                  .Append(definition.InstrumentationTypeName)
                  .Append(@"""
                    ");
            }

            if (integrationName is not null)
            {
                // write the last one
                sb.Append(@"=> Datadog.Trace.Configuration.IntegrationId.")
                  .Append(integrationName)
                  .Append(',')
                  .AppendLine();
            }

            sb.Append(@"
                // adonet integrations
                ");

            bool doneFirst = false;
            foreach (var definition in orderedDefinitions)
            {
                if (!definition.IsAdoNetIntegration)
                {
                    // Only "adonet" integrations
                    continue;
                }

                if (doneFirst)
                {
                    sb.Append("or ");
                }

                doneFirst = true;
                sb.Append('"')
                  .Append(definition.InstrumentationTypeName)
                  .Append(@"""
                    ");
            }

            if (doneFirst)
            {
                sb.Append(
                    @"=> GetAdoNetIntegrationId(
                        integrationTypeName: integrationTypeName,
                        targetTypeName: targetType.FullName,
                        assemblyName: targetType.Assembly.GetName().Name),
                ");
            }

            sb.Append(
                @"_ => null,
            };
        }

        public static Datadog.Trace.Configuration.IntegrationId? GetAdoNetIntegrationId(
            string? integrationTypeName, string? targetTypeName, string? assemblyName)
        {
            return new System.Collections.Generic.KeyValuePair<string?, string?>(assemblyName, targetTypeName) switch
            {
                ");

            integrationName = null;
            foreach (var definition in orderedDefinitions)
            {
                if (!definition.IsAdoNetIntegration || definition.IntegrationType != 0)
                {
                    // only non-derived "adonet" integrations
                    continue;
                }

                if (definition.IntegrationName == integrationName)
                {
                    sb.Append(@"or ");
                }
                else if (definition.IntegrationName != integrationName)
                {
                    if (integrationName is not null)
                    {
                        // write the previous result
                        // Assumes that IntegrationName is a valid IntegrationId
                        // (Will cause compilation failures if not (so "safe")
                        sb.Append(@"=> Datadog.Trace.Configuration.IntegrationId.")
                          .Append(integrationName)
                          .Append(@",
                    ");
                    }

                    integrationName = definition.IntegrationName;
                }

                sb.Append(@"{ Key: """)
                  .Append(definition.AssemblyName)
                  .Append(@""", Value: """)
                  .Append(definition.TargetTypeName)
                  .Append(@""" }
                    ");
            }

            if (integrationName is not null)
            {
                // write the last one
                sb.Append(@"=> Datadog.Trace.Configuration.IntegrationId.")
                  .Append(integrationName)
                  .Append(@",

                ");
            }

            sb.Append(@"// derived attribute, assume ADO.NET
                _ => Datadog.Trace.Configuration.IntegrationId.AdoNet,
            };
        }
    }
}
");

            return sb.ToString();
        }

        private static string WriteDefinition(CallTargetDefinitionSource definition, string? integrationName, StringBuilder sb)
        {
            if (definition.IntegrationName != integrationName)
            {
                if (integrationName is not null)
                {
                    sb.AppendLine();
                }

                integrationName = definition.IntegrationName;
                sb.Append(
                    $@"
                // {integrationName}");
            }

            sb.Append(
                   @"
               new (""")
              .Append(definition.AssemblyName)
              .Append(@""", """)
              .Append(definition.TargetTypeName)
              .Append(@""", """)
              .Append(definition.TargetMethodName)
              .Append(@""",  new[] { """)
              .Append(definition.TargetReturnType)
              .Append(@"""");

            if (definition.TargetParameterTypes is { Length: > 0 } types)
            {
                foreach (var parameterType in types)
                {
                    sb.Append(@", """)
                      .Append(parameterType)
                      .Append('"');
                }
            }

            var min = definition.MinimumVersion;
            var max = definition.MaximumVersion;
            sb.Append(" }, ")
              .Append(min.Major)
              .Append(", ")
              .Append(min.Minor)
              .Append(", ")
              .Append(min.Patch)
              .Append(", ")
              .Append(max.Major)
              .Append(", ")
              .Append(max.Minor)
              .Append(", ")
              .Append(max.Patch);

            sb.Append(@", assemblyFullName, """)
              .Append(definition.InstrumentationTypeName)
              .Append(@"""),");
            return integrationName;
        }
    }
}
