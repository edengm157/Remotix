#include "RequestHandlerFactory.h"
#include "LoginRequestHandler.h"
#include "MenuRequestHandler.h"

RequestHandlerFactory::RequestHandlerFactory()
    : m_loginManager(new SqliteDataBase())
    , m_roomManager()
    , m_loginRequestHandler(nullptr)
{
}

RequestHandlerFactory::~RequestHandlerFactory()
{
    if (m_loginRequestHandler != nullptr)
    {
        delete m_loginRequestHandler;
        m_loginRequestHandler = nullptr;
    }
}

LoginRequestHandler* RequestHandlerFactory::createLoginRequestHandler()
{
    if (m_loginRequestHandler == nullptr)
    {
        m_loginRequestHandler = new LoginRequestHandler(*this);
    }
    return m_loginRequestHandler;
}

MenuRequestHandler* RequestHandlerFactory::createMenuRequestHandler(LoggedUser& loggedUser)
{
    return new MenuRequestHandler(loggedUser, *this);
}

LoginManager& RequestHandlerFactory::getLoginManager()
{
    return m_loginManager;
}

RoomManager& RequestHandlerFactory::getRoomManager()
{
    return m_roomManager;
}