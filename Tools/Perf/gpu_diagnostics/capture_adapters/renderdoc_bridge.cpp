// Консольный экспорт официального RenderDoc SDK. Replay-время не является live GPU-временем.
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include "renderdoc_replay.h"

// Обязательная метка SDK: собственный replay-процесс не является capture target.
REPLAY_PROGRAM_MARKER()

// SDK поставляет PipeState в .inl; его числовые string helpers не экспортируются DLL.
template <typename T> rdcstr DoStringise(const T &value)
{
    std::ostringstream text;
    if constexpr (std::is_enum_v<T>) text << static_cast<uint32_t>(value);
    else text << value;
    return text.str().c_str();
}
template <> rdcstr DoStringise(const ResultCode &value)
{
    std::string text = "ResultCode(" + std::to_string(static_cast<uint32_t>(value)) + ")";
    return text.c_str();
}
#include "pipestate.inl"

namespace fs = std::filesystem;

static std::string Utf8(const wchar_t *text)
{
    int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text, -1, nullptr, 0, nullptr, nullptr);
    if (bytes == 0) throw std::runtime_error("Invalid Unicode argument");
    std::string value(bytes, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text, -1, value.data(), bytes, nullptr, nullptr);
    value.pop_back();
    return value;
}

static std::string Json(const std::string &text)
{
    std::ostringstream result;
    result << '"';
    for (unsigned char c : text)
    {
        if (c == '"' || c == '\\') result << '\\' << c;
        else if (c < 32) result << "\\u" << std::hex << std::setw(4) << std::setfill('0') << unsigned(c) << std::dec;
        else result << c;
    }
    result << '"';
    return result.str();
}

static std::string Json(const rdcstr &text) { return Json(std::string(text.c_str(), text.size())); }
static std::string Json(const char *text) { return Json(std::string(text)); }

struct ReplayScope
{
    explicit ReplayScope(bool enumerate = true)
    {
        GlobalEnvironment environment;
        environment.enumerateGPUs = enumerate;
        RENDERDOC_InitialiseReplay(environment, {});
    }
    ~ReplayScope() { RENDERDOC_ShutdownReplay(); }
};

template <typename T> struct Handle
{
    T *value;
    explicit Handle(T *p) : value(p) {}
    ~Handle() { if (value) value->Shutdown(); }
    Handle(const Handle &) = delete;
    Handle &operator=(const Handle &) = delete;
};

static void Receipt(const fs::path &path, const std::string &json)
{
    fs::path partial = path;
    partial += L".partial";
    if (fs::exists(path) || fs::exists(partial)) throw std::runtime_error("Receipt already exists; unknown outcomes are not replayed");
    if (!path.parent_path().empty()) fs::create_directories(path.parent_path());
    std::ofstream stream(partial);
    stream.exceptions(std::ios::badbit | std::ios::failbit);
    stream << json << '\n';
    stream.close();
    fs::rename(partial, path);
}

static uint32_t Number(const wchar_t *argument)
{
    std::wstring text(argument);
    size_t used = 0;
    auto value = std::stoull(text, &used);
    if (used != text.size() || value == 0 || value > UINT32_MAX) throw std::runtime_error("Expected positive uint32 argument");
    return uint32_t(value);
}

