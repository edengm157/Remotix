#pragma once
#include "IRequestHandler.h"
#include "RequestHandlerFactory.h"
#include "JsonRequestDeserializer.h"
#include "JsonResponsePacketSerializer.h"
#include "Protocol.h"
#include "LoggedUser.h"

class RoomRequestHandler : public IRequestHandler
{
public:
    RoomRequestHandler(LoggedUser& loggedUser, unsigned int roomId, RequestHandlerFactory& factory);
    virtual ~RoomRequestHandler() = default;

    bool isRequestRelevant(const RequestInfo& requestInfo) override;
    RequestResult handleRequest(const RequestInfo& requestInfo) override;

private:
    LoggedUser m_user;
    unsigned int m_roomId;
    RequestHandlerFactory& m_handlerFactory;

    // Handler functions
    RequestResult leaveRoom(const RequestInfo& requestInfo);

    // TODO: Add future handlers here for H.264 streaming
    // RequestResult handleStreamFrame(const RequestInfo& requestInfo);
};