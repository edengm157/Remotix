#include "LoginRequestHandler.h"
#include "MenuRequestHandler.h"
#include <iostream>

LoginRequestHandler::LoginRequestHandler(RequestHandlerFactory& requestHandlerFactory)
    : m_handlerFactory(requestHandlerFactory)
{
}

bool LoginRequestHandler::isRequestRelevant(const RequestInfo& requestInfo)
{
    return requestInfo.id == static_cast<char>(RequestCodes::LOGIN) ||
        requestInfo.id == static_cast<char>(RequestCodes::SIGNUP);
}

RequestResult LoginRequestHandler::handleRequest(const RequestInfo& requestInfo)
{
    try
    {
        RequestCodes code = static_cast<RequestCodes>(requestInfo.id);

        switch (code)
        {
        case RequestCodes::LOGIN:
            return login(requestInfo);

        case RequestCodes::SIGNUP:
            return signup(requestInfo);

        default:
            ErrorResponse errorResponse{ "Invalid request" };
            RequestResult result;
            result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
            result.newHandler = this;
            return result;
        }
    }
    catch (std::exception& e)
    {
        std::cerr << "Error in login handler: " << e.what() << std::endl;
        ErrorResponse errorResponse{ "An error occurred while processing the request" };
        RequestResult result;
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = this;
        return result;
    }
}

RequestResult LoginRequestHandler::login(const RequestInfo& requestInfo)
{
    RequestResult result;

    LoginRequest request = JsonRequestDeserializer::deserializeLoginRequest(requestInfo.buffer);
    LoginManager& loginManager = m_handlerFactory.getLoginManager();

    int statusCode = loginManager.login(request.username, request.password);

    if (statusCode == static_cast<int>(LoginStatus::SUCCESS))
    {
        LoggedUser loggedUser(request.username);
        LoginResponse response{ 1 };

        result.response = JsonResponsePacketSerializer::serializeResponse(response);
        result.newHandler = new MenuRequestHandler(loggedUser, m_handlerFactory);
    }
    else
    {
        ErrorResponse errorResponse{ fetchMessageOfStatus(statusCode) };
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = m_handlerFactory.createLoginRequestHandler();
    }

    return result;
}

RequestResult LoginRequestHandler::signup(const RequestInfo& requestInfo)
{
    RequestResult result;

    SignupRequest request = JsonRequestDeserializer::deserializeSignupRequest(requestInfo.buffer);
    LoginManager& loginManager = m_handlerFactory.getLoginManager();

    int statusCode = loginManager.signup(request.username, request.password);

    if (statusCode == static_cast<int>(LoginStatus::SUCCESS))
    {
        LoggedUser loggedUser(request.username);
        SignupResponse response{ 1 };

        result.response = JsonResponsePacketSerializer::serializeResponse(response);
        result.newHandler = new MenuRequestHandler(loggedUser, m_handlerFactory);
    }
    else
    {
        ErrorResponse errorResponse{ fetchMessageOfStatus(statusCode) };
        result.response = JsonResponsePacketSerializer::serializeResponse(errorResponse);
        result.newHandler = new LoginRequestHandler(m_handlerFactory);
    }

    return result;
}

std::string LoginRequestHandler::fetchMessageOfStatus(int statusCode)
{
    switch (statusCode)
    {
    case static_cast<int>(LoginStatus::USER_EXISTS):
        return "User already exists";

    case static_cast<int>(LoginStatus::USER_MISSING):
        return "User does not exist";

    case static_cast<int>(LoginStatus::MISSING_FIELDS):
        return "Username and password cannot be empty";

    case static_cast<int>(LoginStatus::WRONG_USER_DETAILS):
        return "Incorrect password";

    default:
        return "Unknown error";
    }
}