// Только actuator: родительский Run-E2E владеет процессами, портами и уборкой.
static void Launch(wchar_t **argv)
{
    fs::path receipt(argv[6]);
    fs::path partial = receipt; partial += L".partial";
    if (fs::exists(receipt) || fs::exists(partial)) throw std::runtime_error("Launch receipt already exists or is partial");
    ReplayScope global(false);
    auto result = RENDERDOC_ExecuteAndInject(Utf8(argv[2]).c_str(), Utf8(argv[3]).c_str(), Utf8(argv[4]).c_str(),
                                            {}, Utf8(argv[5]).c_str(), CaptureOptions(), false);
    if (!result.result.OK() || !result.ident) throw std::runtime_error(result.result.Message().c_str());
    HANDLE process = nullptr;
    {
        Handle<ITargetControl> control(RENDERDOC_CreateTargetControl("", result.ident, "VRBattlegroundsLaunch", false));
        if (!control.value || !control.value->Connected()) throw std::runtime_error("No target control after launch; runner must clean its child tree");
        uint32_t pid = control.value->GetPID();
        process = OpenProcess(SYNCHRONIZE, FALSE, pid);
        if (!process) throw std::runtime_error("Cannot retain launched process handle; runner must clean its child tree");
        try
        {
            Receipt(receipt, "{\"schemaVersion\":1,\"status\":\"launched\",\"backendVersion\":\"1.46\",\"playerPid\":" +
                    std::to_string(pid) + ",\"targetControlIdent\":" + std::to_string(result.ident) + "}");
        }
        catch (...) { CloseHandle(process); throw; }
    }
    // Helper остаётся живым родителем до выхода Player; runner знает оба PID.
    WaitForSingleObject(process, INFINITE);
    CloseHandle(process);
}

static void Capture(wchar_t **argv)
{
    uint32_t ident = Number(argv[2]), expectedPid = Number(argv[3]), timeout = Number(argv[5]);
    if (timeout > 300) throw std::runtime_error("Capture timeout exceeds 300 seconds");
    fs::path receiptPath(argv[4]), partial = receiptPath; partial += L".partial";
    if (fs::exists(receiptPath) || fs::exists(partial)) throw std::runtime_error("Capture receipt already exists or is partial");
    ReplayScope global(false);
    Handle<ITargetControl> control(RENDERDOC_CreateTargetControl("", ident, "VRBattlegroundsCapture", false));
    if (!control.value || !control.value->Connected() || control.value->GetPID() != expectedPid)
        throw std::runtime_error("Target control unavailable/busy or PID mismatch");
    LARGE_INTEGER started, ended, frequency;
    QueryPerformanceCounter(&started); QueryPerformanceFrequency(&frequency);
    control.value->TriggerCapture(1);
    while (control.value->Connected())
    {
        QueryPerformanceCounter(&ended);
        if (double(ended.QuadPart - started.QuadPart) / frequency.QuadPart > timeout)
            throw std::runtime_error("Capture timed out; inspect durable state before repeating");
        auto message = control.value->ReceiveMessage(nullptr);
        if (message.type != TargetControlMessageType::NewCapture) continue;
        const auto &capture = message.newCapture;
        if (!capture.local) throw std::runtime_error("Only local Windows captures supported");
        QueryPerformanceCounter(&ended);
        std::ostringstream receipt;
        receipt << "{\"schemaVersion\":1,\"status\":\"captured\",\"playerPid\":" << expectedPid
            << ",\"targetControlIdent\":" << ident << ",\"captureId\":" << capture.captureId
            << ",\"frameNumber\":" << capture.frameNumber << ",\"path\":" << Json(capture.path)
            << ",\"api\":" << Json(capture.api) << ",\"byteSize\":" << capture.byteSize
            << ",\"sdkTimestampRaw\":" << capture.timestamp << ",\"requestQpcTicks\":" << started.QuadPart
            << ",\"completionQpcTicks\":" << ended.QuadPart << ",\"qpcFrequency\":" << frequency.QuadPart
            << ",\"gpuSourceIntervalKnown\":false}";
        Receipt(fs::path(argv[4]), receipt.str());
        return;
    }
    throw std::runtime_error("Target disconnected before capture receipt");
}

class Exporter
{
    IReplayController &replay;
    std::map<ResourceId, size_t> ids;
    std::map<uint32_t, double> seconds;
    std::vector<std::pair<uint32_t, bool>> workEvents;
    size_t unresolved = 0, actionCount = 0;

    std::string Id(ResourceId resource)
    {
        if (resource == ResourceId::Null()) return "null";
        auto found = ids.find(resource);
        if (found == ids.end()) { unresolved++; return "null"; }
        // ID локален для одного capture; равенство проверяет сам SDK, не имя ресурса.
        return Json("resource-" + std::to_string(found->second));
    }

