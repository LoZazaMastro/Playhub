/* Copyright 2026 Playhub. MIT license; see LICENSE in this directory. */
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <wchar.h>

static wchar_t command[32768];
static size_t used;
static int append(wchar_t value) {
    if (used >= 32766) return 0;
    command[used++] = value;
    command[used] = 0;
    return 1;
}
static int quote(const wchar_t *value) {
    if (!append(L'"')) return 0;
    while (*value) {
        size_t slashes = 0;
        while (*value == L'\\') { slashes++; value++; }
        size_t count = (*value == L'"' || !*value) ? slashes * 2 : slashes;
        while (count--) if (!append(L'\\')) return 0;
        if (*value == L'"' && !append(L'\\')) return 0;
        if (*value && !append(*value++)) return 0;
    }
    return append(L'"');
}
int wmain(int argc, wchar_t **argv) {
    wchar_t root[32768], python[32768], script[32768];
    DWORD length = GetModuleFileNameW(NULL, root, 32768);
    if (!length || length >= 32768) return ERROR_BAD_PATHNAME;
    wchar_t *slash = wcsrchr(root, L'\\');
    if (!slash) return ERROR_BAD_PATHNAME;
    slash[1] = 0;
    if (wcslen(root) + 32 >= 32768) return ERROR_BAD_PATHNAME;
    wcscpy(python, root); wcscat(python, L"Python\\python.exe");
    wcscpy(script, root); wcscat(script, L"yt-dlp.pyz");
    if (GetFileAttributesW(python) == INVALID_FILE_ATTRIBUTES || GetFileAttributesW(script) == INVALID_FILE_ATTRIBUTES) return ERROR_FILE_NOT_FOUND;
    if (!quote(python) || !append(L' ') || !append(L'-') || !append(L'I') || !append(L' ') || !quote(script)) return ERROR_BAD_LENGTH;
    for (int i = 1; i < argc; i++) if (!append(L' ') || !quote(argv[i])) return ERROR_BAD_LENGTH;
    STARTUPINFOW start = {0}; PROCESS_INFORMATION child = {0};
    start.cb = sizeof(start); start.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
    start.wShowWindow = SW_HIDE;
    start.hStdInput = GetStdHandle(STD_INPUT_HANDLE); start.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE); start.hStdError = GetStdHandle(STD_ERROR_HANDLE);
    if (!CreateProcessW(python, command, NULL, NULL, TRUE, CREATE_NO_WINDOW | BELOW_NORMAL_PRIORITY_CLASS, NULL, NULL, &start, &child)) return (int)GetLastError();
    CloseHandle(child.hThread);
    DWORD result = ERROR_PROCESS_ABORTED;
    if (WaitForSingleObject(child.hProcess, INFINITE) == WAIT_OBJECT_0) GetExitCodeProcess(child.hProcess, &result);
    CloseHandle(child.hProcess);
    return (int)result;
}
