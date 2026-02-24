#pragma once
#include "IRequestHandler.h"
#include "RequestHandlerFactory.h"
#include "JsonRequestDeserializer.h"
#include "JsonResponsePacketSerializer.h"
#include "Protocol.h"

class LoginRequestHandler : public IRequestHandler
{
public:
    LoginRequestHandler(RequestHandlerFactory& requestHandlerFactory);
    virtual ~LoginRequestHandler() = default;

    bool isRequestRelevant(const RequestInfo& requestInfo) override;
    RequestResult handleRequest(const RequestInfo& requestInfo) override;

private:
    RequestHandlerFactory& m_handlerFactory;

    RequestResult login(const RequestInfo& requestInfo);
    RequestResult signup(const RequestInfo& requestInfo);

    std::string fetchMessageOfStatus(int statusCode);
};