    void Actions(std::ostream &out, const rdcarray<ActionDescription> &rows, uint32_t parent)
    {
        for (const auto &row : rows)
        {
            actionCount++;
            bool work = (uint32_t(row.flags) & (uint32_t(ActionFlags::Drawcall) | uint32_t(ActionFlags::Dispatch))) != 0;
            bool dispatch = (uint32_t(row.flags) & uint32_t(ActionFlags::Dispatch)) != 0;
            bool indexed = (uint32_t(row.flags) & uint32_t(ActionFlags::Indexed)) != 0;
            if (work) workEvents.emplace_back(row.eventId, dispatch);
            out << "{\"eventId\":" << row.eventId << ",\"actionId\":" << row.actionId
                << ",\"parentEventId\":" << (parent ? std::to_string(parent) : "null")
                << ",\"customName\":" << Json(row.customName) << ",\"flags\":" << uint32_t(row.flags)
                << ",\"work\":" << (work ? "true" : "false") << ",\"elementCount\":" << row.numIndices
                << ",\"indexed\":" << (indexed ? "true" : "false")
                << ",\"dispatch\":" << (dispatch ? "true" : "false")
                << ",\"copySource\":" << Id(row.copySource) << ",\"copyDestination\":" << Id(row.copyDestination)
                << ",\"depthOut\":" << Id(row.depthOut)
                << ",\"instanceCount\":" << row.numInstances << ",\"indexOffset\":" << row.indexOffset
                << ",\"vertexOffset\":" << row.vertexOffset << ",\"instanceOffset\":" << row.instanceOffset
                << ",\"drawIndex\":" << row.drawIndex << ",\"apiEvents\":[";
            bool comma = false;
            for (const auto &event : row.events)
            {
                if (comma) out << ',';
                comma = true;
                out << "{\"eventId\":" << event.eventId << ",\"chunkIndex\":" << event.chunkIndex << '}';
            }
            out << "],\"gpuReplaySeconds\":";
            auto timing = seconds.find(row.eventId);
            bool known = timing != seconds.end() && std::isfinite(timing->second) && timing->second >= 0;
            if (known) out << std::setprecision(17) << timing->second;
            else out << "null";
            out << ",\"timingUnavailableReason\":" << (known ? "null" : Json("Counter missing or failed; not zero"))
                << ",\"sceneJoin\":\"unresolved\"}\n";
            Actions(out, row.children, row.eventId);
        }
    }

    void Descriptors(std::ostream &out, const rdcarray<UsedDescriptor> &rows,
                     const ShaderReflection *reflection, bool writable)
    {
        bool comma = false;
        for (const auto &row : rows)
        {
            if (comma) out << ',';
            comma = true;
            const auto &access = row.access;
            const auto &descriptor = row.descriptor;
            out << "{\"resourceId\":" << Id(descriptor.resource) << ",\"reflectionIndex\":" << access.index
                << ",\"arrayElement\":" << access.arrayElement << ",\"staticallyUnused\":" << (access.staticallyUnused ? "true" : "false")
                << ",\"byteOffset\":" << descriptor.byteOffset << ",\"byteSize\":" << descriptor.byteSize
                << ",\"firstMip\":" << uint32_t(descriptor.firstMip) << ",\"numMips\":" << uint32_t(descriptor.numMips)
                << ",\"firstSlice\":" << descriptor.firstSlice << ",\"numSlices\":" << descriptor.numSlices
                << ",\"shaderProperty\":";
            if (reflection)
            {
                const auto &properties = writable ? reflection->readWriteResources : reflection->readOnlyResources;
                if (access.index < properties.size()) out << Json(properties[access.index].name);
                else out << "null";
            }
            else out << "null";
            out << '}';
        }
    }

