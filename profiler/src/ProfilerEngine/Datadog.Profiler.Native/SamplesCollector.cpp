// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2022 Datadog, Inc.

#include "SamplesCollector.h"

#include "Log.h"
#include "OpSysTools.h"


using namespace std::chrono_literals;


template<typename Clock, typename Duration>
std::ostream &operator<<(std::ostream &stream,
                         const std::chrono::time_point<Clock, Duration> &time_point) {
    const time_t time = Clock::to_time_t(time_point);
    char buffer[32] = {};
    struct tm *tm_info = localtime(&time);
    auto millis = std::chrono::time_point_cast<std::chrono::milliseconds>(time_point);
    strftime(buffer, sizeof(buffer), "%Y-%m-%d %H:%M:%S", tm_info);
    sprintf(buffer + strlen(buffer), ".%03d", millis.time_since_epoch() % 1000);
    return stream << buffer;
}

ProfileTime _time() {
    return std::chrono::time_point_cast<std::chrono::milliseconds>(std::chrono::system_clock::now());
}

ProfileTime truncate(
    ProfileTime &t,
    std::chrono::seconds &period) {
    return t - (t.time_since_epoch() % period);
}


SamplesCollector::SamplesCollector(
    IConfiguration* configuration,
    IThreadsCpuManager* pThreadsCpuManager,
    IExporter* exporter,
    IMetricsSender* metricsSender) :
    _uploadInterval(10s),
    _mustStop{false},
    _pThreadsCpuManager{pThreadsCpuManager},
    _metricsSender{metricsSender},
    _exporter{exporter}
{
}

void SamplesCollector::Register(ISamplesProvider* samplesProvider)
{
    _samplesProviders.push_front(std::make_pair(samplesProvider, 0));
}

bool SamplesCollector::Start()
{
    Log::Info("Starting the samples collector");
    _mustStop = false;
    _workerThread = std::thread(&SamplesCollector::SamplesWork, this);
    OpSysTools::SetNativeThreadName(&_workerThread, WorkerThreadName);
    _exporterThread = std::thread(&SamplesCollector::ExportWork, this);
    OpSysTools::SetNativeThreadName(&_exporterThread, ExporterThreadName);
    return true;
}

bool SamplesCollector::Stop()
{
    if (_mustStop)
    {
        return true;
    }

    _mustStop = true;

    _workerThreadPromise.set_value();
    _workerThread.join();

    _exporterThreadPromise.set_value();
    _exporterThread.join();

    // Export the leftover samples
    CollectSamples();
    auto now = _time();
    auto startTime = truncate(now, _uploadInterval);
    auto endTime = startTime + _uploadInterval;
    Export(startTime, endTime);

    return true;
}

const char* SamplesCollector::GetName()
{
    return _serviceName;
}

void SamplesCollector::SamplesWork()
{
    _pThreadsCpuManager->Map(OpSysTools::GetThreadId(), WorkerThreadName);

    const auto future = _workerThreadPromise.get_future();

    while (future.wait_for(CollectingPeriod) == std::future_status::timeout)
    {
        CollectSamples();
    }
}

void SamplesCollector::ExportWork()
{
    _pThreadsCpuManager->Map(OpSysTools::GetThreadId(), ExporterThreadName);

    const auto future = _exporterThreadPromise.get_future();

    auto now = _time();
    auto startTime = truncate(now, _uploadInterval);
    auto endTime = startTime + _uploadInterval;
    auto sleepTime = endTime - now;

    while (true)
    {
        if (future.wait_for(sleepTime) != std::future_status::timeout)
        {
            break;
        }

        Export(startTime, endTime);

        now = _time();
        startTime = truncate(now, _uploadInterval);
        endTime = startTime + _uploadInterval;
        if (now - startTime > endTime - now) {
            startTime = endTime;
            endTime = startTime + _uploadInterval;
        }
        sleepTime = endTime - now;
    }
}

void SamplesCollector::Export(ProfileTime &startTime, ProfileTime &endTime)
{
//    std::cout << "export " << startTime << " - " << endTime << std::endl;
    bool success = false;

    try
    {
        std::lock_guard lock(_exportLock);

        Log::Debug("Collected samples per provider:");
        for (auto& samplesProvider : _samplesProviders)
        {
            auto name = samplesProvider.first->GetName();
            Log::Debug(name, " : ", samplesProvider.second);
            samplesProvider.second = 0;
        }

        success = _exporter->Export(startTime, endTime);
    }
    catch (std::exception const& ex)
    {
        SendHeartBeatMetric(false);
        Log::Error("An exception occured during export: ", ex.what());
    }

    SendHeartBeatMetric(success);
    _pThreadsCpuManager->LogCpuTimes();
}

void SamplesCollector::CollectSamples()
{
    for (auto& samplesProvider : _samplesProviders)
    {
        try
        {
            std::lock_guard lock(_exportLock);

            auto result = samplesProvider.first->GetSamples();
            samplesProvider.second += result.size();

            for (auto const& sample : result)
            {
                if (!sample.GetCallstack().empty())
                {
                    _exporter->Add(sample);
                }
            }
        }
        catch (std::exception const& ex)
        {
            Log::Error("An exception occured while collecting samples: ", ex.what());
        }
    }
}

void SamplesCollector::SendHeartBeatMetric(bool success)
{
    if (_metricsSender != nullptr)
    {
        _metricsSender->Counter(SuccessfulExportsMetricName, 1, {{"success", success ? "1" : "0"}});
    }
}