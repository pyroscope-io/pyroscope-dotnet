// <copyright file="MetricValue.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

namespace Datadog.Trace.Debugger.Configurations.Models
{
    internal record MetricValue
    {
        public string Expr { get; set; }
    }
}