    void Bindings(std::ostream &out)
    {
        for (const auto &work : workEvents)
        {
            uint32_t event = work.first;
            bool dispatch = work.second;
            replay.SetFrameEvent(event, true);
            const auto &pipeline = replay.GetPipelineState();
            auto index = pipeline.GetIBuffer();
            out << "{\"eventId\":" << event << ",\"pipeline\":" << Json(dispatch ? "compute" : "graphics")
                << ",\"topologyCode\":" << uint32_t(pipeline.GetPrimitiveTopology())
                << ",\"indexBuffer\":";
            if (dispatch) out << "null";
            else out << "{\"resourceId\":" << Id(index.resourceId)
                << ",\"byteOffset\":" << index.byteOffset << ",\"byteStride\":" << index.byteStride
                << '}';
            out << ",\"vertexBuffers\":[";
            bool comma = false;
            if (!dispatch) for (const auto &buffer : pipeline.GetVBuffers())
            {
                if (comma) out << ',';
                comma = true;
                out << "{\"resourceId\":" << Id(buffer.resourceId) << ",\"byteOffset\":" << buffer.byteOffset
                    << ",\"byteStride\":" << buffer.byteStride << '}';
            }
            out << "],\"stages\":[";
            comma = false;
            for (ShaderStage stage : {ShaderStage::Vertex, ShaderStage::Hull, ShaderStage::Domain,
                                     ShaderStage::Geometry, ShaderStage::Pixel, ShaderStage::Compute})
            {
                // D3D11 сохраняет неактивный pipeline state; не приписываем его текущей команде.
                if ((stage == ShaderStage::Compute) != dispatch) continue;
                ResourceId shader = pipeline.GetShader(stage);
                if (shader == ResourceId::Null()) continue;
                const ShaderReflection *reflection = pipeline.GetShaderReflection(stage);
                if (comma) out << ',';
                comma = true;
                out << "{\"stageCode\":" << uint32_t(stage) << ",\"shaderId\":" << Id(shader) << ",\"readOnlyResources\":[";
                bool resourceComma = false;
                for (const auto &binding : pipeline.GetReadOnlyResources(stage, true))
                {
                    if (resourceComma) out << ',';
                    resourceComma = true;
                    out << Id(binding.descriptor.resource);
                }
                out << "],\"readWriteResources\":[";
                resourceComma = false;
                for (const auto &binding : pipeline.GetReadWriteResources(stage, true))
                {
                    if (resourceComma) out << ',';
                    resourceComma = true;
                    out << Id(binding.descriptor.resource);
                }
                out << "],\"entryPoint\":" << (reflection ? Json(reflection->entryPoint) : "null")
                    << ",\"readOnlyBindings\":[";
                Descriptors(out, pipeline.GetReadOnlyResources(stage, true), reflection, false);
                out << "],\"readWriteBindings\":[";
                Descriptors(out, pipeline.GetReadWriteResources(stage, true), reflection, true);
                out << "]}";
            }
            out << "],\"outputTargets\":[";
            comma = false;
            if (!dispatch) for (const auto &target : pipeline.GetOutputTargets())
            {
                if (comma) out << ',';
                comma = true;
                out << Id(target.resource);
            }
            out << "],\"depthTarget\":" << (dispatch ? "null" : Id(pipeline.GetDepthTarget().resource)) << "}\n";
        }
    }

public:
    explicit Exporter(IReplayController &controller) : replay(controller) {}

