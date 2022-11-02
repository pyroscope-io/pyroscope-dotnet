//
// Created by root on 11/2/22.
//

#include "PprofExporter.h"
#include "Log.h"

PprofExporter::PprofExporter(IApplicationStore* applicationStore, std::unique_ptr<PProfExportSink> sink, std::vector<SampleValueType> sampleTypeDefinitions) :
    _applicationStore(applicationStore),
    _sink(std::move(sink)),
    _sampleTypeDefinitions(sampleTypeDefinitions)
{
    signal(SIGPIPE, SIG_IGN);
    Log::Info("PyroscopeExporter::PyroscopeExporter");
}

PProfExportSink::~PProfExportSink()
{
}

void PprofExporter::Add(const Sample& sample)
{
    GetPprofBuilder(sample.GetRuntimeId())
        .AddSample(sample);
}

void PprofExporter::SetEndpoint(const std::string& runtimeId, uint64_t traceId, const std::string& endpoint)
{
    Log::Info("SetEndpoint 2");
}

bool PprofExporter::Export()
{
    Log::Info("PyroscopeExporter::Export");
    std::lock_guard lock(_perAppBuilderLock);
    for (auto& builder : _perAppBuilder)
    {
        Log::Info("   Export ", builder.first);
        auto pprof = builder.second.Build();
        _sink->Export(std::move(pprof)); // todo move out of _perAppBuilderLock
    }
    return true;
}

PprofBuilder& PprofExporter::GetPprofBuilder(std::string_view runtimeId)
{
    std::lock_guard lock(_perAppBuilderLock);
    auto it = _perAppBuilder.find(runtimeId);
    if (it != _perAppBuilder.end())
    {
        return it->second;
    }
    auto res = _perAppBuilder.emplace(runtimeId, _sampleTypeDefinitions);
    return res.first->second;
}
