#pragma once
#include "LoginManager.h"
#include "RoomManager.h"
#include "SqliteDataBase.h"

// Forward declarations
class LoginRequestHandler;
class MenuRequestHandler;
class LoggedUser;

class RequestHandlerFactory
{
public:
    RequestHandlerFactory();
    ~RequestHandlerFactory();

    LoginRequestHandler* createLoginRequestHandler();
    MenuRequestHandler* createMenuRequestHandler(LoggedUser& loggedUser);

    LoginManager& getLoginManager();
    RoomManager& getRoomManager();

private:
    LoginManager m_loginManager;
    RoomManager m_roomManager;
    LoginRequestHandler* m_loginRequestHandler;
};