    void Write(const fs::path &out, const std::string &capturePath, const std::string &logPath,
               const rdcstr &recordedMachine, const rdcstr &driverName)
    {
        if (replay.GetAPIProperties().pipelineType != GraphicsAPI::D3D11)
            throw std::runtime_error("This bridge currently supports D3D11 only");
        if (fs::exists(out)) throw std::runtime_error("Output directory already exists");
        fs::create_directories(out);
        size_t ordinal = 0;
        for (const auto &resource : replay.GetResources()) ids.emplace(resource.resourceId, ++ordinal);
        auto counters = replay.EnumerateCounters();
        bool duration = std::find(counters.begin(), counters.end(), GPUCounter::EventGPUDuration) != counters.end();
        if (duration)
        {
            auto description = replay.DescribeCounter(GPUCounter::EventGPUDuration);
            if (description.resultType != CompType::Float || description.resultByteWidth != 8 || description.unit != CounterUnit::Seconds)
                throw std::runtime_error("Unsupported EventGPUDuration counter representation");
            for (const auto &value : replay.FetchCounters({GPUCounter::EventGPUDuration})) seconds[value.eventId] = value.value.d;
        }
        std::ofstream resources(out / "resources.jsonl"), actions(out / "actions.jsonl"), bindings(out / "bindings.jsonl");
        resources.exceptions(std::ios::badbit | std::ios::failbit);
        actions.exceptions(std::ios::badbit | std::ios::failbit);
        bindings.exceptions(std::ios::badbit | std::ios::failbit);
        std::map<ResourceId, std::string> descriptors;
        for (const auto &texture : replay.GetTextures())
        {
            std::ostringstream data;
            data << "{\"kind\":\"texture\",\"width\":" << texture.width << ",\"height\":" << texture.height
                << ",\"depth\":" << texture.depth << ",\"dimension\":" << texture.dimension
                << ",\"typeCode\":" << uint32_t(texture.type) << ",\"format\":" << Json(texture.format.Name())
                << ",\"mips\":" << texture.mips << ",\"arraySize\":" << texture.arraysize
                << ",\"msaaSamples\":" << texture.msSamp << ",\"estimatedByteSize\":" << texture.byteSize
                << ",\"creationFlags\":" << uint32_t(texture.creationFlags) << '}';
            descriptors.emplace(texture.resourceId, data.str());
        }
        for (const auto &buffer : replay.GetBuffers())
        {
            std::ostringstream data;
            data << "{\"kind\":\"buffer\",\"byteLength\":" << buffer.length
                << ",\"creationFlags\":" << uint32_t(buffer.creationFlags) << '}';
            descriptors.emplace(buffer.resourceId, data.str());
        }
        for (const auto &resource : replay.GetResources())
        {
            resources << "{\"id\":" << Id(resource.resourceId) << ",\"name\":" << Json(resource.name)
                << ",\"typeCode\":" << uint32_t(resource.type) << ",\"parentResources\":[";
            bool comma = false;
            for (auto parent : resource.parentResources) { if (comma) resources << ','; comma = true; resources << Id(parent); }
            auto description = descriptors.find(resource.resourceId);
            resources << "],\"descriptor\":" << (description == descriptors.end() ? "null" : description->second)
                << ",\"sceneJoin\":\"unresolved\"}\n";
        }
        Actions(actions, replay.GetRootActions(), 0);
        Bindings(bindings);
        resources.close(); actions.close(); bindings.close();
        const auto properties = replay.GetAPIProperties();
        std::vector<std::string> selectedDeviceLines;
        std::ifstream log(fs::u8path(logPath));
        for (std::string line; std::getline(log, line); )
            if (line.find("Running replay on ") != std::string::npos) selectedDeviceLines.push_back(line);
        log.close();
        // SDK удаляет успешный diagnostic log при shutdown; сохраняем фактическое свидетельство.
        fs::path retainedLog = out / "replay.log";
        fs::copy_file(fs::u8path(logPath), retainedLog);
        // Manifest — последний маркер полноты. Родительские/дочерние времена не суммируются.
        std::ofstream manifest(out / "capture.json");
        manifest.exceptions(std::ios::badbit | std::ios::failbit);
        manifest << "{\"schemaVersion\":1,\"kind\":\"vrb.renderdoc-replay\",\"complete\":true,\"backendVersion\":"
            << Json(RENDERDOC_GetVersionString()) << ",\"sourceCapture\":" << Json(capturePath)
            << ",\"timingDomain\":\"renderdoc-replay\",\"unit\":\"seconds\",\"apiCode\":" << uint32_t(replay.GetAPIProperties().pipelineType)
            << ",\"durationCounterAvailable\":" << (duration ? "true" : "false")
            << ",\"captureMetadata\":{\"driverName\":" << Json(driverName)
            << ",\"recordedMachineIdent\":" << Json(recordedMachine) << '}'
            << ",\"replayApiProperties\":{\"vendorCode\":" << uint32_t(properties.vendor)
            << ",\"localRendererCode\":" << uint32_t(properties.localRenderer)
            << ",\"remoteReplay\":" << (properties.remoteReplay ? "true" : "false")
            << ",\"degraded\":" << (properties.degraded ? "true" : "false") << '}'
            << ",\"selectedReplayGpuEvidence\":{\"source\":\"renderdoc-diagnostic-log\",\"path\":" << Json(Utf8(retainedLog.c_str()))
            << ",\"gpuName\":null,\"driverVersion\":null,\"rawLogLines\":[";
        bool comma = false;
        for (const auto &line : selectedDeviceLines) { if (comma) manifest << ','; comma = true; manifest << Json(line); }
        manifest << "],\"identityStatus\":" << Json(selectedDeviceLines.size() == 1 ? "single-log-record; unparsed" : "unresolved") << '}'
            << ",\"actionCount\":" << actionCount << ",\"workEventCount\":" << workEvents.size()
            << ",\"resourceCount\":" << ids.size() << ",\"unresolvedResourceReferenceCount\":" << unresolved
            << ",\"resourceIdentity\":\"capture-local SDK ResourceId equality, never Unity InstanceID\""
            << ",\"limitations\":[\"Replay timings are not live GPU timings\",\"Never sum parent/child or overlapping events\",\"Scene/object/mesh/light joins unresolved\",\"Capture overhead not measured\",\"Bound shader descriptors do not prove physical GPU reads; texture bytes are estimates, not resident VRAM\"]}\n";
    }
};

