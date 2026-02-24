#pragma once
#include <vector>
#include <chrono>

// Forward declaration
class IRequestHandler;

typedef std::vector<char> buffer;

typedef struct {
    char id;
    time_t receivalTime;
    buffer buffer;
} RequestInfo;

typedef struct {
    buffer response;
    IRequestHandler* newHandler;
} RequestResult;