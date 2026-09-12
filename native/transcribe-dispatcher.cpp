#include <process.h>
#include <windows.h>

#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

namespace {

std::wstring executable_directory() {
    std::wstring buffer(32768, L'\0');
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return L".";
    }

    buffer.resize(length);
    const size_t pos = buffer.find_last_of(L"\\/");
    return pos == std::wstring::npos ? L"." : buffer.substr(0, pos);
}

bool force_cuda() {
    const wchar_t * value = _wgetenv(L"SPEAK2TEXT_FORCE_CUDA");
    return value != nullptr && std::wcscmp(value, L"1") == 0;
}

}  // namespace

int wmain(int argc, wchar_t * argv[]) {
    const std::wstring native_exe = executable_directory() + L"\\transcribe-native.exe";

    if (GetFileAttributesW(native_exe.c_str()) == INVALID_FILE_ATTRIBUTES) {
        std::fwprintf(stderr, L"Speak2Text dispatcher: missing %ls\n", native_exe.c_str());
        return 2;
    }

    const bool cuda = force_cuda();
    std::vector<std::wstring> args;
    args.reserve(static_cast<size_t>(argc));
    args.push_back(native_exe);

    for (int i = 1; i < argc; ++i) {
        std::wstring value = argv[i];

        if (cuda && value == L"--backend" && i + 1 < argc) {
            args.push_back(value);
            std::wstring requested = argv[++i];
            if (requested == L"auto") {
                requested = L"cuda";
            }
            args.push_back(requested);
            continue;
        }

        args.push_back(std::move(value));
    }

    std::vector<const wchar_t *> argv_native;
    argv_native.reserve(args.size() + 1);
    for (const auto & value : args) {
        argv_native.push_back(value.c_str());
    }
    argv_native.push_back(nullptr);

    const intptr_t code = _wspawnv(
        _P_WAIT,
        native_exe.c_str(),
        argv_native.data());

    if (code == -1) {
        std::fwprintf(stderr, L"Speak2Text dispatcher: failed to start native engine.\n");
        return 3;
    }

    return static_cast<int>(code);
}