int wmain(int argc, wchar_t **argv)
{
    try
    {
        if (std::string(RENDERDOC_GetVersionString()) != "1.46") throw std::runtime_error("SDK/backend version mismatch");
        if (argc == 2 && std::wstring(argv[1]) == L"version")
        {
            std::cout << "{\"backendVersion\":" << Json(RENDERDOC_GetVersionString()) << ",\"ui\":false}\n";
            return 0;
        }
        if (argc == 7 && std::wstring(argv[1]) == L"launch") { Launch(argv); return 0; }
        if (argc == 6 && std::wstring(argv[1]) == L"capture") { Capture(argv); return 0; }
        if (argc != 4 || std::wstring(argv[1]) != L"export")
            throw std::runtime_error("Usage: version | export <capture.rdc> <new-directory> | launch <exe> <cwd> <cmdline> <capture-prefix> <receipt> | capture <ident> <pid> <receipt> <timeout-seconds>");
        fs::path output(argv[3]), logPath = output; logPath += L".renderdoc.log";
        if (fs::exists(output) || fs::exists(logPath)) throw std::runtime_error("Output or replay log already exists");
        fs::create_directories(output.parent_path());
        RENDERDOC_SetDebugLogFile(Utf8(logPath.c_str()).c_str());
        ReplayScope global(false);
        Handle<ICaptureFile> capture(RENDERDOC_OpenCaptureFile());
        if (!capture.value) throw std::runtime_error("OpenCaptureFile returned null");
        std::string input = Utf8(argv[2]);
        auto opened = capture.value->OpenFile(input.c_str(), "", nullptr);
        if (!opened.OK()) throw std::runtime_error(opened.Message().c_str());
        auto replay = capture.value->OpenCapture(ReplayOptions(), nullptr);
        if (!replay.first.OK() || !replay.second) throw std::runtime_error(replay.first.Message().c_str());
        Handle<IReplayController> controller(replay.second);
        Exporter(*controller.value).Write(output, input, Utf8(logPath.c_str()),
                                          capture.value->RecordedMachineIdent(), capture.value->DriverName());
        std::cout << "{\"status\":\"exported\",\"timingDomain\":\"renderdoc-replay\"}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cout << "{\"status\":\"failed\",\"reason\":" << Json(error.what()) << "}\n";
        return 2;
    }
}
