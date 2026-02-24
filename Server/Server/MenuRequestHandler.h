#pragma once
#include "IRequestHandler.h"
#include "RequestHandlerFactory.h"
#include "JsonRequestDeserializer.h"
#include "JsonResponsePacketSerializer.h"
#include "Protocol.h"
#include "LoggedUser.h"


class MenuRequestHandler : public IRequestHandler
{
public:
    MenuRequestHandler(LoggedUser& loggedUser, RequestHandlerFactory& factory);
    virtual ~MenuRequestHandler() = default;

    bool isRequestRelevant(const RequestInfo& requestInfo) override;
    RequestResult handleRequest(const RequestInfo& requestInfo) override;

private:
    LoggedUser m_user;
    RequestHandlerFactory& m_handlerFactory;

    // Handler functions (only 3 menu options)
    RequestResult logout(const RequestInfo& requestInfo);
    RequestResult createRoom(const RequestInfo& requestInfo);
    RequestResult joinRoom(const RequestInfo& requestInfo);
};