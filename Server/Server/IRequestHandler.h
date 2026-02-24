#pragma once
#include "HandlerStructures.h"

class IRequestHandler
{
public:
    virtual ~IRequestHandler() = default;
    virtual bool isRequestRelevant(const RequestInfo& requestInfo) = 0;
    virtual RequestResult handleRequest(const RequestInfo& requestInfo) = 0;
};