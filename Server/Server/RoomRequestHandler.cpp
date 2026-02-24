#include "RoomRequestHandler.h"
#include "MenuRequestHandler.h"
#include <iostream>

RoomRequestHandler::RoomRequestHandler(LoggedUser& loggedUser, unsigned int roomId, RequestHandlerFactory& factory)
    : m_user(loggedUser)
    , m_roomId(roomId)
    , m_handlerFactory(factory)
{
    std::cout << "User " << m_user.getUsername() << " entered room " << m_roomId << std::endl;
}

bool RoomRequestHandler::isRequestRelevant(const RequestInfo& requestInfo)
{
    // For now, only LEAVE_ROOM is relevant
    // In the future, add STREAM_FRAME, etc.
    return requestInfo.id == static_cast<char>(RequestCodes::LEAVE_ROOM);
}

RequestResult RoomRequestHandler::handleRequest(const RequestInfo& requestInfo)
{
    RequestCodes code = static_cast<RequestCodes>(requestInfo.id);

    try
    {
        switch (code)
        {
        case RequestCodes::LEAVE_ROOM:
            return leaveRoom(requestInfo);

            // TODO: Add future cases here
            // case RequestCodes::STREAM_FRAME:
            //     return handleStreamFrame(requestInfo);

        default:
            ErrorResponse errorResponse{ "Invalid request for room handler" };
            RequestResult result;
            result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
            result.newHandler = this;
            return result;
        }
    }
    catch (std::exception& e)
    {
        std::cerr << "Error handling room request: " << e.what() << std::endl;
        ErrorResponse errorResponse{ std::string("An error occurred: ") + e.what() };
        RequestResult result;
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = this;
        return result;
    }
}

RequestResult RoomRequestHandler::leaveRoom(const RequestInfo& requestInfo)
{
    RequestResult result;

    try
    {
        RoomManager& roomManager = m_handlerFactory.getRoomManager();
        Room& room = roomManager.getRoom(m_roomId);

        // If the user leaving is the creator, delete the room
        if (room.getCreator() == m_user.getUsername())
        {
            std::cout << "Creator " << m_user.getUsername() << " left, deleting room " << m_roomId << std::endl;
            roomManager.deleteRoom(m_roomId);
        }
        else
        {
            room.removeUser(m_user);
            std::cout << m_user.getUsername() << " left room " << m_roomId << std::endl;
        }

        LeaveRoomResponse response{ 1 };
        result.response = JsonResponsePacketSerializer::serializeResponse(response);

        // Return to menu
        result.newHandler = new MenuRequestHandler(m_user, m_handlerFactory);
        delete this;
    }
    catch (RoomException& e)
    {
        ErrorResponse errorResponse{ e.what() };
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = this;
    }

    return result;
}