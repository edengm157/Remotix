#include "MenuRequestHandler.h"
#include "RoomRequestHandler.h"
#include <iostream>
#include "LoginRequestHandler.h"


MenuRequestHandler::MenuRequestHandler(LoggedUser& loggedUser, RequestHandlerFactory& factory)
    : m_user(loggedUser)
    , m_handlerFactory(factory)
{
}

bool MenuRequestHandler::isRequestRelevant(const RequestInfo& requestInfo)
{
    char code = requestInfo.id;
    // Only LOGOUT, CREATE_ROOM, JOIN_ROOM
    return code >= static_cast<char>(RequestCodes::LOGOUT) &&
        code <= static_cast<char>(RequestCodes::JOIN_ROOM);
}

RequestResult MenuRequestHandler::handleRequest(const RequestInfo& requestInfo)
{
    RequestCodes code = static_cast<RequestCodes>(requestInfo.id);

    try
    {
        switch (code)
        {
        case RequestCodes::LOGOUT:
            return logout(requestInfo);

        case RequestCodes::CREATE_ROOM:
            return createRoom(requestInfo);

        case RequestCodes::JOIN_ROOM:
            return joinRoom(requestInfo);

        default:
            ErrorResponse errorResponse{ "Invalid menu request" };
            RequestResult result;
            result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
            result.newHandler = this;
            return result;
        }
    }
    catch (std::exception& e)
    {
        std::cerr << "Error handling menu request: " << e.what() << std::endl;
        ErrorResponse errorResponse{ std::string("An error occurred: ") + e.what() };
        RequestResult result;
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = this;
        return result;
    }
}

RequestResult MenuRequestHandler::logout(const RequestInfo& requestInfo)
{
    RequestResult result;

    LoginManager& loginManager = m_handlerFactory.getLoginManager();
    loginManager.logout(m_user.getUsername());

    result.newHandler = m_handlerFactory.createLoginRequestHandler();
    result.response = JsonResponsePacketSerializer::serializeResponse(LogoutResponse{ 1 });

    delete this;
    return result;
}

RequestResult MenuRequestHandler::createRoom(const RequestInfo& requestInfo)
{
    RequestResult result;

    try
    {
        RoomManager& roomManager = m_handlerFactory.getRoomManager();

        // Create room and get the assigned ID
        unsigned int roomId = roomManager.createRoom(m_user);

        std::cout << "Room " << roomId << " created by " << m_user.getUsername() << std::endl;

        CreateRoomResponse response{ 1, roomId };
        result.response = JsonResponsePacketSerializer::serializeResponse(response);

        // Creator stays in menu - doesn't automatically enter the room
        // They need to explicitly JOIN_ROOM with the returned ID if they want to enter
        result.newHandler = this;
    }
    catch (RoomException& e)
    {
        ErrorResponse errorResponse{ e.what() };
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = this;
    }

    return result;
}

RequestResult MenuRequestHandler::joinRoom(const RequestInfo& requestInfo)
{
    RequestResult result;

    try
    {
        JoinRoomRequest request = JsonRequestDeserializer::deserializeJoinRoomRequest(requestInfo.buffer);
        RoomManager& roomManager = m_handlerFactory.getRoomManager();

        Room& room = roomManager.getRoom(request.roomId);
        room.addUser(m_user);

        std::cout << m_user.getUsername() << " joined room " << request.roomId << std::endl;

        JoinRoomResponse response{ 1 };
        result.response = JsonResponsePacketSerializer::serializeResponse(response);

        // Transition to RoomRequestHandler - user is now "inside" the room
        result.newHandler = new RoomRequestHandler(m_user, request.roomId, m_handlerFactory);
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